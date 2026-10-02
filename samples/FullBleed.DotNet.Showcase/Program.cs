using FullBleed.DotNet;

var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
var output = Path.GetFullPath(args.Length == 1 ? args[0] : "output/northstar");
Directory.CreateDirectory(output);
var html = File.ReadAllText(Path.Combine(assets, "invoice.html"));
var css = File.ReadAllText(Path.Combine(assets, "invoice.css"));
using var engine = new FullBleedEngine(new FullBleedEngineOptions
{
    DocumentLanguage = "en-US",
    DocumentTitle = "Northstar Studio - Invoice NS-1042",
    Assets = Directory.GetFiles(Path.Combine(assets, "fonts"), "*.ttf")
        .Order(StringComparer.Ordinal)
        .Select(path => FullBleedAsset.FromPath(path, FullBleedAssetKind.Font))
        .ToList(),
});
var rendered = engine.RenderPdfWithDiagnostics(html, css);
if (rendered.Diagnostics.MissingGlyphs.Count != 0)
{
    throw new InvalidOperationException("The supplied fonts did not cover every document character.");
}

var pdfPath = Path.Combine(output, "invoice.pdf");
File.WriteAllBytes(pdfPath, rendered.Pdf);
var previews = engine.RenderImagePagesToDirectory(html, css, output, dpi: 96, stem: "invoice");
var inspection = FullBleedEngine.InspectPdf(pdfPath);
Console.WriteLine($"Created {pdfPath}: {inspection.PageCount} page(s), {previews.Paths.Count} PNG preview(s).");
