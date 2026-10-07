using FullBleed.DotNet;
using System.Security.Cryptography;
using System.Text.Json;

internal static class StandardFontFixtures
{
    internal static void Run(string assets, string outputDirectory, string version)
    {
        var output = Path.Combine(outputDirectory, "standard-fonts");
        Directory.CreateDirectory(output);
        var fontPath = Path.Combine(assets, "fonts", "Inter-Variable.ttf");
        using var engine = new FullBleedEngine(new FullBleedEngineOptions
        {
            Assets = [FullBleedAsset.FromPath(fontPath, FullBleedAssetKind.Font)],
        });
        string[] fonts = [
            "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
            "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
            "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique", "Inter",
        ];
        const string text = "Invoice AV 105 & caf\u00e9";
        const string html = "<p>{{text}}</p>";
        var cases = new List<object>();
        foreach (var mode in new[] { "pdf", "fixed", "reflow", "compact" })
        {
            foreach (var font in fonts)
            {
                var name = mode + "-" + font;
                var folder = Path.Combine(output, name);
                Directory.CreateDirectory(folder);
                var css = $"@page {{size:300pt 100pt;margin:12pt}} p {{margin:0;font-family:\"{font}\";font-size:20pt}}";
                File.WriteAllText(Path.Combine(folder, "input.html"), html);
                File.WriteAllText(Path.Combine(folder, "style.css"), css);
                byte[] pdf;
                string? htmlPreviewSha256 = null;
                if (mode == "pdf")
                {
                    var input = html.Replace("{{text}}", "Invoice AV 105 &amp; caf&#233;", StringComparison.Ordinal);
                    var rendered = engine.RenderPdfWithDiagnostics(input, css);
                    pdf = rendered.Pdf;
                    if (rendered.Diagnostics.MissingGlyphs.Count != 0 || !pdf.SequenceEqual(engine.RenderPdf(input, css)))
                    {
                        throw new InvalidOperationException($"Non-repeatable output or missing glyphs: {name}");
                    }
                    var direct = engine.RenderImagePagesToDirectory(input, css, Path.Combine(folder, "html"), dpi: 72, stem: "preview");
                    if (direct.Paths.Count != 1) { throw new InvalidOperationException($"Expected one HTML preview: {name}"); }
                    htmlPreviewSha256 = HashFile(direct.Paths[0]);
                }
                else
                {
                    using var compiled = engine.Compile(html, css);
                    var values = new[] { text };
                    byte[] Paint() => mode == "fixed"
                        ? compiled.RenderBindings(values, map => map.Bind("text", value => value))
                        : compiled.RenderReflowBindings(values, map => map.Bind("text", value => value),
                            mode == "compact" ? CompiledFlowCompression.Compact : CompiledFlowCompression.Throughput);
                    pdf = Paint();
                    if (!pdf.SequenceEqual(Paint())) { throw new InvalidOperationException($"Non-repeatable compiled output: {name}"); }
                }
                var path = Path.Combine(folder, "document.pdf");
                File.WriteAllBytes(path, pdf);
                if (FullBleedEngine.InspectPdf(path).PageCount != 1)
                {
                    throw new InvalidOperationException($"Expected one standard-font page: {name}");
                }
                var previews = engine.RenderFinalizedPdfImagePagesToDirectory(path, folder, dpi: 72, stem: "preview");
                var repeated = engine.RenderFinalizedPdfImagePagesToDirectory(path, Path.Combine(folder, "repeat"), dpi: 72, stem: "preview");
                if (previews.Paths.Count != 1 || repeated.Paths.Count != 1 || HashFile(previews.Paths[0]) != HashFile(repeated.Paths[0]) ||
                    !pdf.SequenceEqual(File.ReadAllBytes(path)))
                {
                    throw new InvalidOperationException($"Non-repeatable preview or changed source PDF: {name}");
                }
                cases.Add(new
                {
                    name, mode, font, expectedText = text,
                    htmlSha256 = HashFile(Path.Combine(folder, "input.html")),
                    cssSha256 = HashFile(Path.Combine(folder, "style.css")),
                    pdfSha256 = HashFile(path),
                    previewFile = Path.GetFileName(previews.Paths[0]),
                    previewSha256 = HashFile(previews.Paths[0]),
                    htmlPreviewSha256,
                    repeatPdfBytesIdentical = true,
                    repeatPreviewBytesIdentical = true,
                });
            }
        }
        File.WriteAllText(Path.Combine(output, "renders.json"), JsonSerializer.Serialize(new
        {
            schema = "fullbleed.dotnet.standard_fonts.v1", ok = true, packageVersion = version,
            runtime = Environment.Version.ToString(), embeddedControlFontSha256 = HashFile(fontPath), cases,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
