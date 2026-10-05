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

The native crate pins the published Fullbleed `2.5.7` crate with an exact Cargo dependency and a checked-in lockfile. The build and tests need no sibling engine checkout. Build scripts require the requested RID to match the Rust host, so an x64 binary cannot silently be staged as ARM64.

## Local verification

```powershell
./scripts/verify.ps1
```

This stages the host native library, checks Rust formatting and Clippy, runs native tests, verifies managed formatting, builds the full solution, and runs the managed test suite. The integration suite verifies deterministic rendering, diagnostics, metrics, PNG output, in-memory and direct-to-file batches, fixed and reflow compiled bindings, inspection, template stamping/composition, concurrency, and failure-path recovery.

CLI integration tests run when the independently installed `fullbleed` command is available. A render failure is a test failure. CI installs `fullbleed==2.5.7` and sets `FULLBLEED_REQUIRE_CLI=1`, making a missing CLI a failure too. Native integration tests are unconditional once the bridge is built. The registered-font fixture includes its font and OFL notice.

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

The evidence job independently checks five fixtures: the styled invoice, weight-700 bold text, two fixed records, two reflow records, and Unicode text using a separately registered Noto Sans font. It compares their PDF bytes and saved-PDF PNG previews across all 12 consumers, plus the invoice's HTML preview. It also rejects evidence from a missing or incorrect framework/platform combination. `pypdf` and PDFium must agree on extracted text; FontTools checks embedded glyph programs, metrics, mappings, checksums, notices, and compact metadata against the source fonts. Four source fonts are exercised; the invoice registers a fifth font that is unused in these outputs. These readers are release-check dependencies, not dependencies of applications using the .NET package.

## Font output comparison

After creating an isolated consumer with `scripts/package-smoke.ps1`, install the independent readers in a maintainer environment:

```sh
python -m pip install pypdf==6.19.0 pypdfium2==5.14.0 fonttools==4.65.0 pillow==12.3.0
python tools/verify_font_outputs.py artifacts/package-smoke
python tools/compare_font_packages.py artifacts/package-smoke
```

The comparison script requires PowerShell (`pwsh`, or an explicit `--shell` path) and the .NET 8 SDK. It downloads the previous public `0.1.2` package, checks its pinned SHA-256 and engine provenance, and renders the same inputs in another isolated consumer. The old package must fail the new compact-metadata gate as a negative control, while passing the original glyph/metric checks. Every fixture must become smaller with unchanged extracted text, native previews, and independently rendered PDFium page pixels. These are document-size checks, not timing measurements or general parity claims.

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
