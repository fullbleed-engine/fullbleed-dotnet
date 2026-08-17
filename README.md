# Fullbleed for .NET

Idiomatic .NET 8 bindings for [Fullbleed PDF Engine](https://github.com/fullbleed-engine/fullbleed-official): deterministic HTML/CSS-to-PDF rendering, compiled variable-data publishing (VDP), diagnostics, previews, PDF inspection, template composition, and runtime-discovered CLI workflows.

This repository contains two complementary integration layers:

- `FullBleedEngine` calls a small, panic-safe C ABI over the Rust engine. Use it for in-process rendering, high-volume batches, and compiled VDP.
- `FullBleedCliClient` calls the installed `fullbleed` command with argument-safe process APIs and structured JSON. Use it for runtime discovery, verification, profiles, assets, scaffolding, agent contracts, and commands that evolve independently of the native ABI.

The managed assembly has no third-party NuGet runtime dependencies. Native runtime libraries are packaged using NuGet's `runtimes/{rid}/native/` convention. The core engine remains browser-free and does not depend on the operating system's PDF stack or fonts.

## Status

The binding is version `0.1.0` and currently builds against the Fullbleed `2.3.1` Rust crate. It is not yet claimed as published on NuGet. `dotnet pack` produces the intended `FullBleed.DotNet` package locally and CI assembles platform artifacts.

Supported package targets in the current build pipeline:

- `win-x64`
- `linux-x64`
- `osx-x64`
- `osx-arm64`

The public managed API targets `net8.0`; applications on compatible later .NET releases can consume that target.

## Quick start from source

Place this repository beside `fullbleed-official`:

```text
workbench/
  fullbleed-dotnet/
  fullbleed-official/
```

Then build and test:

```powershell
./scripts/build-native.ps1
dotnet test FullBleed.DotNet.sln -c Release
```

Basic rendering:

```csharp
using FullBleed.DotNet;

using var engine = new FullBleedEngine(new FullBleedEngineOptions
{
    DocumentLanguage = "en-US",
    DocumentTitle = "Quarterly report",
});

engine.RenderPdfToFile(
    "<h1>Quarterly report</h1><p>Deterministic print output.</p>",
    "body { font-family: Helvetica, sans-serif; }",
    "output/report.pdf");

var inspection = FullBleedEngine.InspectPdf("output/report.pdf");
Console.WriteLine($"{inspection.PageCount} page(s), PDF {inspection.PdfVersion}");
```

`FullBleedRenderer.Render(...)` and `RenderToFile(...)` remain as compatibility helpers for the original proof of concept.

## LINQ and compiled VDP

LINQ remains the modern .NET projection/filtering API. Fullbleed's selector-based `BindingMap<T>` enumerates the result once and converts it into validated columnar bindings:

```csharp
var invoices = Enumerable.Range(1, 1_000)
    .Select(i => new Invoice($"INV-{i:000000}", $"Customer {i}", 100m + i))
    .Where(invoice => invoice.Total >= 250m)
    .OrderBy(invoice => invoice.Id);

using var engine = new FullBleedEngine();
using var compiled = engine.Compile(
    "<h1>{{invoice_id}}</h1><p>{{customer}}</p><strong>USD {{total}}</strong>",
    "body { font-family: Helvetica, sans-serif; }");

compiled.RenderBindingsToFile(
    invoices,
    map => map
        .Bind("invoice_id", invoice => invoice.Id)
        .Bind("customer", invoice => invoice.Customer)
        .Bind("total", invoice => invoice.Total, "0.00"),
    "output/invoices.pdf");
```

Use `RenderBindings*` only for paint-only values whose geometry is reserved by the template. Use `RenderReflowBindings*` when values can wrap, reshape, change element size, or repaginate:

```csharp
compiled.RenderReflowBindingsToFile(
    records,
    map => map
        .Bind("title", record => record.Title)
        .Bind("narrative", record => record.Narrative),
    "output/reflow.pdf",
    CompiledFlowCompression.Throughput);
```

Structural `data-fb-bind-html` values are trusted HTML. Construct them from escaped fields or pass them through an application-approved allowlist sanitizer; ordinary `{{slot}}` values remain literal text.

See the complete executable example in [`samples/FullBleed.DotNet.LinqVdp`](samples/FullBleed.DotNet.LinqVdp).

## Batches, diagnostics, and previews

```csharp
var jobs = records.Select(record => new RenderJob(BuildHtml(record), reportCss));
var batch = engine.RenderBatch(jobs, new BatchRenderOptions
{
    Parallel = true,
    IncludePageData = true,
});
File.WriteAllBytes("output/batch.pdf", batch.Pdf);

var diagnostic = engine.RenderPdfWithDiagnostics(html, css);
foreach (var missing in diagnostic.Diagnostics.MissingGlyphs)
{
    Console.WriteLine($"U+{missing.Codepoint:X4}: {missing.Count}");
}

var previews = engine.RenderImagePagesToDirectory(
    html,
    css,
    "output/preview",
    dpi: 144);
```

Parallel native batching is used when jobs share CSS. Mixed-CSS jobs retain input order and use the ordinary ordered batch lane.

## Runtime-authoritative CLI access

The CLI layer never assumes the installed release has the same surface as the compiled native package:

```csharp
var client = new FullBleedCliClient();
var capabilities = await client.GetCapabilitiesAsync();
var contract = await client.GetAgentContractAsync();
var renderSchema = await client.GetSchemaAsync(["render"]);

var result = await client.RenderAsync(new FullBleedCliRenderRequest
{
    Html = "<h1>CLI render</h1>",
    Css = "body { font-family: Helvetica, sans-serif; }",
    OutputPath = "output/cli.pdf",
    Profile = "preflight",
    FailOn = ["overflow", "missing-glyphs"],
    EmitImageDirectory = "output/cli-preview",
});

result.EnsureSuccess();
```

`RunAsync` and `RunJsonAsync` expose every installed command without shell interpolation. Typed render/verify requests cover the current document flags, while `AdditionalArguments` and the generic runner provide forward compatibility.

## Surface map

| Area | .NET API |
| --- | --- |
| Ordinary PDF bytes/files | `FullBleedEngine.RenderPdf*` |
| Metrics, page data, glyphs | `RenderPdfWithMetrics`, `RenderPdfWithDiagnostics` |
| PNG page previews | `RenderImagePagesToDirectory`, `RenderFinalizedPdfImagePagesToDirectory` |
| Ordered and parallel batch | `RenderBatch`, `RenderBatchToFile` |
| Compile and immutable copies | `Compile`, `FullBleedCompiledDocument.Render*` |
| Fixed-geometry VDP | `RenderBindings*` |
| Content-reflow VDP | `RenderReflowBindings*` |
| Assets/fonts/output intent | `FullBleedEngineOptions` |
| PDF inspection | `FullBleedEngine.InspectPdf` |
| Existing-template overlay | `StampPdf`, `ComposePdf` |
| Capability/contract/schema discovery | `FullBleedCliClient` |
| Verification and full CLI suite | typed CLI requests plus `RunJsonAsync` |

More detail is in [`docs/api.md`](docs/api.md), [`docs/native-abi.md`](docs/native-abi.md), and [`docs/development.md`](docs/development.md).

## PDF profiles and claims

Selecting `PdfUa1`, `PdfUa2`, a PDF/A profile, PDF/X, PDF/VT, WTPDF, or `Tagged` changes engine output configuration; it does not by itself prove conformance or accessibility. Supply the required embedded fonts/output intent, run Fullbleed verification, retain diagnostics, and use the applicable independent conformance checker before making claims.

## Packaging

Build the current platform package locally:

```powershell
./scripts/pack.ps1
```

Cross-platform release packages must contain every claimed RID asset. CI builds each native library on its matching operating system and verifies the final `.nupkg` entries. Details are in [`docs/development.md`](docs/development.md).

## License

MIT. See [`LICENSE`](LICENSE) and [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
