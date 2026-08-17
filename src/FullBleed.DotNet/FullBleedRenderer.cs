namespace FullBleed.DotNet;

// Compatibility facade retained for callers of the original proof-of-concept API.
public static class FullBleedRenderer
{
    public static byte[] Render(string html, string css = "", RenderOptions? options = null)
    {
        using var engine = new FullBleedEngine((options ?? RenderOptions.Default).ToEngineOptions());
        return engine.RenderPdf(html, css);
    }

    public static void RenderToFile(
        string html,
        string css,
        string outputPath,
        RenderOptions? options = null)
    {
        using var engine = new FullBleedEngine((options ?? RenderOptions.Default).ToEngineOptions());
        engine.RenderPdfToFile(html, css, outputPath);
    }
}
