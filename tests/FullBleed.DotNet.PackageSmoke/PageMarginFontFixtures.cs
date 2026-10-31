using FullBleed.DotNet;
using System.Security.Cryptography;
using System.Text.Json;

internal static class PageMarginFontFixtures
{
    internal static void Run(string assets, string outputDirectory, string version)
    {
        var bytes = File.ReadAllBytes(Path.Combine(assets, "page-margin-fonts-2.5.48.json"));
        using var source = JsonDocument.Parse(bytes);
        var output = Path.Combine(outputDirectory, "page-margin-fonts");
        Directory.CreateDirectory(Path.Combine(output, "fonts"));
        File.WriteAllBytes(Path.Combine(output, "inputs.json"), bytes);
        var paths = new Dictionary<string, string>
        {
            ["Body.ttf"] = Path.Combine(assets, "verification-fonts", "NotoSans-Regular.ttf"),
            ["Display.ttf"] = Path.Combine(assets, "fonts", "DMSerifDisplay-Regular.ttf"),
        };
        var fonts = new List<FullBleedAsset>();
        foreach (var font in source.RootElement.GetProperty("fonts").EnumerateArray())
        {
            var name = font.GetProperty("name").GetString()!;
            var data = File.ReadAllBytes(paths[name]);
            if (Hash(data) != font.GetProperty("sha256").GetString())
                throw new InvalidOperationException("Page-font input changed.");
            File.WriteAllBytes(Path.Combine(output, "fonts", name), data);
            fonts.Add(FullBleedAsset.FromBytes(data, FullBleedAssetKind.Font, name));
        }
        File.Copy(Path.Combine(assets, "verification-fonts", "NotoSans-OFL.txt"), Path.Combine(output, "fonts", "Body-OFL.txt"));
        File.Copy(Path.Combine(assets, "fonts", "DMSerifDisplay-OFL.txt"), Path.Combine(output, "fonts", "Display-OFL.txt"));
        using var engine = new FullBleedEngine(new FullBleedEngineOptions { Assets = fonts });
        var cases = new List<object>();
        foreach (var fixture in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = fixture.GetProperty("name").GetString()!;
            foreach (var mode in new[] { "direct", "compiled", "fixed", "reflow", "compact" })
            {
                var html = fixture.GetProperty("html").GetString()!;
                var records = mode is "direct" or "compiled" ? new[] { "Alpha" } : new[] { "Alpha", "Bravo" };
                if (records.Length == 2) html = html.Replace("Alpha", "{{name}}", StringComparison.Ordinal);
                foreach (var kind in new[] { "alias", "control" })
                {
                    var css = fixture.GetProperty(kind + "Css").GetString()!;
                    var directory = mode + "-" + name + "/" + kind;
                    var folder = Path.Combine(output, directory);
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "input.html"), html);
                    File.WriteAllText(Path.Combine(folder, "style.css"), css);
                    File.WriteAllText(Path.Combine(folder, "records.json"), JsonSerializer.Serialize(records));
                    var pdf = Render(engine, html, css, mode, records);
                    if (!pdf.SequenceEqual(Render(engine, html, css, mode, records)))
                        throw new InvalidOperationException("Page-font PDF changed on repeat.");
                    var path = Path.Combine(folder, "document.pdf");
                    File.WriteAllBytes(path, pdf);
                    var pages = FullBleedEngine.InspectPdf(path).PageCount;
                    var previews = engine.RenderFinalizedPdfImagePagesToDirectory(path, folder, dpi: 96, stem: "native");
                    var expectedPages = fixture.GetProperty("footerFaces").GetArrayLength() * records.Length;
                    if (pages != expectedPages || previews.Paths.Count != expectedPages)
                        throw new InvalidOperationException("Unexpected page-font page or preview count.");
                    cases.Add(new
                    {
                        name, mode, kind, directory, pages, pdfSha256 = Hash(pdf), repeatPdfBytesMatch = true,
                        previewFiles = previews.Paths.Select(p => Path.GetRelativePath(folder, p).Replace('\\', '/')).ToArray(),
                        previewSha256 = previews.Paths.Select(p => Hash(File.ReadAllBytes(p))).ToArray(),
                    });
                }
            }
        }
        if (cases.Count != 80) throw new InvalidOperationException("Incomplete page-font fixture set.");
        File.WriteAllText(Path.Combine(output, "renders.json"), JsonSerializer.Serialize(new
        {
            packageVersion = version, runtime = Environment.Version.ToString(), inputSha256 = Hash(bytes), cases,
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] Render(FullBleedEngine engine, string html, string css, string mode, string[] records)
    {
        if (mode == "direct") return engine.RenderPdf(html, css);
        using var compiled = engine.Compile(html, css);
        if (mode == "compiled") return compiled.Render();
        if (mode == "fixed") return compiled.RenderBindings(records, map => map.Bind("name", record => record));
        return compiled.RenderReflowBindings(records, map => map.Bind("name", record => record),
            mode == "compact" ? CompiledFlowCompression.Compact : CompiledFlowCompression.Throughput);
    }
}
