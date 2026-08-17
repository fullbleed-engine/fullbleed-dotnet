namespace FullBleed.DotNet;

public abstract record FullBleedCliDocumentRequest
{
    public string? Html { get; init; }

    public string? HtmlPath { get; init; }

    public string? Css { get; init; }

    public string? CssPath { get; init; }

    public string? PageSize { get; init; }

    public double? PageWidthPt { get; init; }

    public double? PageHeightPt { get; init; }

    public double? MarginPt { get; init; }

    public string? PageMargins { get; init; }

    public bool? ReuseXobjects { get; init; }

    public bool? SvgFormXobjects { get; init; }

    public bool? SvgRasterFallback { get; init; }

    public bool? UnicodeSupport { get; init; }

    public bool? ShapeText { get; init; }

    public bool? UnicodeMetrics { get; init; }

    public string? PdfVersion { get; init; }

    public string? PdfProfile { get; init; }

    public string? OutputIntentIccPath { get; init; }

    public string? OutputIntentIdentifier { get; init; }

    public string? OutputIntentInfo { get; init; }

    public int? OutputIntentComponents { get; init; }

    public string? ColorSpace { get; init; }

    public string? DocumentLanguage { get; init; }

    public string? DocumentTitle { get; init; }

    public string? HeaderEach { get; init; }

    public string? HeaderHtmlEach { get; init; }

    public string? FooterEach { get; init; }

    public string? FooterHtmlEach { get; init; }

    public string? WatermarkText { get; init; }

    public string? WatermarkHtml { get; init; }

    public string? WatermarkImage { get; init; }

    public string? WatermarkLayer { get; init; }

    public string? WatermarkSemantics { get; init; }

    public double? WatermarkOpacity { get; init; }

    public double? WatermarkRotation { get; init; }

    public string? JitMode { get; init; }

    public string? EmitJitPath { get; init; }

    public string? EmitPerfPath { get; init; }

    public string? EmitGlyphReportPath { get; init; }

    public string? EmitPageDataPath { get; init; }

    public string? EmitComposePlanPath { get; init; }

    public string? EmitImageDirectory { get; init; }

    public int? ImageDpi { get; init; }

    public string? EmitManifestPath { get; init; }

    public string? TemplateBinding { get; init; }

    public string? Templates { get; init; }

    public double? TemplateDx { get; init; }

    public double? TemplateDy { get; init; }

    public string? ComposeAnnotationMode { get; init; }

    public List<string> Assets { get; init; } = [];

    public string? AssetKind { get; init; }

    public string? AssetName { get; init; }

    public bool AssetTrusted { get; init; }

    public bool AllowRemoteAssets { get; init; }

    public bool VerboseAssets { get; init; }

    public string? Profile { get; init; }

    public bool AllowFallbacks { get; init; }

    public List<string> FailOn { get; init; } = [];

    public string? DeterministicHashPath { get; init; }

    public int? BudgetMaxPages { get; init; }

    public long? BudgetMaxBytes { get; init; }

    public double? BudgetMaxMs { get; init; }

    public string? ReproRecordPath { get; init; }

    public string? ReproCheckPath { get; init; }

    public List<string> AdditionalArguments { get; init; } = [];
}

public sealed record FullBleedCliRenderRequest : FullBleedCliDocumentRequest
{
    public required string OutputPath { get; init; }
}

public sealed record FullBleedCliVerifyRequest : FullBleedCliDocumentRequest
{
    public string? EmitPdfPath { get; init; }
}
