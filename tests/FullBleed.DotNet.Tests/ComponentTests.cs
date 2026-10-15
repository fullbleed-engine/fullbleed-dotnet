using System.Globalization;
using FullBleed.DotNet.UI;
using static FullBleed.DotNet.UI.Components;

namespace FullBleed.DotNet.Tests;

public sealed class ComponentTests
{
    [Fact]
    public void FunctionsComposeEscapedRichTextAndForwardCallerStyles()
    {
        static Element Notice(string customer, string className, Style style) => Card(
            Paragraph("For ", Strong(customer)), Paragraph("Paid"))
            .WithClass("notice", className).WithStyle(style);

        var notice = Notice("A & <B> \"C\" 'D'", "paid notice", new Style { ["color"] = "#315d9b" });

        Assert.Equal("ui-card notice paid", notice.ClassName);
        Assert.Equal("<div class=\"ui-card notice paid\" style=\"color: #315d9b;\"><p>For <strong>A &amp; &lt;B&gt; &quot;C&quot; &#x27;D&#x27;</strong></p><p>Paid</p></div>", notice.ToHtml());
    }

    [Fact]
    public void LinqChildrenAreCapturedOnceInOrder()
    {
        var calls = 0;
        var data = new List<string> { "one", "two" };
        var tree = Stack(Heading(1, "Records"), Fragment(data.Select(value =>
        {
            calls++;
            return Paragraph(value);
        })));
        data.Add("later");

        Assert.Equal(2, calls);
        Assert.Contains("<p>one</p><p>two</p>", tree.ToHtml());
        Assert.Equal(tree.ToHtml(), tree.ToHtml());
        Assert.DoesNotContain("later", tree.ToHtml());
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ChildArraysAndAttributesCannotMutateAnExistingComponent()
    {
        Node?[] children = ["original"];
        var original = Paragraph(children).WithClass("base");
        children[0] = "changed";
        var modified = original.WithClass("extra").WithAttribute("data-id", "A\" & B");

        Assert.Equal("<p class=\"base\">original</p>", original.ToHtml());
        Assert.Equal("<p class=\"base extra\" data-id=\"A&quot; &amp; B\">original</p>", modified.ToHtml());
        Assert.Throws<NotSupportedException>(() => ((IList<Node>)original.Children)[0] = Text("changed"));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)original.Attributes)["class"] = "changed");
    }

    [Fact]
    public void NullOmissionAndNumbersAreInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("<p>0 | 12.50 | 1.25</p>", Paragraph(null, 0, " | ", 12.50m, " | ", 1.25d, null).ToHtml());
            Assert.Throws<ArgumentOutOfRangeException>(() => Paragraph(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Paragraph(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => Paragraph(float.PositiveInfinity));
            Assert.Equal("<p>18446744073709551615|0.1|4294967295</p>", Paragraph(ulong.MaxValue, "|", 0.1f, "|", uint.MaxValue).ToHtml());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void NullParamsArraysOmitChildrenAndNullSequencesFailClearly()
    {
        Assert.Equal("<p></p>", Paragraph(null).ToHtml());
        Assert.Equal("", Fragment(null).ToHtml());
        Assert.Equal("<div class=\"ui-stack\"></div>", Stack(null).ToHtml());
        Assert.Contains("<main class=\"fb-document-root report-root\"></main>", new Document(null).ToHtml());
        Assert.Throws<ArgumentNullException>(() => Fragment((IEnumerable<Node?>)null!));
    }

    [Theory]
    [InlineData("p onclick")]
    [InlineData("div><script")]
    [InlineData("1div")]
    [InlineData("")]
    public void InvalidTagNamesAreRejected(string name) => Assert.ThrowsAny<ArgumentException>(() => Element.Create(name));

    [Fact]
    public void VoidElementsAndSemanticArgumentsFailClearly()
    {
        Assert.Throws<ArgumentException>(() => Element.Create("img", "lost child"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Heading(7, "Invalid"));
        Assert.Throws<ArgumentNullException>(() => Image("logo.png", null!));
        Assert.Throws<ArgumentException>(() => Paragraph("text").WithAttribute("title onclick", "injection"));
        Assert.Equal("<img src=\"logo.png\" alt=\"\">", Image("logo.png", "").ToHtml());
        Assert.Equal("<br><hr>", Fragment(LineBreak(), Divider()).ToHtml());
    }

    [Fact]
    public void BooleanAndAriaAttributesHaveDifferentSemantics()
    {
        var element = Paragraph("Hidden").WithAttribute("hidden", true).WithAttribute("aria-hidden", false);
        Assert.Contains("hidden=\"\"", element.ToHtml());
        Assert.Contains("aria-hidden=\"false\"", element.ToHtml());
        Assert.Equal("<p aria-hidden=\"false\">Hidden</p>", element.WithAttribute("hidden", false).ToHtml());
    }

    [Fact]
    public void SerializationHandlesDeepTreesWithoutRecursiveStackGrowth()
    {
        Node tree = "leaf";
        for (var i = 0; i < 20_000; i++) tree = Box(tree);
        Assert.Equal(220_004, tree.ToHtml().Length);
    }

    [Fact]
    public void StyleCompositionKeepsShorthandOrderAndDoesNotChangeSharedValues()
    {
        var shared = new Style { ["marginLeft"] = "12pt", ["margin"] = "0", ["--Brand"] = "#123456" };
        var overridden = shared.With("margin_left", "20pt");
        var element = Paragraph("Styled").WithStyle(shared).WithStyle("margin-left: 30pt; color: blue");

        Assert.Equal("margin-left: 12pt; margin: 0; --Brand: #123456;", shared.ToCss());
        Assert.Equal("margin: 0; --Brand: #123456; margin-left: 20pt;", overridden.ToCss());
        Assert.Equal("margin: 0; --Brand: #123456; margin-left: 30pt; color: blue;", element.InlineStyle.ToCss());
        Assert.Null(shared["color"]);
        Assert.Equal("#123456", shared["--Brand"]);
        Assert.Null(shared["--brand"]);
    }

    [Fact]
    public void StyleStringsPreserveQuotedSemicolonsCommentsFunctionsAndEscapes()
    {
        var style = Style.Parse("/* start */ content: 'A; B: C'; color: red /* note */; background: url(\"data:image/svg+xml;utf8,<svg/>\"); --tokens: { a: b; c: d }; font-family: 'A\\'B'");

        Assert.Equal("'A; B: C'", style["content"]);
        Assert.Equal("red", style["color"]);
        Assert.Equal("url(\"data:image/svg+xml;utf8,<svg/>\")", style["background"]);
        Assert.Equal("{ a: b; c: d }", style["--tokens"]);
        Assert.Equal("'A\\'B'", style["font-family"]);
        Assert.Contains("&lt;svg/&gt;", Paragraph("text").WithStyle(style).ToHtml());
    }

    [Theory]
    [InlineData("color")]
    [InlineData("color: 'red")]
    [InlineData("color: red /*")]
    [InlineData("color: rgb(1, 2, 3]")]
    [InlineData("color: red}")]
    [InlineData("color: red\\")]
    [InlineData("color: ")]
    public void MalformedDeclarationsFailBeforeRendering(string css) => Assert.ThrowsAny<ArgumentException>(() => Style.Parse(css));

    [Fact]
    public void DeclarationValuesCannotInjectAdditionalPropertiesOrRules()
    {
        Assert.Throws<ArgumentException>(() => new Style { ["color"] = "red; background: blue" });
        Assert.Throws<ArgumentException>(() => new Style { ["color"] = "red} body {color: blue}" });
        Assert.Throws<ArgumentException>(() => new Style { ["color; margin"] = "red" });
        Assert.Throws<ArgumentException>(() => new StyleSheet { [".x } body { color:red"] = new Style { ["color"] = "blue" } });
    }

    [Fact]
    public void StylesheetsComposeInAuthoredOrderAndOnlyAttachToDocuments()
    {
        var baseStyles = new StyleSheet { [".notice"] = new Style { ["color"] = "green" } };
        var styles = new StyleSheet(baseStyles) { [".notice.paid"] = new Style { ["color"] = "blue" } };
        var paragraph = Paragraph("Paid").WithClass("notice paid");
        var doc = new Document(paragraph) { Styles = styles, Title = "A & <B>", Language = "en-US", IncludeComponentStyles = false };

        Assert.Equal(".notice { color: green; }", baseStyles.ToCss());
        Assert.Equal(".notice { color: green; }\n.notice.paid { color: blue; }", doc.ToCss());
        Assert.Equal("<p class=\"notice paid\">Paid</p>", paragraph.ToHtml());
        Assert.DoesNotContain("color:", doc.ToHtml());
        Assert.Contains("<title>A &amp; &lt;B&gt;</title>", doc.ToHtml());
        Assert.Contains("<html lang=\"en-US\">", doc.ToHtml());
        Assert.Equal(doc.ToHtml(), doc.ToRenderJob().Html);
        Assert.Equal(doc.ToCss(), doc.ToRenderJob().Css);
    }

    [Fact]
    public void DefaultLayoutRulesPrecedeDocumentOverrides()
    {
        var doc = new Document(Stack("text")) { Styles = new StyleSheet { [".ui-stack"] = new Style { ["gap"] = "20pt" } } };
        Assert.StartsWith(".ui-stack { display: flex;", doc.ToCss());
        Assert.EndsWith(".ui-stack { gap: 20pt; }", doc.ToCss());
        Assert.Equal("<div class=\"ui-stack\">text</div>", Stack("text").ToHtml());
    }

    [Fact]
    public void QuotedSelectorPunctuationAndCustomPropertyNamesRemainValid()
    {
        var style = new Style { ["--1"] = "red", ["--Brand"] = "blue" };
        var sheet = new StyleSheet { ["[data-label=\"{a;b}\"]"] = style };
        Assert.Equal("[data-label=\"{a;b}\"] { --1: red; --Brand: blue; }", sheet.ToCss());
    }

    [Fact]
    public void ComponentsRenderThroughExistingEngineAndCompiledApis()
    {
        using var temporary = TemporaryDirectory.Create();
        using var engine = new FullBleedEngine();
        var doc = new Document(Heading(1, "Component {{id}}"), Paragraph("Literal <tag> & data"))
        {
            Styles = new StyleSheet
            {
                ["@page"] = new Style { ["size"] = "A5", ["margin"] = "15mm" },
                ["body"] = new Style { ["fontFamily"] = "Helvetica", ["color"] = "#315d9b" },
            },
        };
        var pdf = doc.RenderPdf(engine);
        var diagnostics = doc.RenderPdfWithDiagnostics(engine);
        var path = Path.Combine(temporary.Path, "components.pdf");
        var written = doc.RenderPdfToFile(engine, path);
        var previews = doc.RenderImagePagesToDirectory(engine, temporary.Path, dpi: 72, stem: "component");

        Assert.Equal(pdf, diagnostics.Pdf);
        Assert.Equal(pdf, File.ReadAllBytes(path));
        Assert.Equal(pdf.LongLength, written);
        Assert.Equal(1, FullBleedEngine.InspectPdf(path).PageCount);
        Assert.Empty(diagnostics.Diagnostics.MissingGlyphs);
        Assert.Single(previews.Paths);
        using var compiled = doc.Compile(engine);
        Assert.Contains("id", compiled.GetStats().BindingSlots);
        var recordsPath = Path.Combine(temporary.Path, "records.pdf");
        compiled.RenderBindingsToFile(new[] { "ONE", "TWO" }, map => map.Bind("id", value => value), recordsPath);
        Assert.Equal(2, FullBleedEngine.InspectPdf(recordsPath).PageCount);
    }
}
