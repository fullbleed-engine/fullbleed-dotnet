namespace FullBleed.DotNet.Tests;

public sealed class CliArgumentTests
{
    [Fact]
    public void DocumentArgumentsCoverTogglesRepeatedFailuresAndInvariantNumbers()
    {
        var request = new FullBleedCliRenderRequest
        {
            Html = "<p>Hello</p>",
            Css = "p { color: red; }",
            OutputPath = "out.pdf",
            ReuseXobjects = false,
            ShapeText = true,
            WatermarkOpacity = 0.25,
            FailOn = ["overflow", "missing-glyphs"],
        };
        var arguments = new List<string>();

        FullBleedCliArguments.AddDocumentArguments(arguments, request);

        Assert.Contains("--html-str", arguments);
        Assert.Contains("--css-str", arguments);
        Assert.Contains("--no-reuse-xobjects", arguments);
        Assert.Contains("--shape-text", arguments);
        Assert.Equal(2, arguments.Count(value => value == "--fail-on"));
        var opacityIndex = arguments.IndexOf("--watermark-opacity");
        Assert.Equal("0.25", arguments[opacityIndex + 1]);
    }

    [Fact]
    public void DocumentArgumentsRejectAmbiguousInlineAndFileInput()
    {
        var request = new FullBleedCliRenderRequest
        {
            Html = "<p>Hello</p>",
            HtmlPath = "input.html",
            OutputPath = "out.pdf",
        };

        Assert.Throws<ArgumentException>(() =>
            FullBleedCliArguments.AddDocumentArguments([], request));
    }
}
