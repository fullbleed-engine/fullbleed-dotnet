namespace FullBleed.DotNet;

public sealed record RenderOptions(
    float PageWidthPt = 595.28f,
    float PageHeightPt = 841.89f,
    float MarginTopPt = 36f,
    float MarginRightPt = 36f,
    float MarginBottomPt = 36f,
    float MarginLeftPt = 36f)
{
    public static RenderOptions Default { get; } = new();

    internal FullBleedEngineOptions ToEngineOptions() => new()
    {
        PageSize = new PageSize(PageWidthPt, PageHeightPt),
        Margins = new PageMargins(MarginTopPt, MarginRightPt, MarginBottomPt, MarginLeftPt),
    };
}
