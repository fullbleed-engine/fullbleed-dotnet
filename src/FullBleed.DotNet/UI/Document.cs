namespace FullBleed.DotNet.UI;

/// <summary>A complete static print document with an attached stylesheet and an explicit rendering engine.</summary>
public sealed class Document
{
    private readonly Node _content;
    private string _title = "Fullbleed document";
    private string _language = "en";
    private StyleSheet _styles = new();

    public Document(params Node?[]? children) : this((IEnumerable<Node?>)(children ?? [])) { }
    public Document(IEnumerable<Node?> children) => _content = Element.Create("main", children).WithClass("fb-document-root report-root");

    public string Title
    {
        get => _title;
        init { ArgumentNullException.ThrowIfNull(value); _title = value; }
    }
    public string Language
    {
        get => _language;
        init { ArgumentException.ThrowIfNullOrWhiteSpace(value); _language = value; }
    }
    public StyleSheet Styles
    {
        get => _styles;
        init { ArgumentNullException.ThrowIfNull(value); _styles = value; }
    }
    public bool IncludeComponentStyles { get; init; } = true;

    /// <summary>Serialize markup only. Direct engine calls must also receive ToCss().</summary>
    public string ToHtml() => "<!doctype html><html lang=\"" + Node.Escape(Language) +
        "\"><head><meta charset=\"utf-8\"><title>" + Node.Escape(Title) +
        "</title></head><body>" + _content.ToHtml() + "</body></html>";

    public string ToCss() => IncludeComponentStyles
        ? new StyleSheet(Components.DefaultStyles, Styles).ToCss()
        : Styles.ToCss();

    public byte[] RenderPdf(FullBleedEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.RenderPdf(ToHtml(), ToCss());
    }

    public long RenderPdfToFile(FullBleedEngine engine, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.RenderPdfToFile(ToHtml(), ToCss(), outputPath);
    }

    public RenderDiagnosticsResult RenderPdfWithDiagnostics(FullBleedEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.RenderPdfWithDiagnostics(ToHtml(), ToCss());
    }

    public ImagePagesResult RenderImagePagesToDirectory(FullBleedEngine engine, string outputDirectory, uint dpi = 150, string stem = "render")
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.RenderImagePagesToDirectory(ToHtml(), ToCss(), outputDirectory, dpi, stem);
    }

    public FullBleedCompiledDocument Compile(FullBleedEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Compile(ToHtml(), ToCss());
    }

    public RenderJob ToRenderJob() => new(ToHtml(), ToCss());
}
