using FullBleed.DotNet;
using System.Security.Cryptography;
using System.Text.Json;

internal static class TaggedStructureFixtures
{
    internal static void Run(string assets, string outputDirectory, string version)
    {
        var output = Path.Combine(outputDirectory, "tagged-structure");
        Directory.CreateDirectory(output);
        var html = File.ReadAllText(Path.Combine(assets, "tagged-structure.html")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var css = File.ReadAllText(Path.Combine(assets, "tagged-structure.css")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var fontPath = Path.Combine(assets, "fonts", "Inter-Variable.ttf");
        File.WriteAllText(Path.Combine(output, "input.html"), html);
        File.WriteAllText(Path.Combine(output, "style.css"), css);
        var cases = new List<object>();
        foreach (var (name, profile) in new[] { ("none", PdfProfile.None), ("ua1", PdfProfile.PdfUa1), ("ua2", PdfProfile.PdfUa2) })
        {
            var folder = Path.Combine(output, name);
            Directory.CreateDirectory(folder);
            using var engine = new FullBleedEngine(new FullBleedEngineOptions
            {
                PdfProfile = profile,
                DocumentLanguage = "en-US",
                DocumentTitle = "Tagged structure regression specimen",
                Assets = [FullBleedAsset.FromPath(fontPath, FullBleedAssetKind.Font)],
            });
            var rendered = engine.RenderPdfWithDiagnostics(html, css);
            if (rendered.Diagnostics.MissingGlyphs.Count != 0 || !rendered.Pdf.SequenceEqual(engine.RenderPdf(html, css)))
            {
                throw new InvalidOperationException($"Missing glyphs or non-repeatable tagged output: {name}");
            }
            var pdf = Path.Combine(folder, "document.pdf");
            File.WriteAllBytes(pdf, rendered.Pdf);
            var inspection = FullBleedEngine.InspectPdf(pdf);
            if (inspection.PageCount != 1 || (profile != PdfProfile.None && !inspection.Profile.StructTreeRootPresent))
            {
                throw new InvalidOperationException($"Missing structure tree or unexpected page count: {name}");
            }
            var previews = engine.RenderFinalizedPdfImagePagesToDirectory(pdf, folder, dpi: 96, stem: "preview");
            if (previews.Paths.Count != 1 || !rendered.Pdf.SequenceEqual(File.ReadAllBytes(pdf)))
            {
                throw new InvalidOperationException($"Unexpected preview or changed PDF: {name}");
            }
            cases.Add(new
            {
                name, pdfSha256 = HashFile(pdf), previewSha256 = HashFile(previews.Paths[0]),
                previewFile = Path.GetFileName(previews.Paths[0]), repeatPdfBytesIdentical = true,
                inspection, diagnostics = rendered.Diagnostics,
            });
        }
        File.WriteAllText(Path.Combine(output, "renders.json"), JsonSerializer.Serialize(new
        {
            schema = "fullbleed.dotnet.tagged_structure.v1", packageVersion = version,
            htmlSha256 = HashFile(Path.Combine(output, "input.html")),
            cssSha256 = HashFile(Path.Combine(output, "style.css")),
            embeddedFontSha256 = HashFile(fontPath), cases,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
