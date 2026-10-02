# Third-party notices

The managed `FullBleed.DotNet` assembly uses only .NET platform libraries. Its native runtime
bridge statically links Fullbleed PDF Engine and the following Rust crates. Their license texts
and exact versions are retained in `native-provenance.json` and `licenses/native/`,
generated from the checked-in `Cargo.lock`. The bundle also includes build-time
procedural-macro dependencies for complete provenance.

| Component | License |
| --- | --- |
| Fullbleed PDF Engine | MIT |
| `base64` | MIT OR Apache-2.0 |
| `serde`, `serde_core`, `serde_derive` | MIT OR Apache-2.0 |
| `serde_json` | MIT OR Apache-2.0 |
| Transitive Fullbleed and bridge dependencies | See `native-provenance.json` and `licenses/native/` |

The repository's test and showcase fonts are licensed under the SIL Open Font License.
Their unmodified license texts are stored beside the font files. These sample assets
are not included in the native runtime NuGet package.

This notice is informational and does not replace the corresponding upstream license texts.
