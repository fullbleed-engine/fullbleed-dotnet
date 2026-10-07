# Development and release workflow

## Repository layout

```text
native/fullbleed-dotnet-native/  Rust cdylib bridge
src/FullBleed.DotNet/           managed library and CLI client
tests/FullBleed.DotNet.Tests/   unit and real-engine integration tests
samples/                        executable examples
runtimes/{rid}/native/          generated/staged package assets
scripts/                        build, verify, and pack entrypoints
```

The native crate pins the published Fullbleed `2.5.13` crate with an exact Cargo dependency and a checked-in lockfile. The build and tests need no sibling engine checkout. Build scripts require the requested RID to match the Rust host, so an x64 binary cannot silently be staged as ARM64.

## Local verification

```powershell
./scripts/verify.ps1
```

This stages the host native library, checks Rust formatting and Clippy, runs native tests, verifies managed formatting, builds the full solution, and runs the managed test suite. The integration suite verifies deterministic rendering, diagnostics, metrics, PNG output, in-memory and direct-to-file batches, fixed and reflow compiled bindings, inspection, template stamping/composition, concurrency, and failure-path recovery.

CLI integration tests run when the independently installed `fullbleed` command is available. A render failure is a test failure. CI installs `fullbleed==2.5.13` and sets `FULLBLEED_REQUIRE_CLI=1`, making a missing CLI a failure too. Native integration tests are unconditional once the bridge is built. The registered-font fixture includes its font and OFL notice.

## Local package

```powershell
./scripts/pack.ps1
```

The resulting package contains only the current host RID unless other native assets were already staged. Do not describe a package as multi-platform unless its `.nupkg` has been inspected for every claimed `runtimes/{rid}/native/` entry.

Exercise the packed package through a clean `PackageReference` consumer:

```powershell
./scripts/package-smoke.ps1 -SkipPack
./scripts/package-smoke.ps1 -SkipPack -TargetFramework net10.0 -OutputDirectory artifacts/package-smoke-net10
```

Install the matching stable SDK for each requested framework. The consumer pins that SDK family in its own `global.json`, disallows major runtime roll-forward, and fails if its actual runtime differs from the requested .NET version. The default remains `net8.0`; `net9.0` and `net10.0` are also accepted.

## CI package assembly

The CI matrix compiles native assets on matching Windows, Linux, Intel macOS, and Apple Silicon macOS runners. A packaging job downloads each artifact into its RID directory and packs once. `tools/verify_package.py --all-rids` checks native binary architectures, managed dependency metadata, required notices, and staged-versus-packaged hashes.

A second matrix consumes that same assembled package on .NET 8, 9, and 10 for each of the four platforms (12 consumers). `scripts/package-smoke.ps1` creates a consumer and a fresh NuGet cache outside the checkout and clears `FULLBLEED_NATIVE_LIBRARY` for the child run. This prevents the development resolver from masking missing or incorrect packaged assets. Its retained PDF, PNG, compiled-record PDF, and JSON hashes come from the package, not a ProjectReference. Evidence includes the SDK, target framework, actual runtime version, and package checksum.

The evidence job independently checks six fixtures: the styled invoice, an explicit regular-face invoice control, weight-700 bold text, two fixed records, two reflow records, and Unicode text using a separately registered Noto Sans font. It compares their PDF bytes and saved-PDF PNG previews across all 12 consumers, plus the invoice's HTML preview. It also rejects evidence from a missing or incorrect framework/platform combination. `pypdf` and PDFium must agree on extracted text; FontTools checks embedded glyph programs, metrics, mappings, checksums, notices, and compact metadata against all five source fonts. These readers are release-check dependencies, not dependencies of applications using the .NET package.

Each isolated consumer also renders 22 regular/italic family cases: explicit-face controls, both registration orders through ordinary/fixed/reflow output, and ordinary `@font-face` mappings. The independent family verifier checks embedded face names and notice tables, extracted text, and native/PDFium preview pixels against those controls. Their PDFs and previews must agree across all 12 consumers.

## Tagged-structure verification

The isolated NuGet consumer also renders a rich specimen through ordinary PDF,
PDF/UA-1 and PDF/UA-2 profiles. It includes mixed-size decorated text, scoped
headers, nested lists, a definition list, and figures with first/last captions.
Every run retains exact source, PDF bytes, native previews and diagnostics.

