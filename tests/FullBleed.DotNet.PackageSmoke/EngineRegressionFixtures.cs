using FullBleed.DotNet;
using System.Security.Cryptography;
using System.Text.Json;

internal static class EngineRegressionFixtures
{
    internal static void Run(string assets, string outputDirectory, string version)
    {
        var output = Path.Combine(outputDirectory, "engine-regressions");
        Directory.CreateDirectory(output);
        var source = Path.Combine(assets, "engine-2.5.22.json");
        using var fixtures = JsonDocument.Parse(File.ReadAllText(source));
        var font = Path.Combine(assets, "verification-fonts", "NotoSans-Regular.ttf");
        if (HashFile(font) != fixtures.RootElement.GetProperty("font").GetProperty("sha256").GetString())
        {
            throw new InvalidOperationException("The engine fixtures require their exact reference font.");
        }
        var features = FullBleedEngine.GetNativeFeatures();
        if (!features.SvgRaster) { throw new InvalidOperationException("SVG raster fallback is required."); }
        var cases = new List<object>();
        foreach (var mode in new[] { "direct", "compiled" })
        {
            foreach (var fixture in fixtures.RootElement.GetProperty("cases").EnumerateArray())
            {
                var name = fixture.GetProperty("name").GetString()!;
                var directory = mode + "-" + name;
                var folder = Path.Combine(output, directory);
                Directory.CreateDirectory(folder);
                var html = fixture.GetProperty("html").GetString()!;
                var css = fixture.GetProperty("css").GetString()!;
                var dpi = fixture.GetProperty("previewDpi").GetUInt32();
                var fonts = fixture.TryGetProperty("fontFiles", out var fontFiles)
                    ? fontFiles.EnumerateArray().Select(item => FullBleedAsset.FromPath(
                        Path.Combine(assets, "verification-fonts", item.GetString()!), FullBleedAssetKind.Font)).ToList()
                    : [];
                using var engine = new FullBleedEngine(new FullBleedEngineOptions { Assets = fonts });
                File.WriteAllText(Path.Combine(folder, "input.html"), html);
                File.WriteAllText(Path.Combine(folder, "style.css"), css);
                byte[] pdf;
                if (mode == "direct")
                {
                    var result = engine.RenderPdfWithDiagnostics(html, css);
                    pdf = result.Pdf;
                    if (result.Diagnostics.MissingGlyphs.Count != 0 || !pdf.SequenceEqual(engine.RenderPdf(html, css)))
                    {
                        throw new InvalidOperationException($"Missing glyphs or non-repeatable direct output: {name}");
                    }
                }
                else
                {
                    using var compiled = engine.Compile(html, css);
                    pdf = compiled.Render();
                    if (!pdf.SequenceEqual(compiled.Render()))
                    {
                        throw new InvalidOperationException($"Non-repeatable compiled output: {name}");
                    }
                }
                var path = Path.Combine(folder, "document.pdf");
                File.WriteAllBytes(path, pdf);
                var pages = FullBleedEngine.InspectPdf(path).PageCount;
                var previews = engine.RenderFinalizedPdfImagePagesToDirectory(path, folder, dpi: dpi, stem: "preview");
                if (previews.Paths.Count != pages) { throw new InvalidOperationException($"Missing saved-PDF previews: {name}"); }
                var htmlPreviews = engine.RenderImagePagesToDirectory(html, css, Path.Combine(folder, "html"), dpi: dpi, stem: "preview");
                if (htmlPreviews.Paths.Count != pages) { throw new InvalidOperationException($"Missing HTML previews: {name}"); }
                cases.Add(new
                {
                    name, mode, directory, pages,
                    expectedPages = fixture.GetProperty("pages").GetInt32(),
                    htmlSha256 = HashFile(Path.Combine(folder, "input.html")),
                    cssSha256 = HashFile(Path.Combine(folder, "style.css")),
                    pdfSha256 = HashFile(path),
                    previewFiles = previews.Paths.Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/')).ToArray(),
                    previewsSha256 = previews.Paths.Select(HashFile).ToArray(),
                    htmlPreviewFiles = htmlPreviews.Paths.Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/')).ToArray(),
                    htmlPreviewsSha256 = htmlPreviews.Paths.Select(HashFile).ToArray(),
                    repeatPdfBytesIdentical = true,
                });
            }
        }
        File.WriteAllText(Path.Combine(output, "renders.json"), JsonSerializer.Serialize(new
        {
            schema = "fullbleed.dotnet.engine_regressions.renders.v1", ok = true,
            packageVersion = version, sourceSha256 = HashFile(source), fontSha256 = HashFile(font),
            runtime = Environment.Version.ToString(), svgRaster = features.SvgRaster, cases,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
