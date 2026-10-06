using FullBleed.DotNet;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length >= 3 && $"{Environment.Version.Major}.{Environment.Version.Minor}" != args[2])
{
    throw new InvalidOperationException($"Running on .NET {Environment.Version}; expected {args[2]}.");
}

var outputPath = Path.GetFullPath(args.Length >= 1 ? args[0] : "output/package-smoke.pdf");
var outputDirectory = Path.GetDirectoryName(outputPath)!;
Directory.CreateDirectory(outputDirectory);
var features = FullBleedEngine.GetNativeFeatures();
if (args.Length >= 2 && features.BindingVersion != args[1])
{
    throw new InvalidOperationException($"Loaded native binding {features.BindingVersion}; expected package {args[1]}.");
}

var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
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

var diagnostic = engine.RenderPdfWithDiagnostics(html, css);
if (diagnostic.Diagnostics.MissingGlyphs.Count != 0)
{
    throw new InvalidOperationException("The packed runtime reported missing glyphs in the styled invoice.");
}
File.WriteAllBytes(outputPath, diagnostic.Pdf);
if (!diagnostic.Pdf.SequenceEqual(engine.RenderPdf(html, css)))
{
    throw new InvalidOperationException("Repeated invoice renders produced different PDF bytes.");
}

var inspection = FullBleedEngine.InspectPdf(outputPath);
if (inspection.PageCount != 1)
{
    throw new InvalidOperationException($"Expected one page, got {inspection.PageCount}.");
}

