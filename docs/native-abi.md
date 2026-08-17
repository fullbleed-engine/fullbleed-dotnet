# Native ABI contract

The native layer is private implementation detail for the NuGet package, but its rules are documented so managed and non-managed consumers can audit ownership.

## Versioning

`fullbleed_dotnet_abi_version()` currently returns `1`. The managed assembly checks this value before creating an engine. ABI-breaking changes require a new integer and a coordinated managed release.

## Handles

- `fullbleed_engine_create` returns an opaque engine pointer; release it exactly once with `fullbleed_engine_free`.
- `fullbleed_engine_compile` returns an opaque compiled-document pointer; release it exactly once with `fullbleed_compiled_free`.
- A compiled document owns the engine state needed for rendering and may outlive its creating engine.
- Calls accept null only where explicitly documented. A null handle returns `InvalidHandle`.

The managed layer wraps both pointers in `SafeHandle` implementations.

## Buffers and errors

Native byte/JSON outputs use `FbByteBuffer { ptr, len }`. Release a non-null buffer exactly once with `fullbleed_buffer_free(ptr, len)`. Error messages are UTF-8 C strings and must be released with `fullbleed_string_free`.

Every fallible export:

1. initializes output buffers/handles to null;
2. catches Rust panics;
3. returns an `FbStatusCode`;
4. writes an owned error string on failure when possible.

The managed layer copies bytes into managed memory and frees native allocations in `finally` blocks.

## Configuration and result schemas

Engine options, bindings, batch jobs, and compose plans cross the ABI as UTF-8 JSON. Binary PDF/PNG data does not use JSON or base64. Stable schema identifiers are included on native JSON results.

Unknown option fields are ignored for compatible forward evolution. Invalid known values return `InvalidOptions`. Managed source-generated serialization removes reflection-based marshalling and keeps the public path compatible with trimming and Native AOT.

## Threading

Engine methods use shared immutable state plus engine-owned synchronization. Compiled documents are immutable. The bridge adds no global process lock; Fullbleed's native ordered parallel and compiled-reflow execution policies remain authoritative.
