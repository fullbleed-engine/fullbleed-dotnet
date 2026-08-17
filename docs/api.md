# API guide

## Choosing the integration layer

Use `FullBleedEngine` for in-process, high-throughput work. It embeds the engine version used to build the native runtime and does not require Python or the `fullbleed` command.

Use `FullBleedCliClient` when the installed runtime is the source of truth: capabilities, agent contract, schemas, verification policies, scaffolding, assets, compliance tooling, or newly added commands. Call `GetCapabilitiesAsync` before selecting optional functionality.

## Engine lifetime

`FullBleedEngine` and `FullBleedCompiledDocument` own native resources and implement `IDisposable`. Both may be used for multiple calls; compiled documents are immutable. Dispose them deterministically with `using`.

The bridge uses `SafeHandle`, so an in-flight P/Invoke keeps its native object alive even if another managed reference is finalized. Native panics are caught at the ABI boundary and returned as `FullBleedException` with `FullBleedStatusCode.Panic`.

## Engine configuration

`FullBleedEngineOptions` exposes:

- page size, default margins, and 1-based per-page margins;
- registered font directories/files and asset bundles from paths or bytes;
- XObject reuse and native SVG controls;
- Unicode output, shaping, and Unicode metrics;
- PDF version/profile, color space, and output-intent ICC data;
- document title/language;
- eager/lazy layout and JIT mode;
- debug/performance logs;
- text/HTML headers, text footers, watermarks;
- paginated aggregation context and PDF-template bindings.

Null options preserve native defaults and allow CSS `@page` rules to remain authoritative. Explicit `PageSize` or `Margins` values override those builder defaults.

## Rendering

- `RenderPdf` returns a managed byte array.
- `RenderPdfToFile` streams through the native engine and returns bytes written.
- `RenderPdfWithDiagnostics` returns PDF bytes plus page-data/glyph diagnostics.
- `RenderPdfWithMetrics` returns PDF bytes plus typed timing and page metrics.
- Preview methods write ordered PNG pages and return their paths.

The direct-to-file methods create the parent directory on the managed side, then let the native engine write through a buffered file writer.

## Batch rendering

`RenderBatch(IEnumerable<RenderJob>, BatchRenderOptions)` materializes the input once. When every job has the same CSS and `Parallel` is true, the native parallel lane is selected. Mixed CSS is supported with deterministic input ordering but currently uses the ordered mixed-CSS lane.

## Compiled documents

`Compile` performs HTML parsing, CSS matching, layout, pagination, and static paint compilation once.

- `Render(copies)` links immutable copies.
- `RenderBindings*` links fixed-geometry text bindings.
- `RenderReflowBindings*` binds literal text and structural slots through compiled reflow programs.
- `GetStats` reports page/command counts, slot sets, compile time, reflow readiness, and compression modes.

All binding columns must match the compiled slot set and have equal, non-zero lengths. The generic overloads use `BindingMap<T>` selectors and invariant formatting by default.

## Inspection and composition

`InspectPdf` returns page count, version, encryption state, profile markers, seed blockers, warnings, and template-composition compatibility. These are evidence fields, not independent standards certification.

`StampPdf` applies one overlay PDF to an existing template with an optional zero-based page map and translation. `ComposePdf` selects template PDFs per overlay page and supports `None`, `LinkOnly`, and `CarryWidgets` annotation modes.

## Structured CLI

The client uses `ProcessStartInfo.ArgumentList`, redirects stdout/stderr concurrently, and supports cancellation by terminating the child process tree. It never constructs a shell command string.

`FullBleedCliResult` deliberately retains JSON payloads on non-zero exit so callers can inspect structured verification failures. Call `EnsureSuccess` when non-zero results should become exceptions.

Typed render and verify request properties map one-to-one to current CLI flags. Use `RunJsonAsync` for all other commands or newly introduced flags.
