using System.Text.Json;
using FullBleed.ComponentInvoice;
using FullBleed.DotNet;

var output = Path.GetFullPath(args.Length == 0 ? "output/components" : args[0]);
Directory.CreateDirectory(output);
var document = InvoiceDocument.Create(InvoiceDocument.Example);
using var engine = new FullBleedEngine(new FullBleedEngineOptions
{
    DocumentLanguage = "en-US",
    DocumentTitle = document.Title,
    Assets = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Assets/fonts"), "*.ttf")
        .Order(StringComparer.Ordinal).Select(path => FullBleedAsset.FromPath(path, FullBleedAssetKind.Font)).ToList(),
});
var result = document.RenderPdfWithDiagnostics(engine);
if (result.Diagnostics.MissingGlyphs.Count != 0)
{
    throw new InvalidOperationException("The invoice contains text absent from its supplied fonts.");
}
File.WriteAllBytes(Path.Combine(output, "invoice.pdf"), result.Pdf);
document.RenderImagePagesToDirectory(engine, output, dpi: 96, stem: "invoice");
File.WriteAllText(Path.Combine(output, "invoice.html"), document.ToHtml());
File.WriteAllText(Path.Combine(output, "invoice.css"), document.ToCss());
File.WriteAllText(Path.Combine(output, "diagnostics.json"), JsonSerializer.Serialize(result.Diagnostics, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Created {Path.Combine(output, "invoice.pdf")}");
