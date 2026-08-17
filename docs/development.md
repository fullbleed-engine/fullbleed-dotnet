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

The native crate uses a versioned path dependency on a sibling `fullbleed-official` checkout. Cargo uses the path during development; the `version = "2.3.1"` constraint records the intended engine release.

## Local verification

```powershell
./scripts/verify.ps1
```

This stages the host native library, checks Rust formatting and Clippy, runs native tests, verifies managed formatting, builds the full solution, and runs the managed test suite. The integration suite verifies deterministic rendering, diagnostics, metrics, PNG output, in-memory and direct-to-file batches, fixed and reflow compiled bindings, inspection, template stamping/composition, concurrency, and failure-path recovery.

CLI integration tests are conditional on a healthy independently installed `fullbleed` command. Native integration tests are unconditional once the bridge is built.

## Local package

```powershell
./scripts/pack.ps1
```

The resulting package contains only the current host RID unless other native assets were already staged. Do not describe a package as multi-platform unless its `.nupkg` has been inspected for every claimed `runtimes/{rid}/native/` entry.

Exercise the packed package through a clean `PackageReference` consumer:

```powershell
./scripts/package-smoke.ps1 -SkipPack
```

## CI package assembly

The CI matrix compiles native assets on matching Windows, Linux, Intel macOS, and Apple Silicon macOS runners. A final job downloads each artifact into its RID directory, packs once, and inspects the ZIP table for every expected native path.

## Release checklist

1. Synchronize managed bridge, native bridge, Fullbleed dependency, changelog, and package metadata deliberately.
2. Run the full native/managed matrix.
3. Inspect NuGet contents and dependency metadata.
4. Exercise the packed package in a clean consumer for each RID.
5. Retain deterministic hashes, PDF inspection output, and any profile/conformance evidence used in release claims.
6. Publish only after package ownership and ecosystem registration are independently confirmed.

## Adding native functionality

Prefer a narrow exported operation over exposing Rust layout directly. Keep input/output ownership explicit, initialize every out parameter before work, convert panics to status codes, and add a managed integration test that forces both success and error cleanup paths.
