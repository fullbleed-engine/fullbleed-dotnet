using System.Text.Json.Serialization;

namespace FullBleed.DotNet;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(FullBleedEngineOptions))]
[JsonSerializable(typeof(NativeBatchRequest))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(RenderDiagnostics))]
[JsonSerializable(typeof(RenderMetrics))]
[JsonSerializable(typeof(ImagePagesResult))]
[JsonSerializable(typeof(BatchRenderDiagnostics))]
[JsonSerializable(typeof(CompiledDocumentStats))]
[JsonSerializable(typeof(NativeFeatures))]
[JsonSerializable(typeof(PdfInspection))]
[JsonSerializable(typeof(StampOptions))]
[JsonSerializable(typeof(ComposeOptions))]
[JsonSerializable(typeof(FinalizeResult))]
internal sealed partial class FullBleedJsonContext : JsonSerializerContext;
