using FullBleed.DotNet;

var outputPath = args.Length > 0
    ? args[0]
    : Path.Combine("output", "linq-invoices.pdf");

// LINQ is still the idiomatic .NET projection/filtering layer. The binding map
// consumes the resulting sequence once and turns it into Fullbleed's columnar VDP input.
var invoices = Enumerable.Range(1, 25)
    .Select(index => new Invoice(
        Id: $"VDP-{index:0000}",
        Customer: $"Customer {index:000}",
        Total: 100m + (index * 7.25m)))
    .Where(invoice => invoice.Total >= 107.25m)
    .OrderBy(invoice => invoice.Id);

const string template = """
<!doctype html>
<html>
  <body>
    <article class="invoice">
      <p class="eyebrow">INVOICE</p>
      <h1>{{invoice_id}}</h1>
      <dl>
        <dt>Customer</dt><dd>{{customer}}</dd>
        <dt>Total</dt><dd>USD {{total}}</dd>
      </dl>
    </article>
  </body>
</html>
""";

const string css = """
@page { size: Letter; margin: 0.65in; }
body { font-family: Helvetica, sans-serif; color: #172033; }
.invoice { border-top: 8pt solid #2463eb; padding-top: 24pt; }
.eyebrow { color: #2463eb; font-size: 9pt; letter-spacing: 1.5pt; }
h1 { font-size: 28pt; margin: 4pt 0 24pt; }
dt { color: #667085; font-size: 9pt; margin-top: 10pt; }
dd { font-size: 14pt; margin: 2pt 0 0; }
""";

using var engine = new FullBleedEngine();
using var compiled = engine.Compile(template, css);
var bytesWritten = compiled.RenderBindingsToFile(
    invoices,
    fields => fields
        .Bind("invoice_id", invoice => invoice.Id)
        .Bind("customer", invoice => invoice.Customer)
        .Bind("total", invoice => invoice.Total, "0.00"),
    outputPath);

var stats = compiled.GetStats();
var inspection = FullBleedEngine.InspectPdf(outputPath);
Console.WriteLine($"compiled in {stats.CompileMs:F2} ms; slots: {string.Join(", ", stats.BindingSlots)}");
Console.WriteLine($"wrote {bytesWritten:N0} bytes and {inspection.PageCount} ordered records to {Path.GetFullPath(outputPath)}");

internal sealed record Invoice(string Id, string Customer, decimal Total);
