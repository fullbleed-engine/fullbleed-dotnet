# Third-party notices

The managed `FullBleed.DotNet` assembly uses only .NET platform libraries. Its native runtime
bridge statically links Fullbleed PDF Engine and the following Rust crates. Their license texts
and exact versions must be retained in release provenance generated from `Cargo.lock`.

| Component | License |
| --- | --- |
| Fullbleed PDF Engine | MIT |
| `base64` | MIT OR Apache-2.0 |
| `serde`, `serde_core`, `serde_derive` | MIT OR Apache-2.0 |
| `serde_json` | MIT OR Apache-2.0 |
| Transitive Fullbleed dependencies | See the Fullbleed distribution's `THIRD_PARTY_LICENSES.md` |

This notice is informational and does not replace the corresponding upstream license texts.