`tools/verify_tagged_structure.py <consumer>/tagged-structure` verifies hashes,
text, structure-tree presence and the two PDF/UA profiles with veraPDF 1.30.2.
The maintainer-only validator download has a pinned SHA-256 and requires Java.
CI runs it on all 12 platform/framework consumers, then compares PDF bytes,
previews, text and validator results. None of these tools are runtime NuGet
dependencies.

`tools/compare_tagged_packages.py <consumer>` downloads hash-pinned public 0.1.6
and runs the same isolated consumer. Both old PDF/UA outputs must fail the
independent validator and the candidate must pass. The ordinary PDF bytes,
all text and previews must remain unchanged. The `tagged-structure-comparison`
artifact retains the failing old PDFs and validator reports. These are
specimen-level machine checks, not blanket PDF accessibility acceptance.

## Standard-font preview verification

After creating an isolated packed consumer, run:

```sh
python tools/verify_standard_fonts.py artifacts/package-smoke/standard-fonts
```

The fixture uses all twelve Latin standard faces without embedded font programs, plus an embedded Inter control, through ordinary PDF, fixed bindings, reflow and compact reflow output: 52 cases. It retains inputs, PDFs, repeated saved-PDF previews, direct HTML previews, text/font inspection and independent PDFium images. Every candidate preview must contain visible text; hashes must agree across all 12 platform/framework consumers.

CI also runs `tools/compare_standard_font_packages.py` inside a private Linux mount namespace. The script refuses to run outside that isolated namespace, hides the system-font directories with temporary bind mounts, and clears `FULLBLEED_FONT_DIR`. It runs the same isolated consumers from hash-pinned public 0.1.5 and the candidate NuGet. The old package must produce blank previews for all 48 unembedded-font cases and twelve direct HTML previews; the candidate must pass. All 52 PDF byte sequences and the four embedded-font controls must remain unchanged. Candidate preview hashes must match the ordinary host's output. Host fonts are never modified.

The `standard-font-comparison` artifact retains both consumers' outputs, package identity, logs and the comparison. These fixtures establish portable previews for the tested Latin faces; they do not establish original-font-design parity, universal glyph coverage or PDF conformance. The core's full Standard 14 glyph checks are separate. The historical reviewed 2.5.10 layout hashes remain unchanged.

## Inline-layout verification

Every isolated package consumer renders 54 cases: 12 wrapping styles through ordinary PDF, compiled fixed, throughput reflow, and compact reflow output, plus flex and inline-block labels at three letter spacings. The tests retain exact HTML/CSS, explicit font files and licenses, PDF bytes, and saved-PDF previews. Rendering twice must produce identical PDF bytes.

```sh
python tools/verify_inline_layout.py artifacts/package-smoke/inline-layout
python tools/compare_inline_packages.py artifacts/package-smoke
```

Use the independent reader dependencies listed below. The comparison requires the .NET 8 SDK and PowerShell (`pwsh`, or `--shell` with an explicit path). It checks the downloaded public 0.1.4 package's SHA-256 and engine provenance, then runs the same consumer outside the checkout. That package must fail the reviewed 40 styled-wrapping cases and four tracked-label cases. Ten healthy controls must keep the same text; eight wrapping controls also keep identical PDF bytes and native/PDFium previews. Corrected intrinsic advances move the two zero-tracking label controls slightly. Their output must match the separately reviewed PDF/preview hashes in `tests/FullBleed.DotNet.PackageSmoke/reviewed-layout-2.5.10.json`. Every candidate case must pass.

The verifier checks content with pypdf and PDFium, then checks physical word positions for overlap, line order, boundaries, and unintended wrapping. Fixed bindings paint replacements after the static base in the PDF stream; their content is checked once and their visual order is verified independently. Proportional Helvetica and Times spacing is compared with a whole-string control. Their native previews use the engine's bundled outline substitutes. Their visual check also uses PDFium independently.

CI checks all 12 platform/framework consumers. All 54 PDF byte sequences, independent PDFium pixel buffers and native PNG bytes must agree across consumers. The verifier confirms that 46 fixtures embed their fonts and that eight exercise unembedded Helvetica/Times faces. Per-platform hashes remain in the evidence for inspection.

