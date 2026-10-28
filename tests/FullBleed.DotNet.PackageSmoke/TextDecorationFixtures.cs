using FullBleed.DotNet;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class TextDecorationFixtures
{
    internal static void Run(string assets, string outputDirectory, string version, bool directOnly = false)
    {
        var sourceBytes = File.ReadAllBytes(Path.Combine(assets, "text-decorations-2.5.40.json"));
        using var source = JsonDocument.Parse(sourceBytes);
        var root = source.RootElement;
        var font = Path.Combine(assets, "fonts", "Inter-Variable.ttf");
        if (HashFile(font) != root.GetProperty("fontSha256").GetString())
        {
            throw new InvalidOperationException("The decoration fixtures require their exact reference font.");
        }
        var output = Path.Combine(outputDirectory, "text-decorations");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "inputs.json"), sourceBytes);
        using var engine = new FullBleedEngine(new FullBleedEngineOptions
        {
            Assets = [FullBleedAsset.FromPath(font, FullBleedAssetKind.Font)],
        });
        var modes = directOnly ? new[] { "direct" } : new[] { "direct", "compiled", "fixed", "reflow", "compact" };
        var cases = new List<object>();
        foreach (var fixture in root.GetProperty("cases").EnumerateArray())
        {
            var name = fixture.GetProperty("name").GetString()!;
            var html = fixture.GetProperty("html").GetString()!;
            var css = fixture.GetProperty("css").GetString()!;
            if (Hash(Encoding.UTF8.GetBytes(html)) != fixture.GetProperty("htmlSha256").GetString() ||
                Hash(Encoding.UTF8.GetBytes(css)) != fixture.GetProperty("cssSha256").GetString())
            {
                throw new InvalidOperationException("The authored decoration input changed.");
            }
            foreach (var mode in modes)
            {
                var directory = name + "-" + mode;
                var folder = Path.Combine(output, directory);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "input.html"), html);
                File.WriteAllText(Path.Combine(folder, "style.css"), css);
                var pdf = Render(engine, html, css, mode);
                var path = Path.Combine(folder, "document.pdf");
                File.WriteAllBytes(path, pdf);
                var undecorated = Render(engine, html, css + ".dec {text-decoration:none}", mode);
                File.WriteAllBytes(Path.Combine(folder, "undecorated.pdf"), undecorated);
                var pages = FullBleedEngine.InspectPdf(path).PageCount;
                var previews = engine.RenderFinalizedPdfImagePagesToDirectory(path, folder, dpi: 144, stem: "finalized");
                var expectedPages = mode is "direct" or "compiled" ? 1 : 2;
                if (pages != expectedPages || previews.Paths.Count != expectedPages)
                {
                    throw new InvalidOperationException("Unexpected decoration page count: " + directory);
                }
                string? nativeSha256 = null;
                if (mode == "direct")
                {
                    var native = engine.RenderImagePagesToDirectory(
                        html.Replace("{{record}}", "AAA", StringComparison.Ordinal), css, folder, dpi: 144, stem: "direct");
                    if (native.Paths.Count != 1)
                    {
                        throw new InvalidOperationException("Unexpected direct decoration preview count.");
                    }
                    File.Copy(native.Paths[0], Path.Combine(folder, "native.png"));
                    nativeSha256 = HashFile(Path.Combine(folder, "native.png"));
                }
                string? controlSha256 = null;
                if (fixture.TryGetProperty("controlHtml", out var controlHtml))
                {
                    var control = Render(engine, controlHtml.GetString()!, css, mode);
                    File.WriteAllBytes(Path.Combine(folder, "control.pdf"), control);
                    controlSha256 = Hash(control);
                }
                cases.Add(new
                {
                    name, mode, directory, pages, pdfSha256 = Hash(pdf), undecoratedSha256 = Hash(undecorated),
                    htmlSha256 = HashFile(Path.Combine(folder, "input.html")), cssSha256 = HashFile(Path.Combine(folder, "style.css")),
                    previewFiles = previews.Paths.Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/')).ToArray(),
                    previewsSha256 = previews.Paths.Select(HashFile).ToArray(), nativeSha256, controlSha256,
                    repeatPdfBytesIdentical = true,
                });
            }
        }
        if (cases.Count != (directOnly ? 16 : 80))
        {
            throw new InvalidOperationException("Unexpected decoration fixture count.");
        }
        File.WriteAllText(Path.Combine(output, "renders.json"), JsonSerializer.Serialize(new
        {
            schema = "fullbleed.dotnet.text_decorations.renders.v1", packageVersion = version,
            runtime = Environment.Version.ToString(), sourceSha256 = Hash(sourceBytes), fontSha256 = HashFile(font), cases,
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static byte[] Render(FullBleedEngine engine, string html, string css, string mode)
    {
        if (mode == "direct")
        {
            var authored = html.Replace("{{record}}", "AAA", StringComparison.Ordinal);
            var result = engine.RenderPdfWithDiagnostics(authored, css);
            if (result.Diagnostics.MissingGlyphs.Count != 0 || !result.Pdf.SequenceEqual(engine.RenderPdf(authored, css)))
            {
                throw new InvalidOperationException("Decoration direct rendering failed glyph or repeatability checks.");
            }
            return result.Pdf;
        }
        using var compiled = engine.Compile(mode == "compiled" ? html.Replace("{{record}}", "AAA", StringComparison.Ordinal) : html, css);
        var records = new Dictionary<string, IReadOnlyList<string>> { ["record"] = new[] { "AAA", "BBB" } };
        Func<byte[]> action = mode switch
        {
            "compiled" => () => compiled.Render(),
            "fixed" => () => compiled.RenderBindings(records),
            "reflow" => () => compiled.RenderReflowBindings(records, CompiledFlowCompression.Throughput),
            "compact" => () => compiled.RenderReflowBindings(records, CompiledFlowCompression.Compact),
            _ => throw new ArgumentException("Unknown decoration mode: " + mode),
        };
        var data = action();
        if (!data.SequenceEqual(action()))
        {
            throw new InvalidOperationException("Compiled decoration output was not repeatable.");
        }
        return data;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string HashFile(string path) => Hash(File.ReadAllBytes(path));
}