var preview = engine.RenderImagePagesToDirectory(html, css, outputDirectory, dpi: 96, stem: "invoice");
if (preview.Paths.Count != 1 || !File.ReadAllBytes(preview.Paths[0]).AsSpan(0, 8)
    .SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
{
    throw new InvalidOperationException("The packaged runtime did not produce a valid one-page PNG preview.");
}

var finalizedPreview = engine.RenderFinalizedPdfImagePagesToDirectory(
    outputPath, Path.Combine(outputDirectory, "finalized"), dpi: 96, stem: "invoice");
if (finalizedPreview.Paths.Count != 1 || !File.ReadAllBytes(finalizedPreview.Paths[0]).AsSpan(0, 8)
    .SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
{
    throw new InvalidOperationException("The packaged runtime did not produce a valid preview of the saved PDF.");
}

// This control pins the brand's intended regular face. The ordinary invoice
// above retains its original family CSS and exercises registration order.
var explicitInvoiceCss = css + "\n.invoice .brand {font-family:'DMSerifDisplay-Regular';font-style:normal;}";
var explicitInvoicePath = Path.Combine(outputDirectory, "invoice-explicit.pdf");
engine.RenderPdfToFile(html, explicitInvoiceCss, explicitInvoicePath);
var explicitInvoicePreview = engine.RenderFinalizedPdfImagePagesToDirectory(
    explicitInvoicePath, Path.Combine(outputDirectory, "finalized-explicit"), dpi: 96, stem: "invoice-explicit");

var boldPath = Path.Combine(outputDirectory, "bold.pdf");
const string boldHtml = "<h1>Invoice BOLD-1042</h1><p><strong>Customer Ada</strong></p><p>Total USD 250.00</p>";
const string boldCss = "body { font-family: Inter; font-size: 12pt; } h1, strong { font-weight: 700; }";
engine.RenderPdfToFile(boldHtml, boldCss, boldPath);
var boldPreview = engine.RenderFinalizedPdfImagePagesToDirectory(
    boldPath, Path.Combine(outputDirectory, "finalized-bold"), dpi: 96, stem: "bold");
if (boldPreview.Paths.Count != 1)
{
    throw new InvalidOperationException("Expected one preview page for the bold-text regression fixture.");
}

const string fixedHtml = "<p>Invoice {{id}}</p>";
const string fixedCss = "body { font-family: Inter; }";
var fixedRecords = new[] { "FIRST-001", "SECOND-002" };
using var compiled = engine.Compile(fixedHtml, fixedCss);
var recordsPath = Path.Combine(outputDirectory, "records.pdf");
compiled.RenderBindingsToFile(
    fixedRecords,
    map => map.Bind("id", id => id), recordsPath);
if (FullBleedEngine.InspectPdf(recordsPath).PageCount != 2)
{
    throw new InvalidOperationException("The packaged compiled-binding API did not emit two records.");
}
if (!File.ReadAllBytes(recordsPath).SequenceEqual(compiled.RenderBindings(fixedRecords, map => map.Bind("id", id => id))))
{
    throw new InvalidOperationException("Compiled fixed records differ between file and repeated byte output.");
}
var recordsPreview = engine.RenderFinalizedPdfImagePagesToDirectory(
    recordsPath, Path.Combine(outputDirectory, "finalized-records"), dpi: 96, stem: "records");

const string reflowHtml = "<h1>Record {{id}}</h1><p>{{story}}</p>";
const string reflowCss = "@page {size:A4;margin:36pt} body {font-family:Inter;font-size:12pt} p {width:180pt}";
var reflowRecords = new[]
{
    new { Id = "REFLOW-001", Story = "Résumé café Ångström. A short narrative." },
    new { Id = "REFLOW-002", Story = string.Join(" ", Enumerable.Repeat("A longer narrative wraps across several lines while retaining its original glyphs and metrics.", 8)) },
};
using var reflow = engine.Compile(reflowHtml, reflowCss);
var reflowPath = Path.Combine(outputDirectory, "reflow.pdf");
reflow.RenderReflowBindingsToFile(reflowRecords,
    map => map.Bind("id", record => record.Id).Bind("story", record => record.Story),
    reflowPath, CompiledFlowCompression.Compact);
if (FullBleedEngine.InspectPdf(reflowPath).PageCount != 2 ||
    !File.ReadAllBytes(reflowPath).SequenceEqual(reflow.RenderReflowBindings(reflowRecords,
        map => map.Bind("id", record => record.Id).Bind("story", record => record.Story), CompiledFlowCompression.Compact)))
{
    throw new InvalidOperationException("Compiled reflow records must retain two pages and repeat identically.");
}
var reflowPreview = engine.RenderFinalizedPdfImagePagesToDirectory(
    reflowPath, Path.Combine(outputDirectory, "finalized-reflow"), dpi: 96, stem: "reflow");

var probeFont = Path.Combine(assets, "verification-fonts", "NotoSans-Regular.ttf");
const string probeText = "Invoice 2042 Résumé café Ångström Ω Ж 123.45";
const string probeHtml = "<p>" + probeText + "</p>";
const string probeCss = "@page {size:A4;margin:36pt} body {font-family:'Noto Sans';font-size:18pt}";
using var probeEngine = new FullBleedEngine(new FullBleedEngineOptions
{
    DocumentLanguage = "en-US",
    DocumentTitle = "Unicode font verification",
    Assets = [FullBleedAsset.FromPath(probeFont, FullBleedAssetKind.Font)],
});
var probe = probeEngine.RenderPdfWithDiagnostics(probeHtml, probeCss);
if (probe.Diagnostics.MissingGlyphs.Count != 0 || !probe.Pdf.SequenceEqual(probeEngine.RenderPdf(probeHtml, probeCss)))
{
    throw new InvalidOperationException("Unicode font output must have no missing glyphs and repeat identically.");
}
var probePath = Path.Combine(outputDirectory, "unicode.pdf");
File.WriteAllBytes(probePath, probe.Pdf);
if (FullBleedEngine.InspectPdf(probePath).PageCount != 1)
{
    throw new InvalidOperationException("Expected one Unicode font probe page.");
}
var probePreview = probeEngine.RenderFinalizedPdfImagePagesToDirectory(
    probePath, Path.Combine(outputDirectory, "finalized-unicode"), dpi: 96, stem: "unicode");

var inputs = new
{
    Schema = "fullbleed.dotnet.font_inputs.v1",
    DocumentLanguage = "en-US",
    DocumentTitle = "Northstar Studio - Invoice NS-1042",
    Invoice = new { Html = html, Css = css },
    InvoiceExplicit = new { Html = html, Css = explicitInvoiceCss },
    Bold = new { Html = boldHtml, Css = boldCss },
    Fixed = new { Html = fixedHtml, Css = fixedCss, Records = fixedRecords },
    Reflow = new { Html = reflowHtml, Css = reflowCss, Records = reflowRecords, Compression = "Compact" },
    Unicode = new { Html = probeHtml, Css = probeCss, Text = probeText, DocumentTitle = "Unicode font verification" },
    Fonts = Directory.GetFiles(Path.Combine(assets, "fonts"), "*.ttf").Append(probeFont)
        .Order(StringComparer.Ordinal).Select(path => new { Name = Path.GetFileName(path), Sha256 = HashFile(path) }).ToArray(),
};
File.WriteAllText(Path.Combine(outputDirectory, "font-inputs.json"),
    JsonSerializer.Serialize(inputs, new JsonSerializerOptions { WriteIndented = true }));

var evidence = new
{
    features.BindingVersion,
    features.AbiVersion,
    Runtime = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
    Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    FrameworkVersion = Environment.Version.ToString(),
    TargetFramework = AppContext.TargetFrameworkName,
    inspection.PageCount,
    MissingGlyphs = diagnostic.Diagnostics.MissingGlyphs.Count,
    PdfSha256 = Convert.ToHexString(SHA256.HashData(diagnostic.Pdf)).ToLowerInvariant(),
    PreviewSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(preview.Paths[0]))).ToLowerInvariant(),
    FinalizedPreviewSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(finalizedPreview.Paths[0]))).ToLowerInvariant(),
    ExplicitInvoicePdfSha256 = HashFile(explicitInvoicePath),
    ExplicitInvoicePreviewSha256 = HashFile(explicitInvoicePreview.Paths[0]),
    BoldPdfSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(boldPath))).ToLowerInvariant(),
    BoldPreviewSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(boldPreview.Paths[0]))).ToLowerInvariant(),
    CompiledRecords = 2,
    CompiledPdfSha256 = HashFile(recordsPath),
    CompiledPreviewsSha256 = recordsPreview.Paths.Select(HashFile).ToArray(),
    ReflowRecords = 2,
    ReflowPdfSha256 = HashFile(reflowPath),
    ReflowPreviewsSha256 = reflowPreview.Paths.Select(HashFile).ToArray(),
    UnicodePdfSha256 = HashFile(probePath),
    UnicodePreviewsSha256 = probePreview.Paths.Select(HashFile).ToArray(),
    FontInputsSha256 = HashFile(Path.Combine(outputDirectory, "font-inputs.json")),
};
File.WriteAllText(Path.Combine(outputDirectory, "evidence.json"),
    JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
FontFamilyFixtures.Run(assets, outputDirectory, features.BindingVersion);
InlineLayoutFixtures.Run(assets, outputDirectory, features.BindingVersion);
Console.WriteLine($"package smoke passed: {outputPath}");

static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