`inline-comparison` retains the old outputs, failure report, package identity, and before/after summary; candidate output is retained in `package-consumer-*` and `cross-platform-evidence`. These are fixture-specific checks, not universal rendering parity or standards-conformance evidence.

## Font output comparison

After creating an isolated consumer with `scripts/package-smoke.ps1`, install the independent readers in a maintainer environment:

```sh
python -m pip install pypdf==6.19.0 pypdfium2==5.14.0 fonttools==4.65.0 pillow==12.3.0
python tools/verify_font_outputs.py artifacts/package-smoke
python tools/verify_font_families.py artifacts/package-smoke/font-families
python tools/compare_font_packages.py artifacts/package-smoke
```

The comparison script requires PowerShell (`pwsh`, or an explicit `--shell` path) and the .NET 8 SDK. It retains the public `0.1.3` package as the font-family negative control, checking its pinned SHA-256 and engine provenance before rendering the same inputs in another isolated consumer. That package must fail the normal-text family check when italic is registered first, while still passing the glyph/metric checks. The current invoice must match its explicit regular-face control. Fixed, reflow, and Unicode fixtures preserve PDF bytes, extracted text, native previews, and PDFium pixels across versions.

The invoice, its explicit-face control, and the bold fixture retain their normalized text but change layout in engine 2.5.10: inline advances use the corrected spacing and tracked footer labels use their corrected intrinsic widths. These three outputs must match the reviewed versioned PDF/preview hashes, in addition to the independent glyph, metric, face-selection, and text checks. The baseline is not regenerated by CI. These checks make no general parity or speed claim; the historical [0.1.4 release evidence](https://github.com/fullbleed-engine/fullbleed-dotnet/releases/tag/v0.1.4) retains the earlier comparison before the inline-layout change.

The earlier 0.1.2-to-0.1.3 compaction comparison remains available in the [0.1.3 release evidence](https://github.com/fullbleed-engine/fullbleed-dotnet/releases/tag/v0.1.3). Its historical inputs and measurements are not rewritten for the family-selection fix.

CI retains the baseline PDFs and previews, exact input HTML/CSS/records, source fonts with licenses, package checksums, reader versions, negative-control result, and comparison report in `font-comparison`. The candidate outputs are in the corresponding `package-consumer-*` artifacts; `cross-platform-evidence` retains the independent reports and PDFium renders for all 12 consumers. Locally the comparison writes to a new `artifacts/font-comparison` directory; pass `--output` to preserve an earlier run.

When changing the Cargo lockfile, use Python 3.11 or later to regenerate and inspect the committed dependency provenance and upstream license texts:

```sh
python tools/native_provenance.py
python tools/native_provenance.py --check
```

The package includes `native-provenance.json` and `licenses/native/`. Python is needed for these maintainer checks and the CLI integration lane; applications using the native .NET bindings do not need Python.

## NuGet trusted publishing

The `Release NuGet` workflow validates and consumes the package on all four platforms before publishing the exact validated artifact. It publishes only from a `v<package-version>` tag. Manual branch runs validate without uploading.

Configure a NuGet trusted-publishing policy for owner `fullbleed-engine`, repository `fullbleed-dotnet`, workflow `release.yml`, environment `nuget`, and package pattern `FullBleed.DotNet`. Set the account's public NuGet profile name as repository variable `NUGET_USER`. The publishing job exchanges GitHub OIDC for a temporary credential; no long-lived API key is needed.

After publication, verify NuGet's public index, package metadata, and downloaded contents. NuGet can add a repository signature, so compare archive entries and native-library hashes against the retained manifest rather than expecting the signed package's whole-file hash to equal the unsigned upload. Keep publication claims separate from a successful build.

## Release checklist

1. Synchronize managed bridge, native bridge, Fullbleed dependency, changelog, and package metadata deliberately.
2. Run the full native/managed matrix.
3. Inspect NuGet contents and dependency metadata.
4. Exercise the packed package in a clean consumer for each RID.
5. Retain deterministic hashes, PDF inspection output, and any profile/conformance evidence used in release claims.
6. Publish only after package ownership and ecosystem registration are independently confirmed.

## Adding native functionality

Prefer a narrow exported operation over exposing Rust layout directly. Keep input/output ownership explicit, initialize every out parameter before work, convert panics to status codes, and add a managed integration test that forces both success and error cleanup paths.
