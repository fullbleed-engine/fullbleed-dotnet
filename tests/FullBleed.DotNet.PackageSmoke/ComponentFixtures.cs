using System.Security.Cryptography;
using System.Text.Json;
using FullBleed.ComponentInvoice;
using FullBleed.DotNet;
using FullBleed.DotNet.UI;
using static FullBleed.DotNet.UI.Components;

internal static class ComponentFixtures
{
    internal static void Run(string assets, string output, string packageVersion)
    {
        var directory = Path.Combine(output, "components");
        Directory.CreateDirectory(directory);
        var records = new List<object>();
        using var engine = new FullBleedEngine(new FullBleedEngineOptions
        {
            DocumentLanguage = "en-US", DocumentTitle = "Component verification",
            Assets = Directory.GetFiles(Path.Combine(assets, "fonts"), "*.ttf").Order(StringComparer.Ordinal)
                .Select(path => FullBleedAsset.FromPath(path, FullBleedAssetKind.Font)).ToList(),
        });

        var example = InvoiceDocument.Example;
        Save("invoice", InvoiceDocument.Create(example), ["Maple & Finch", "$1,870.00", "TOTAL DUE"], 1);
        Save("paid", InvoiceDocument.Create(example with { Paid = true, Accent = "#245541" }), ["PAID IN FULL", "TOTAL PAID", "$1,870.00"], 1);
        Save("empty", InvoiceDocument.Create(example with { Items = [] }), ["No billable items.", "$0.00"], 1);
        Save("credit", InvoiceDocument.Create(example with { Items = [new("Project credit", "A credit against the original project.", 1, -50)] }), ["Project credit", "-$50.00"], 1);
        Save("escaped", InvoiceDocument.Create(example with { Customer = "A & <Client> \"Café\"", Reference = "SAFE<&>1042" }), ["A & <Client> \"Café\"", "SAFE<&>1042"], 1);
        Save("many", InvoiceDocument.Create(example with
        {
            Items = Enumerable.Range(1, 40).Select(i => new InvoiceItem($"Deliverable {i:00}", "A documented unit of work, ready for handoff.", 1, 25)).ToArray(),
        }), ["Deliverable 01", "Deliverable 40", "$1,000.00"], 4);

        var paymentStyles = new StyleSheet
        {
            ["@page"] = new Style { ["size"] = "A5", ["margin"] = "15mm" },
            ["body"] = new Style { ["font-family"] = "Inter" },
            [".notice"] = new Style { ["color"] = "#245541", ["font-size"] = "18pt", ["margin"] = "0" },
            [".notice.is-paid"] = new Style { ["color"] = "#315d9b" },
        };
        Save("payment", new Document(Paragraph("Paid in full").WithClass("notice is-paid")) { Styles = paymentStyles }, ["Paid in full"], 1);
        Save("payment-no-classes", new Document(Paragraph("Paid in full")) { Styles = paymentStyles }, ["Paid in full"], 1);

        static Element Notice(string? className, Style style, params Node?[] children) => Card(children)
            .WithClass("notice", className).WithStyle(style);
        var noticeStyles = new StyleSheet(paymentStyles)
        {
            [".notice"] = new Style { ["padding"] = "12pt", ["border"] = "2pt solid #245541" },
            [".notice .notice-title"] = new Style { ["font-size"] = "14pt", ["font-weight"] = "700" },
        };
        Node[] NoticeChildren() => [Paragraph("Payment").WithClass("notice-title"), Paragraph("Paid in full")];
        Save("notice", new Document(Notice("is-paid", new Style { ["border-color"] = "#a64232" }, NoticeChildren())) { Styles = noticeStyles }, ["Payment", "Paid in full"], 1);
        Save("notice-no-inline", new Document(Notice("is-paid", new Style(), NoticeChildren())) { Styles = noticeStyles }, ["Payment", "Paid in full"], 1);
        Save("notice-no-classes", new Document(Card(Paragraph("Payment"), Paragraph("Paid in full"))
            .WithStyle(new Style { ["border-color"] = "#a64232" })) { Styles = noticeStyles }, ["Payment", "Paid in full"], 1);

        var bindingStyles = new StyleSheet
        {
            ["@page"] = new Style { ["size"] = "A5", ["margin"] = "15mm" },
            ["body"] = new Style { ["font-family"] = "Inter", ["font-size"] = "12pt" },
            ["h1"] = new Style { ["font-size"] = "20pt" },
        };
        var template = new Document(Heading(1, "Record {{id}}"), Paragraph("{{story}}")) { Styles = bindingStyles };
        using var compiled = template.Compile(engine);
        var fixedRecords = new[] { new { Id = "ONE", Story = "Fixed value one." }, new { Id = "TWO", Story = "Fixed value two." } };
        var fixedPdf = compiled.RenderBindings(fixedRecords, map => map.Bind("id", x => x.Id).Bind("story", x => x.Story));
        SaveCompiled("fixed", template, fixedPdf, ["Record ONE", "Record TWO", "Fixed value one.", "Fixed value two."], 2);
        var reflowRecords = new[]
        {
            new { Id = "SHORT", Story = "Literal <tag> & customer data." },
            new { Id = "LONG", Story = string.Join(" ", Enumerable.Repeat("Long data flows through component-authored templates.", 35)) },
        };
        var reflowPdf = compiled.RenderReflowBindings(reflowRecords, map => map.Bind("id", x => x.Id).Bind("story", x => x.Story), CompiledFlowCompression.Compact);
        SaveCompiled("reflow", template, reflowPdf, ["Record SHORT", "Record LONG", "Literal <tag> & customer data."], 3);

        var batch = engine.RenderBatch(new[]
        {
            new Document(Paragraph("Batch FIRST")) { Styles = bindingStyles }.ToRenderJob(),
            new Document(Paragraph("Batch SECOND")) { Styles = bindingStyles }.ToRenderJob(),
        });
        var batchPath = Path.Combine(directory, "batch.pdf");
        File.WriteAllBytes(batchPath, batch.Pdf);
        var batchPreviews = engine.RenderFinalizedPdfImagePagesToDirectory(batchPath, Path.Combine(directory, "batch"), dpi: 96);
        records.Add(new { Name = "batch", Pdf = "batch.pdf", PdfSha256 = Hash(batchPath), Pages = 2,
            ExpectedText = new[] { "Batch FIRST", "Batch SECOND" }, Previews = PreviewRecords(batchPreviews), Mode = "batch" });
        File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(new
        {
            Schema = "fullbleed.dotnet.components.v1", PackageVersion = packageVersion,
            FrameworkVersion = Environment.Version.ToString(), Cases = records,
        }, new JsonSerializerOptions { WriteIndented = true }));

