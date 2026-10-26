using FullBleed.DotNet;
using System.Security.Cryptography;
using System.Text.Json;

internal static class PaginationFixtures
{
    internal static void Run(string assets, string outputDirectory, string version)
    {
        var output = Path.Combine(outputDirectory, "pagination");
        Directory.CreateDirectory(output);
        var source = Path.Combine(assets, "pagination-2.5.35.json");
        using var fixtures = JsonDocument.Parse(File.ReadAllText(source));
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
                File.WriteAllText(Path.Combine(folder, "input.html"), html);
                File.WriteAllText(Path.Combine(folder, "style.css"), css);
                using var engine = new FullBleedEngine();
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
                var previews = engine.RenderFinalizedPdfImagePagesToDirectory(path, folder, dpi: 96, stem: "preview");
                var htmlPreviews = engine.RenderImagePagesToDirectory(html, css, Path.Combine(folder, "html"), dpi: 96, stem: "preview");
                if (previews.Paths.Count != pages || htmlPreviews.Paths.Count != pages)
                {
                    throw new InvalidOperationException($"Missing pagination previews: {name}");
                }
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
            schema = "fullbleed.dotnet.pagination.renders.v1",
            packageVersion = version, sourceSha256 = HashFile(source),
            runtime = Environment.Version.ToString(), cases,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
