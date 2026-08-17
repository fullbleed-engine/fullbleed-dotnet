using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FullBleed.DotNet;

public enum PdfVersion
{
    Pdf17,
    Pdf20,
}

public enum PdfProfile
{
    None,
    PdfA1a,
    PdfA1b,
    PdfA2a,
    PdfA2b,
    PdfA2u,
    PdfA3a,
    PdfA3b,
    PdfA3u,
    PdfA4,
    PdfA4e,
    PdfA4f,
    PdfX4,
    PdfUa1,
    PdfUa2,
    PdfVt1,
    Wtpdf1r,
    Wtpdf1a,
    Tagged,
}

public enum FullBleedColorSpace
{
    Rgb,
    Cmyk,
}

public enum FullBleedJitMode
{
    Off,
    PlanOnly,
    PlanAndReplay,
}

public enum FullBleedLayoutStrategy
{
    Eager,
    Lazy,
}

public enum FullBleedAssetKind
{
    Css,
    Font,
    Image,
    Pdf,
    Svg,
    Other,
}

public enum WatermarkContentKind
{
    Text,
    Html,
    Image,
}

public enum WatermarkLayer
{
    Background,
    Overlay,
}

public enum WatermarkSemantics
{
    Visual,
    Artifact,
    Ocg,
}

public enum CompiledFlowCompression
{
    Throughput,
    Compact,
}

public enum ComposeAnnotationMode
{
    None,
    LinkOnly,
    CarryWidgets,
}

public sealed record PageSize(float WidthPt, float HeightPt)
{
    public static PageSize A4 { get; } = new(595.28f, 841.89f);

    public static PageSize Letter { get; } = new(612f, 792f);

    public static PageSize FromInches(float width, float height) => new(width * 72f, height * 72f);

    public static PageSize FromMillimeters(float width, float height) =>
        new(width * 72f / 25.4f, height * 72f / 25.4f);
}

public sealed record PageMargins(float TopPt, float RightPt, float BottomPt, float LeftPt)
{
    public static PageMargins All(float points) => new(points, points, points, points);
}

public sealed record RgbColor(float R, float G, float B)
{
    public static RgbColor Black { get; } = new(0f, 0f, 0f);

    public static RgbColor Gray(float value) => new(value, value, value);
}

public sealed record TextDecorationOptions
{
    public string? First { get; init; }

    public string? Each { get; init; }

    public string? Last { get; init; }

    public float XPt { get; init; }

    public float YPt { get; init; } = 18f;

    public string FontName { get; init; } = "Helvetica";

    public float FontSizePt { get; init; } = 9f;

    public RgbColor Color { get; init; } = RgbColor.Black;
}

public sealed record HtmlDecorationOptions
{
    public string? First { get; init; }

    public string? Each { get; init; }

    public string? Last { get; init; }

    public float XPt { get; init; }

    public float YPt { get; init; } = 18f;

    public float WidthPt { get; init; }

    public float HeightPt { get; init; }
}

public sealed record FullBleedWatermark
{
    public required WatermarkContentKind Kind { get; init; }

    public required string Value { get; init; }

    public WatermarkLayer Layer { get; init; } = WatermarkLayer.Overlay;

    public WatermarkSemantics Semantics { get; init; } = WatermarkSemantics.Artifact;

    public float Opacity { get; init; } = 0.15f;

    public float RotationDeg { get; init; }

    public string FontName { get; init; } = "Helvetica";

    public float FontSizePt { get; init; } = 48f;

    public RgbColor Color { get; init; } = RgbColor.Gray(0.6f);

    public static FullBleedWatermark Text(string text) => new()
    {
        Kind = WatermarkContentKind.Text,
        Value = text,
    };
}

public sealed record OutputIntentOptions
{
    public string? IccProfilePath { get; init; }

    public string? IccProfileBase64 { get; init; }

    public required byte Components { get; init; }

    public required string Identifier { get; init; }

    public string? Info { get; init; }

    public static OutputIntentOptions FromBytes(
        ReadOnlySpan<byte> profile,
        byte components,
        string identifier,
        string? info = null) => new()
        {
            IccProfileBase64 = Convert.ToBase64String(profile),
            Components = components,
            Identifier = identifier,
            Info = info,
        };
}

public sealed record FullBleedAsset
{
    public string? Name { get; init; }

    public required FullBleedAssetKind Kind { get; init; }

    public string? Path { get; init; }

    public string? DataBase64 { get; init; }

    public string? Source { get; init; }

    public bool Trusted { get; init; }

    public static FullBleedAsset FromPath(
        string path,
        FullBleedAssetKind kind,
        string? name = null,
        bool trusted = false) => new()
        {
            Path = path,
            Kind = kind,
            Name = name,
            Trusted = trusted,
        };

    public static FullBleedAsset FromBytes(
        ReadOnlySpan<byte> data,
        FullBleedAssetKind kind,
        string name,
        bool trusted = false) => new()
        {
            DataBase64 = Convert.ToBase64String(data),
            Kind = kind,
            Name = name,
            Trusted = trusted,
        };
}