        object[] PreviewRecords(ImagePagesResult previews) => previews.Paths.Select(path => (object)new
        {
            Path = Path.GetRelativePath(directory, path).Replace('\\', '/'), Sha256 = Hash(path),
        }).ToArray();

        void Save(string name, Document document, string[] expectedText, int pages)
        {
            var html = document.ToHtml(); var css = document.ToCss();
            File.WriteAllText(Path.Combine(directory, name + ".html"), html);
            File.WriteAllText(Path.Combine(directory, name + ".css"), css);
            var diagnostic = document.RenderPdfWithDiagnostics(engine);
            if (diagnostic.Diagnostics.MissingGlyphs.Count != 0 || !diagnostic.Pdf.SequenceEqual(document.RenderPdf(engine)))
                throw new InvalidOperationException($"Component {name} must render repeatedly without missing glyphs.");
            var path = Path.Combine(directory, name + ".pdf");
            if (document.RenderPdfToFile(engine, path) != diagnostic.Pdf.Length || !File.ReadAllBytes(path).SequenceEqual(diagnostic.Pdf))
                throw new InvalidOperationException($"Component {name} byte/file output differs.");
            var previews = document.RenderImagePagesToDirectory(engine, Path.Combine(directory, name), dpi: 96);
            records.Add(new { Name = name, Pdf = name + ".pdf", PdfSha256 = Hash(path), Pages = pages,
                ExpectedText = expectedText, Previews = PreviewRecords(previews), Html = name + ".html", Css = name + ".css",
                HtmlSha256 = Hash(Path.Combine(directory, name + ".html")), CssSha256 = Hash(Path.Combine(directory, name + ".css")), Mode = "ordinary" });
        }

        void SaveCompiled(string name, Document document, byte[] pdf, string[] expectedText, int pages)
        {
            var path = Path.Combine(directory, name + ".pdf");
            File.WriteAllBytes(path, pdf);
            File.WriteAllText(Path.Combine(directory, name + ".html"), document.ToHtml());
            File.WriteAllText(Path.Combine(directory, name + ".css"), document.ToCss());
            var previews = engine.RenderFinalizedPdfImagePagesToDirectory(path, Path.Combine(directory, name), dpi: 96);
            records.Add(new { Name = name, Pdf = name + ".pdf", PdfSha256 = Hash(path), Pages = pages,
                ExpectedText = expectedText, Previews = PreviewRecords(previews), Html = name + ".html", Css = name + ".css",
                HtmlSha256 = Hash(Path.Combine(directory, name + ".html")), CssSha256 = Hash(Path.Combine(directory, name + ".css")), Mode = name });
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
