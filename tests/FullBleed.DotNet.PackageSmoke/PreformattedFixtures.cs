using FullBleed.DotNet;
using System.Security.Cryptography;
using System.Text.Json;

internal static class PreformattedFixtures
{
    internal static void Run(string assets, string outputDirectory, string version)
    {
        var bytes = File.ReadAllBytes(Path.Combine(assets, "preformatted-whitespace-2.5.41.json"));
        using var source = JsonDocument.Parse(bytes);
        var root = source.RootElement;
        var font = Path.Combine(assets, "verification-fonts", "NotoSans-Regular.ttf");
        if (Hash(File.ReadAllBytes(font)) != root.GetProperty("fontSha256").GetString())
            throw new InvalidOperationException("Preformatted fixtures require their exact reference font.");
        var output = Path.Combine(outputDirectory, "preformatted-whitespace");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "inputs.json"), bytes);
        using var engine = new FullBleedEngine(new FullBleedEngineOptions
        {
            Assets = [FullBleedAsset.FromPath(font, FullBleedAssetKind.Font)],
        });
        var cases = new List<object>();
        foreach (var fixture in root.GetProperty("cases").EnumerateArray())
        {
            var name = fixture.GetProperty("name").GetString()!;
            var html = fixture.GetProperty("html").GetString()!;
            var css = fixture.GetProperty("css").GetString()!;
            var template = fixture.GetProperty("template").GetString()!;
            var content = fixture.GetProperty("text").GetString()!;
            foreach (var mode in new[] { "direct", "compiled", "reflow" })
            {
                var directory = mode + "-" + name;
                var folder = Path.Combine(output, directory);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "input.html"), html);
                File.WriteAllText(Path.Combine(folder, "style.css"), css);
                var pdf = Render(engine, html, css, template, content, mode);
                if (!pdf.SequenceEqual(Render(engine, html, css, template, content, mode)))
                    throw new InvalidOperationException("Preformatted PDF changed on repeat.");
                var path = Path.Combine(folder, "document.pdf");
                File.WriteAllBytes(path, pdf);
                var pages = FullBleedEngine.InspectPdf(path).PageCount;
                var previews = engine.RenderFinalizedPdfImagePagesToDirectory(path, folder, dpi: 144, stem: "native");
                if (previews.Paths.Count != pages) throw new InvalidOperationException("Preformatted preview page count differs.");
                cases.Add(new
                {
                    name, mode, directory, pages, pdfSha256 = Hash(pdf), repeatPdfBytesMatch = true,
                    previewFiles = previews.Paths.Select(p => Path.GetRelativePath(folder, p).Replace('\\', '/')).ToArray(),
                    previewSha256 = previews.Paths.Select(p => Hash(File.ReadAllBytes(p))).ToArray(),
                });
            }
        }
        if (cases.Count != 51) throw new InvalidOperationException("Incomplete preformatted fixture set.");
        File.WriteAllText(Path.Combine(output, "renders.json"), JsonSerializer.Serialize(new
        {
            packageVersion = version, runtime = Environment.Version.ToString(),
            inputSha256 = Hash(bytes), fontSha256 = Hash(File.ReadAllBytes(font)), cases,
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] Render(FullBleedEngine engine, string html, string css, string template, string content, string mode)
    {
        if (mode == "direct")
        {
            var result = engine.RenderPdfWithDiagnostics(html, css);
            if (result.Diagnostics.MissingGlyphs.Count != 0) throw new InvalidOperationException("Missing glyph in preformatted fixture.");
            return result.Pdf;
        }
        using var compiled = engine.Compile(mode == "compiled" ? html : template, css);
        if (mode == "compiled") return compiled.Render();
        return compiled.RenderReflowBindings(new Dictionary<string, IReadOnlyList<string>>
        {
            ["content"] = new[] { content },
        }, CompiledFlowCompression.Throughput);
    }
}
