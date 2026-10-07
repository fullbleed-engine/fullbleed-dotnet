# Changelog

## 0.1.7 - 2026-10-07

- Pin Fullbleed 2.5.13 to correct decorative artifacts, list numbering and HTML figure/caption structure in tagged PDFs.
- Render a rich PDF/UA-1 and PDF/UA-2 specimen and an ordinary control in every isolated .NET 8/9/10 package consumer on all four supported platforms. Validate with pinned veraPDF 1.30.2 and compare exact PDF, preview and text output.
- Retain the real public 0.1.6 negative control. Machine checks cover these specimens; content quality and reading order still need review.
- Keep the managed API and native ABI unchanged.

## 0.1.6 - 2026-10-07

- Pin Fullbleed 2.5.11 so unembedded standard-font PNG previews use bundled outline substitutes instead of system-font fallbacks.
- Retain the bundled fonts' OFL licenses, modification notices, Adobe metric/mapping notices and source provenance in the NuGet archive.
- Check 52 standard-font cases through ordinary and compiled output in the 12-consumer platform/framework matrix, plus a no-system-font comparison against public 0.1.5. PDF bytes and embedded-font controls remain unchanged.
- Require all 54 existing inline native previews to match across platforms, including the eight unembedded standard-font cases. Preserve the reviewed 2.5.10 layout baselines.

## 0.1.5 - 2026-10-06

- Pin Fullbleed 2.5.10 to correct text following styled inline content and intrinsic widths of tracked labels in flex and inline-block layouts.
- Add 54 PDF fixtures to isolated NuGet consumers: 12 wrapping cases through ordinary, fixed, throughput reflow, and compact reflow rendering, plus six label-width cases. Check text, physical word positions, deterministic output, and previews independently.
- Retain a published 0.1.4 negative control and compare the assembled package across .NET 8, 9, and 10 on all four supported platforms.
- The managed API and native ABI are unchanged. Affected templates can change line breaks and page layout; review saved PDF baselines when upgrading.

## 0.1.4 - 2026-10-05

- Pin published Fullbleed 2.5.8 so normal family text selects the regular face even when italic is registered first.
- Verify ordinary, fixed, and reflow font selection against explicit faces in isolated package consumers, including both registration orders and explicit `@font-face` mappings.
- Correct the Northstar invoice brand and retain its comparison with an explicit regular-face control. Affected documents can change appearance, line breaks, and size; review saved PDF baselines when upgrading.

## 0.1.3 - 2026-10-05

- Pin the published Fullbleed 2.5.7 crate, which compacts embedded font metadata while preserving retained glyph programs, metrics, mappings, and notices.
- Extend isolated package checks to Unicode font input and compiled reflow records, with independent font, text, and page-pixel checks against the previous public package.
- Keep the managed API, native ABI, runtime dependency graph, and .NET 8/9/10 platform coverage unchanged.

## 0.1.2 - 2026-10-03

- Use absolute links for sample source, API documentation, and license notices so NuGet's README renderer preserves them.
- Keep the Fullbleed 2.5.6 engine and rendering behavior from 0.1.1.

## 0.1.1 - 2026-10-03

- Pin the published Fullbleed 2.5.6 crate, including corrected bold-text extraction and finalized previews; source builds no longer need a sibling engine checkout.
- Reject native builds whose requested RID differs from the compiler host.
- Include exact dependency provenance and upstream license texts in the NuGet package.
- Test the assembled package in an isolated consumer on Windows, Linux, and both macOS architectures.
- Independently verify searchable styled text and compiled record contents, and compare saved-PDF previews across the four packaged runtimes.
- Require the CLI integration lane in CI and fail on CLI render errors.
- Add the runnable Northstar invoice example with vendored fonts and PNG previews.
- Add NuGet trusted publishing after the complete validation matrix.

## 0.1.0 - 2026-08-17

- Add the stable native ABI bridge and source-generated .NET interop.
- Add ordinary, diagnostic, metric, preview, batch, compiled-copy, fixed-binding, and reflow-binding APIs.
- Add PDF inspection and template stamp/compose finalization.
- Add the runtime-discovering structured CLI client.
- Add LINQ-friendly columnar VDP bindings, examples, tests, packaging, and cross-platform CI.
- Verify registered-font fixed VDP and finalized-preview parity against the linked Fullbleed engine.
