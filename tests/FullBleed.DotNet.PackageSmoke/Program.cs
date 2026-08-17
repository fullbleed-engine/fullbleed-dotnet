using FullBleed.DotNet;

var outputPath = args.Length == 1
    ? args[0]
    : Path.Combine("output", "package-smoke.pdf");

using var engine = new FullBleedEngine();
engine.RenderPdfToFile(
    "<h1>PACKAGE-SMOKE-001</h1><p>Loaded from a packed NuGet artifact.</p>",
    "body { font-family: Helvetica, sans-serif; }",
    outputPath);

var inspection = FullBleedEngine.InspectPdf(outputPath);
if (inspection.PageCount != 1)
{
    throw new InvalidOperationException($"Expected one page, got {inspection.PageCount}.");
}

Console.WriteLine($"package smoke passed: {Path.GetFullPath(outputPath)}");