public sealed record TemplateBindingOptions
{
    public string? DefaultTemplateId { get; init; }

    public Dictionary<string, string> ByPageTemplate { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> ByFeature { get; init; } = new(StringComparer.Ordinal);

    public string FeaturePrefix { get; init; } = "fb.feature.";
}

public sealed record FullBleedEngineOptions
{
    public PageSize? PageSize { get; init; }

    public PageMargins? Margins { get; init; }

    [JsonPropertyName("pageMargins")]
    public Dictionary<int, PageMargins> PageMarginsByPage { get; init; } = [];

    public List<string> FontDirectories { get; init; } = [];

    public List<string> FontFiles { get; init; } = [];

    public bool? ReuseXobjects { get; init; }

    public bool? SvgFormXobjects { get; init; }

    public bool? SvgRasterFallback { get; init; }

    public bool? UnicodeSupport { get; init; }

    public bool? ShapeText { get; init; }

    public bool? UnicodeMetrics { get; init; }

    public PdfVersion? PdfVersion { get; init; }

    public PdfProfile? PdfProfile { get; init; }

    public FullBleedColorSpace? ColorSpace { get; init; }

    public OutputIntentOptions? OutputIntent { get; init; }

    public string? DocumentLanguage { get; init; }

    public string? DocumentTitle { get; init; }

    public FullBleedJitMode? JitMode { get; init; }

    public FullBleedLayoutStrategy? LayoutStrategy { get; init; }

    public bool AcceptLazyLayoutCost { get; init; }

    public int? LazyMaxPasses { get; init; }

    public double? LazyBudgetMs { get; init; }

    public string? DebugLogPath { get; init; }

    public string? PerfLogPath { get; init; }

    public bool? PerfEnabled { get; init; }

    public TextDecorationOptions? Header { get; init; }

    public HtmlDecorationOptions? HeaderHtml { get; init; }

    public TextDecorationOptions? Footer { get; init; }

    public FullBleedWatermark? Watermark { get; init; }

    public Dictionary<string, string> PaginatedContext { get; init; } = new(StringComparer.Ordinal);

    public TemplateBindingOptions? TemplateBinding { get; init; }

    public List<FullBleedAsset> Assets { get; init; } = [];
}

public sealed record RenderJob(string Html, string Css = "");

public sealed record BatchRenderOptions
{
    public bool Parallel { get; init; } = true;

    public bool IncludePageData { get; init; }
}

public sealed record RenderDiagnosticsResult(byte[] Pdf, RenderDiagnostics Diagnostics);

public sealed record RenderDiagnostics
{
    public required string Schema { get; init; }

    public JsonElement? PageData { get; init; }

    public List<MissingGlyph> MissingGlyphs { get; init; } = [];
}

public sealed record MissingGlyph
{
    public uint Codepoint { get; init; }

    public required string Character { get; init; }

    public List<string> FontsTried { get; init; } = [];

    public int Count { get; init; }
}

public sealed record RenderMetricsResult(byte[] Pdf, RenderMetrics Metrics);

public sealed record RenderMetrics
{
    public required string Schema { get; init; }

    public double TotalRenderMs { get; init; }

    public long TotalBytes { get; init; }

    public List<PageRenderMetrics> Pages { get; init; } = [];
}

public sealed record PageRenderMetrics
{
    public int PageNumber { get; init; }

    public double RenderMs { get; init; }

    public long CommandCount { get; init; }

    public long FlowableCount { get; init; }

    public long ContentBytes { get; init; }
}

public sealed record ImagePagesResult
{
    public required string Schema { get; init; }

    public List<string> Paths { get; init; } = [];

    public uint Dpi { get; init; }
}

public sealed record BatchRenderResult
{
    public required byte[] Pdf { get; init; }

    public required BatchRenderDiagnostics Diagnostics { get; init; }
}

public sealed record BatchFileRenderResult
{
    public long BytesWritten { get; init; }

    public required BatchRenderDiagnostics Diagnostics { get; init; }
}

public sealed record BatchRenderDiagnostics
{
    public required string Schema { get; init; }

    public int JobCount { get; init; }

    public bool ParallelRequested { get; init; }

    public bool ParallelUsed { get; init; }

    public bool SharedCss { get; init; }

    public JsonElement? PageData { get; init; }
}

public sealed record CompiledDocumentStats
{
    public required string Schema { get; init; }

    public int PageCount { get; init; }

    public long CommandCount { get; init; }

    public double CompileMs { get; init; }

    public List<string> BindingSlots { get; init; } = [];

    public int BindingProgramPageCount { get; init; }

    public long BindingProgramCommandCount { get; init; }

    public bool ReflowProgramReady { get; init; }

    public string? ReflowProgramError { get; init; }

    public List<string> ReflowBindingSlots { get; init; } = [];

    public long ReflowProgramNodeCount { get; init; }

    public long ReflowProgramBindingTextNodeCount { get; init; }

