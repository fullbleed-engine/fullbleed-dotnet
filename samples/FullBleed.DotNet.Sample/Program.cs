var outputPath = args.Length > 0
    ? args[0]
    : Path.Combine("output", "sample.pdf");

const string html = """
<!doctype html>
<html>
  <body>
    <div class="card">
      <h1>Fullbleed for .NET</h1>
      <p>Rendered deterministically from Rust through the native .NET binding.</p>
    </div>
  </body>
</html>
""";

const string css = """
body {
  margin: 0;
  font-family: Helvetica, Arial, sans-serif;
  background: #f7f7f7;
}

.card {
  margin: 48pt;
  padding: 24pt;
  border: 1pt solid #b6b6b6;
  background: #ffffff;
}

h1 {
  margin: 0 0 12pt 0;
  font-size: 28pt;
}
""";

using var engine = new FullBleedEngine(new FullBleedEngineOptions
{
    DocumentLanguage = "en-US",
    DocumentTitle = "Fullbleed .NET sample",
});
var bytesWritten = engine.RenderPdfToFile(html, css, outputPath);
var inspection = FullBleedEngine.InspectPdf(outputPath);

Console.WriteLine($"wrote {bytesWritten:N0} bytes to {Path.GetFullPath(outputPath)}");
Console.WriteLine($"PDF {inspection.PdfVersion}, {inspection.PageCount} page(s)");
