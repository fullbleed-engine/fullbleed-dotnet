using FullBleed.DotNet;
using System.Security.Cryptography;
using System.Text.Json;

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

using var compiled = engine.Compile("<p>Invoice {{id}}</p>", "body { font-family: Inter; }");
var recordsPath = Path.Combine(outputDirectory, "records.pdf");
compiled.RenderBindingsToFile(
    new[] { "FIRST-001", "SECOND-002" },
    map => map.Bind("id", id => id), recordsPath);
if (FullBleedEngine.InspectPdf(recordsPath).PageCount != 2)
{
    throw new InvalidOperationException("The packaged compiled-binding API did not emit two records.");
}

var evidence = new
{
    features.BindingVersion,
    features.AbiVersion,
    Runtime = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
    inspection.PageCount,
    MissingGlyphs = diagnostic.Diagnostics.MissingGlyphs.Count,
    PdfSha256 = Convert.ToHexString(SHA256.HashData(diagnostic.Pdf)).ToLowerInvariant(),
    PreviewSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(preview.Paths[0]))).ToLowerInvariant(),
    CompiledRecords = 2,
};
File.WriteAllText(Path.Combine(outputDirectory, "evidence.json"),
    JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"package smoke passed: {outputPath}");