    public long ReflowProgramHtmlBindingNodeCount { get; init; }

    public List<string> ReflowCompressionModes { get; init; } = [];

    public required string ReflowDefaultCompression { get; init; }
}

public sealed record NativeFeatures
{
    public required string Schema { get; init; }

    public uint AbiVersion { get; init; }

    public required string BindingVersion { get; init; }

    public bool SvgRaster { get; init; }

    public bool CompiledDocument { get; init; }

    public bool CompiledFixedBindings { get; init; }

    public bool CompiledReflowBindings { get; init; }

    public List<string> CompiledFlowCompressionModes { get; init; } = [];
}

public sealed record PdfInspection
{
    public required string Schema { get; init; }

    public required string Path { get; init; }

    public required string PdfVersion { get; init; }

    public int PageCount { get; init; }

    public bool Encrypted { get; init; }

    public long FileSizeBytes { get; init; }

    public List<PdfInspectionWarning> Warnings { get; init; } = [];

    public required PdfProfileInspection Profile { get; init; }

    public required PdfCompositionInspection Composition { get; init; }
}

public sealed record PdfInspectionWarning(string Code, string Message);

public sealed record PdfCompositionInspection(bool Supported, List<string> Issues);

public sealed record PdfProfileInspection
{
    public List<string> Claims { get; init; } = [];

    public bool MetadataPresent { get; init; }

    public bool OutputIntentPresent { get; init; }

    public bool StructTreeRootPresent { get; init; }

    public bool MarkInfoPresent { get; init; }

    public bool LangPresent { get; init; }

    public int EmbeddedFontCount { get; init; }

    public bool EmbeddedFilesPresent { get; init; }

    public bool PdfDeclarationPresent { get; init; }

    public bool DpartRootPresent { get; init; }

    public bool DpartPresent { get; init; }

    public bool PageDpartPresent { get; init; }

    public bool PdfvtDpartRootNodeValid { get; init; }

    public bool PdfvtDpartParentValid { get; init; }

    public bool PdfvtDpartNodeNameListValid { get; init; }

    public bool PdfvtDpartLeafValid { get; init; }

    public bool PdfvtDpartPageRangeValid { get; init; }

    public bool PdfvtDpartGraphValid { get; init; }

    public bool? PdfvtModDateMatchesXmp { get; init; }

    public List<string> SeedBlockers { get; init; } = [];
}

public sealed record StampOptions
{
    public List<int[]>? PageMap { get; init; }

    public float Dx { get; init; }

    public float Dy { get; init; }
}

public sealed record ComposeTemplate(
    string TemplateId,
    string PdfPath,
    string? Sha256 = null,
    int? PageCount = null);

public sealed record ComposePage(
    string TemplateId,
    int TemplatePageIndex,
    int OverlayPageIndex,
    float Dx = 0f,
    float Dy = 0f);

public sealed record ComposeOptions
{
    public List<ComposeTemplate> Templates { get; init; } = [];

    public List<ComposePage> Plan { get; init; } = [];

    public ComposeAnnotationMode AnnotationMode { get; init; } = ComposeAnnotationMode.LinkOnly;
}

public sealed record FinalizeResult
{
    public required string Schema { get; init; }

    public int PagesWritten { get; init; }

    public required string OutputPath { get; init; }
}

internal sealed record NativeBatchRequest(
    List<RenderJob> Jobs,
    bool Parallel,
    bool IncludePageData);

public sealed class BindingMap<T>
{
    private readonly List<(string Name, Func<T, string> Selector)> _bindings = [];

    public BindingMap<T> Bind(
        string slot,
        Func<T, string?> selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(selector);
        _bindings.Add((slot, item => selector(item) ?? string.Empty));
        return this;
    }

    public BindingMap<T> Bind<TValue>(
        string slot,
        Func<T, TValue> selector,
        string? format = null,
        IFormatProvider? formatProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(selector);
        formatProvider ??= CultureInfo.InvariantCulture;
        _bindings.Add((slot, item => Format(selector(item), format, formatProvider)));
        return this;
    }

    internal Dictionary<string, List<string>> Materialize(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_bindings.Count == 0)
        {
            throw new InvalidOperationException("At least one binding must be configured.");
        }

        var duplicate = _bindings
            .GroupBy(binding => binding.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Binding slot '{duplicate.Key}' was configured more than once.");
        }

        var columns = _bindings.ToDictionary(
            binding => binding.Name,
            _ => new List<string>(),
            StringComparer.Ordinal);
        foreach (var item in source)
        {
            foreach (var binding in _bindings)
            {
                columns[binding.Name].Add(binding.Selector(item));
            }
        }

        if (columns.First().Value.Count == 0)
        {
            throw new ArgumentException("The record sequence must contain at least one item.", nameof(source));
        }

        return columns;
    }

    private static string Format<TValue>(TValue value, string? format, IFormatProvider provider)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value is IFormattable formattable
            ? formattable.ToString(format, provider) ?? string.Empty
            : value.ToString() ?? string.Empty;
    }
}
