# Fullbleed for .NET

Idiomatic C# bindings for [Fullbleed PDF Engine](https://github.com/fullbleed-engine/fullbleed-official): deterministic HTML/CSS-to-PDF rendering, compiled variable-data publishing (VDP), diagnostics, previews, PDF inspection, template composition, and runtime-discovered CLI workflows.

This repository contains two complementary integration layers:

- `FullBleedEngine` calls a small, panic-safe C ABI over the Rust engine. Use it for in-process rendering, high-volume batches, and compiled VDP.
- `FullBleedCliClient` calls the installed `fullbleed` command with argument-safe process APIs and structured JSON. Use it for runtime discovery, verification, profiles, assets, scaffolding, agent contracts, and commands that evolve independently of the native ABI.

The managed assembly has no third-party NuGet runtime dependencies. Native runtime libraries are packaged using NuGet's `runtimes/{rid}/native/` convention. PDF generation requires neither a browser nor the operating system's PDF stack. Standard-font previews use bundled outlines; explicit fonts provide your chosen type design. Both work without installed system fonts.

## Status

Version `0.1.6` pins the published Fullbleed `2.5.11` Rust crate. The NuGet package ID is [`FullBleed.DotNet`](https://www.nuget.org/packages/FullBleed.DotNet/0.1.6). CI builds the native libraries, assembles one package, and runs a separate consumer of that exact package on each supported platform.

Version 0.1.5 fixed wrapping after styled inline text and the widths of tracked labels in flex and inline-block layouts. The release checks ordinary PDFs and compiled fixed/reflow documents against the previous published package. See the [inline-layout verification](https://github.com/fullbleed-engine/fullbleed-dotnet/blob/master/docs/development.md#inline-layout-verification).

Native PNG previews of unembedded standard PDF fonts now use bundled, licensed outline substitutes. They no longer depend on host fonts or become blank when those fonts are absent. PDF bytes and embedded-font previews are preserved. See the [standard-font verification](https://github.com/fullbleed-engine/fullbleed-dotnet/blob/master/docs/development.md#standard-font-preview-verification) for the twelve Latin faces checked through ordinary and compiled output, including a Linux run with system fonts hidden. Register explicit fonts when the exact type design matters.

Normal text now selects the regular face when a family's italic font is registered first. Review saved PDF baselines when upgrading: affected documents can change appearance, line breaks, and file size. Explicit face names and `@font-face` mappings remain available. See the [font-selection guide](https://docs.fullbleed.dev/engine/font-registration/).

Supported package targets in the current build pipeline:

- `win-x64`
- `linux-x64`
- `osx-x64`
- `osx-arm64`

The managed library targets `net8.0`, which applications on .NET 8, 9, and 10 can reference. CI runs the same assembled NuGet package in applications targeting each of those frameworks on every platform above, checks the actual runtime version, and compares fixture PDFs, independent PDFium renders, and native previews with both embedded fonts and unembedded standard fonts. See the [verification workflow](https://github.com/fullbleed-engine/fullbleed-dotnet/actions/workflows/ci.yml) and its retained `cross-platform-evidence` artifact.

Use .NET 10 LTS for a new application. Microsoft's [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) lists November 10, 2026 as the end of support for both .NET 8 and 9; a library's minimum target does not require your application to stay on that runtime.

## Install and render

With the .NET 10 SDK installed:

```sh
dotnet new console -n FullbleedDemo --framework net10.0
cd FullbleedDemo
dotnet add package FullBleed.DotNet --version 0.1.6
```

Replace `Program.cs` with:

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

Run `dotnet run` and open `output/report.pdf`. Existing .NET 8 and 9 projects use the same package and API.

`FullBleedRenderer.Render(...)` and `RenderToFile(...)` remain as compatibility helpers for the original proof of concept.

## A styled invoice you can run

The Northstar sample uses explicit fonts, print dimensions, CSS grid, tables, color, and typographic hierarchy. All content is fictional. Font files and their OFL notices are included; rendering needs no network access or system fonts.

```sh
dotnet run --project samples/FullBleed.DotNet.Showcase -c Release -- output/northstar
```

This writes `invoice.pdf` and a PNG preview. The [sample source](https://github.com/fullbleed-engine/fullbleed-dotnet/blob/master/samples/FullBleed.DotNet.Showcase/Program.cs) shows font registration and missing-glyph diagnostics. The same document is also rendered from the assembled NuGet package in CI. [Inspect the design and download its HTML/CSS](https://docs.fullbleed.dev/examples/).

The 0.1.4 family-selection fix corrects the invoice's brand to its intended regular face. The release check compares it with an explicit regular-face control, while preserving the original family CSS in the real invoice. It also checks bold text, fixed and reflow records, Unicode text, and both font registration orders. See [how to reproduce the comparison](https://github.com/fullbleed-engine/fullbleed-dotnet/blob/master/docs/development.md#font-output-comparison).

The historical [0.1.3 font-compaction evidence](https://github.com/fullbleed-engine/fullbleed-dotnet/releases/tag/v0.1.3) records this invoice changing from 81,227 to 34,542 bytes with identical text and pixels. That measurement uses the earlier font selection; the 0.1.4 correction changes its appearance and size. Savings depend on the document and fonts.

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

See the complete executable example in [`samples/FullBleed.DotNet.LinqVdp`](https://github.com/fullbleed-engine/fullbleed-dotnet/tree/master/samples/FullBleed.DotNet.LinqVdp).

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

Register font files for portable PNG previews. When a PDF uses unembedded standard fonts such as Helvetica or Times, the native previewer uses host font fallbacks; previews can differ across operating systems or omit glyphs when a fallback is unavailable. The styled invoice sample includes explicit fonts.

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

More detail is in [`docs/api.md`](https://github.com/fullbleed-engine/fullbleed-dotnet/blob/master/docs/api.md), [`docs/native-abi.md`](https://github.com/fullbleed-engine/fullbleed-dotnet/blob/master/docs/native-abi.md), and [`docs/development.md`](https://github.com/fullbleed-engine/fullbleed-dotnet/blob/master/docs/development.md).

## PDF profiles and claims

Selecting `PdfUa1`, `PdfUa2`, a PDF/A profile, PDF/X, PDF/VT, WTPDF, or `Tagged` changes engine output configuration; it does not by itself prove conformance or accessibility. Supply the required embedded fonts/output intent, run Fullbleed verification, retain diagnostics, and use the applicable independent conformance checker before making claims.

## Packaging

To build from source, install the .NET 8 SDK and Rust 1.85 or later, then run:

```powershell
./scripts/build-native.ps1
dotnet test FullBleed.DotNet.sln -c Release
```

Cargo downloads the exact engine release from crates.io. No sibling checkout is required.

Build the current platform package locally:

```powershell
./scripts/pack.ps1
```

Cross-platform release packages must contain every claimed RID asset. CI checks the binary architecture, retained license texts, and dependency provenance inside the final `.nupkg`, then tests it on each matching OS. The managed assembly has no third-party NuGet runtime dependencies; the separate CLI adapter requires an independently installed Fullbleed Python CLI. Details are in [`docs/development.md`](https://github.com/fullbleed-engine/fullbleed-dotnet/blob/master/docs/development.md).

## License

MIT. See [`LICENSE`](https://github.com/fullbleed-engine/fullbleed-dotnet/blob/master/LICENSE) and [`THIRD_PARTY_NOTICES.md`](https://github.com/fullbleed-engine/fullbleed-dotnet/blob/master/THIRD_PARTY_NOTICES.md).
