using System.Security.Cryptography;

namespace FullBleed.DotNet.Tests;

public sealed class NativeIntegrationTests
{
    private const string Css = "body { font-family: Helvetica, sans-serif; }";

    [Fact]
    public void NativeFeaturesAdvertiseCompiledLanes()
    {
        var features = FullBleedEngine.GetNativeFeatures();

        Assert.Equal(FullBleedEngine.SupportedNativeAbiVersion, features.AbiVersion);
        Assert.True(features.CompiledDocument);
        Assert.True(features.CompiledFixedBindings);
        Assert.True(features.CompiledReflowBindings);
        Assert.Contains("throughput", features.CompiledFlowCompressionModes);
    }

    [Fact]
    public void OrdinaryRenderIsDeterministicAndInspectable()
    {
        using var temporary = TemporaryDirectory.Create();
        using var engine = new FullBleedEngine(new FullBleedEngineOptions
        {
            DocumentLanguage = "en-US",
            DocumentTitle = "Native integration",
        });
        const string html = "<h1>DOTNET-SMOKE-001</h1><p>Deterministic output.</p>";

        var first = engine.RenderPdf(html, Css);
        var second = engine.RenderPdf(html, Css);
        var path = Path.Combine(temporary.Path, "ordinary.pdf");
        var written = engine.RenderPdfToFile(html, Css, path);
        var inspection = FullBleedEngine.InspectPdf(path);

        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(first, 0, 8));
        Assert.Equal(SHA256.HashData(first), SHA256.HashData(second));
        Assert.Equal(first.Length, written);
        Assert.Equal(1, inspection.PageCount);
        Assert.False(inspection.Encrypted);
    }

    [Fact]
    public void DiagnosticsMetricsAndPreviewPagesAreTyped()
    {
        using var temporary = TemporaryDirectory.Create();
        using var engine = new FullBleedEngine();
        const string html = "<h1>Diagnostics</h1><p>Page one.</p>";

        var diagnostics = engine.RenderPdfWithDiagnostics(html, Css);
        var metrics = engine.RenderPdfWithMetrics(html, Css);
        var previews = engine.RenderImagePagesToDirectory(
            html,
            Css,
            Path.Combine(temporary.Path, "preview"),
            dpi: 72,
            stem: "diagnostic");

        Assert.NotEmpty(diagnostics.Pdf);
        Assert.Equal("fullbleed.dotnet.render_diagnostics.v1", diagnostics.Diagnostics.Schema);
        Assert.NotEmpty(metrics.Pdf);
        Assert.Single(metrics.Metrics.Pages);
        Assert.Single(previews.Paths);
        Assert.True(File.Exists(previews.Paths[0]));
        Assert.True(new FileInfo(previews.Paths[0]).Length > 8);
    }

    [Fact]
    public void ParallelBatchPreservesOrderedPageCount()
    {
        using var engine = new FullBleedEngine();
        var jobs = Enumerable.Range(1, 4)
            .Select(index => new RenderJob($"<h1>BATCH-{index:000}</h1>", Css));

        var result = engine.RenderBatch(jobs, new BatchRenderOptions { Parallel = true });
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "batch.pdf");
        File.WriteAllBytes(path, result.Pdf);
        var filePath = Path.Combine(temporary.Path, "batch-direct.pdf");
        var direct = engine.RenderBatchToFile(
            Enumerable.Range(1, 4).Select(index => new RenderJob($"<h1>BATCH-{index:000}</h1>", Css)),
            filePath,
            new BatchRenderOptions { Parallel = true });
        var inspection = FullBleedEngine.InspectPdf(path);

        Assert.True(result.Diagnostics.ParallelUsed);
        Assert.Equal(4, result.Diagnostics.JobCount);
        Assert.Equal(4, inspection.PageCount);
        Assert.True(direct.BytesWritten > 0);
        Assert.Equal(4, FullBleedEngine.InspectPdf(filePath).PageCount);
    }

    [Fact]
    public void CompiledCopiesAndFixedBindingsRenderDeterministically()
    {
        using var temporary = TemporaryDirectory.Create();
        using var engine = new FullBleedEngine();
        using var compiled = engine.Compile(
            "<p>Invoice: {{invoice_id}}</p><p>Customer: {{customer}}</p>",
            Css);
        var records = Enumerable.Range(1, 3)
            .Select(index => new Invoice($"INV-{index:0000}", $"Customer {index}"));

        var stats = compiled.GetStats();
        var bound = compiled.RenderBindings(
            records,
            map => map
                .Bind("invoice_id", invoice => invoice.Id)
                .Bind("customer", invoice => invoice.Customer));
        var repeated = compiled.Render(copies: 3);
        var boundPath = Path.Combine(temporary.Path, "bound.pdf");
        var repeatedPath = Path.Combine(temporary.Path, "repeated.pdf");
        File.WriteAllBytes(boundPath, bound);
        File.WriteAllBytes(repeatedPath, repeated);

        Assert.Equal(["customer", "invoice_id"], stats.BindingSlots);
        Assert.Equal(3, FullBleedEngine.InspectPdf(boundPath).PageCount);
        Assert.Equal(3, FullBleedEngine.InspectPdf(repeatedPath).PageCount);
    }

    [Fact]
    public void CompiledFixedBindingsSupportRegisteredFontsAndMatchOrdinaryPaint()
    {
        using var temporary = TemporaryDirectory.Create();
        var fontPath = FullBleedOfficialFontPath("NotoSans-Regular.ttf");
        using var engine = new FullBleedEngine(new FullBleedEngineOptions
        {
            PageSize = new PageSize(260f, 140f),
            Margins = PageMargins.All(12f),
            Assets = [FullBleedAsset.FromPath(fontPath, FullBleedAssetKind.Font, "VdpCustom")],
        });
        const string template = "<main><h1>{{attendee}}</h1><p>{{role}}</p><strong>{{seat}}</strong></main>";
        const string css = "body { margin: 0; font-family: VdpCustom; font-weight: 400; } h1 { font-size: 20pt; font-weight: 400; }";
        using var compiled = engine.Compile(template, css);
        var boundPath = Path.Combine(temporary.Path, "bound-custom-font.pdf");
        var ordinaryPath = Path.Combine(temporary.Path, "ordinary-custom-font.pdf");

        compiled.RenderBindingsToFile(
            new[] { new Badge("ADA RIVERA", "RESEARCHER", "A12") },
            map => map
                .Bind("attendee", badge => badge.Attendee)
                .Bind("role", badge => badge.Role)
                .Bind("seat", badge => badge.Seat),
            boundPath);
        engine.RenderPdfToFile(
            template
                .Replace("{{attendee}}", "ADA RIVERA", StringComparison.Ordinal)
                .Replace("{{role}}", "RESEARCHER", StringComparison.Ordinal)
                .Replace("{{seat}}", "A12", StringComparison.Ordinal),
            css,
            ordinaryPath);

        var boundPreview = engine.RenderFinalizedPdfImagePagesToDirectory(
            boundPath,
            Path.Combine(temporary.Path, "bound-preview"),
            dpi: 120,
            stem: "bound");
        var ordinaryPreview = engine.RenderFinalizedPdfImagePagesToDirectory(
            ordinaryPath,
            Path.Combine(temporary.Path, "ordinary-preview"),
            dpi: 120,
            stem: "ordinary");

        Assert.Equal(1, FullBleedEngine.InspectPdf(boundPath).PageCount);
        Assert.Single(boundPreview.Paths);
        Assert.Single(ordinaryPreview.Paths);
        Assert.Equal(
            SHA256.HashData(File.ReadAllBytes(ordinaryPreview.Paths[0])),
            SHA256.HashData(File.ReadAllBytes(boundPreview.Paths[0])));
    }

    [Fact]
    public void CompiledReflowSupportsLinqSelectorsAndVariableContent()
    {
        using var temporary = TemporaryDirectory.Create();
        using var engine = new FullBleedEngine();
        using var compiled = engine.Compile(
            "<article><h1>{{title}}</h1><div>{{narrative}}</div></article>",
            Css);
        var records = new[]
        {
            new Narrative("Short", "A short paragraph."),
            new Narrative("Long", string.Join(" ", Enumerable.Repeat("A longer paragraph.", 200))),
        };
        var path = Path.Combine(temporary.Path, "reflow.pdf");

        compiled.RenderReflowBindingsToFile(
            records,
            map => map
                .Bind("title", record => record.Title)
                .Bind("narrative", record => record.Text),
            path,
            CompiledFlowCompression.Compact);

        Assert.True(compiled.GetStats().ReflowProgramReady);
        Assert.True(FullBleedEngine.InspectPdf(path).PageCount >= 2);
    }

    [Fact]
    public void TemplateStampFinalizationProducesInspectablePdf()
    {
        using var temporary = TemporaryDirectory.Create();
        using var engine = new FullBleedEngine();
        var template = Path.Combine(temporary.Path, "template.pdf");
        var overlay = Path.Combine(temporary.Path, "overlay.pdf");
        var output = Path.Combine(temporary.Path, "stamped.pdf");
        var composed = Path.Combine(temporary.Path, "composed.pdf");
        engine.RenderPdfToFile("<p>TEMPLATE-MARKER</p>", Css, template);
        engine.RenderPdfToFile("<p>OVERLAY-MARKER</p>", Css, overlay);

        var result = FullBleedEngine.StampPdf(template, overlay, output);
        var composeResult = FullBleedEngine.ComposePdf(
            overlay,
            composed,
            new ComposeOptions
            {
                Templates = [new ComposeTemplate("letter", template)],
                Plan = [new ComposePage("letter", 0, 0)],
            });
        var previews = engine.RenderFinalizedPdfImagePagesToDirectory(
            composed,
            Path.Combine(temporary.Path, "composed-preview"),
            dpi: 72);

        Assert.Equal(1, result.PagesWritten);
        Assert.Equal(1, FullBleedEngine.InspectPdf(output).PageCount);
        Assert.Equal(1, composeResult.PagesWritten);
        Assert.Equal(1, FullBleedEngine.InspectPdf(composed).PageCount);
        Assert.Single(previews.Paths);
    }

    [Fact]
    public void InvalidConfigurationReturnsTypedExceptionWithoutPoisoningLaterCalls()
    {
        var configurationError = Assert.Throws<FullBleedException>(() =>
            new FullBleedEngine(new FullBleedEngineOptions
            {
                Margins = new PageMargins(-1f, 0f, 0f, 0f),
            }));
        Assert.Equal(FullBleedStatusCode.InvalidOptions, configurationError.StatusCode);

        using var engine = new FullBleedEngine();
        using var compiled = engine.Compile("<p>{{required}}</p>", Css);
        var bindingError = Assert.Throws<FullBleedException>(() =>
            compiled.RenderBindings(new Dictionary<string, IReadOnlyList<string>>
            {
                ["unknown"] = ["value"],
            }));
        var successful = engine.RenderPdf("<p>RECOVERY-SMOKE</p>", Css);

        Assert.Equal(FullBleedStatusCode.InvalidOptions, bindingError.StatusCode);
        Assert.NotEmpty(successful);
    }

    [Fact]
    public void EngineAndCompiledDocumentSupportConcurrentCalls()
    {
        using var engine = new FullBleedEngine();
        using var compiled = engine.Compile("<p>CONCURRENT-COMPILED</p>", Css);

        var ordinary = Enumerable.Range(0, 8)
            .AsParallel()
            .Select(index => engine.RenderPdf($"<p>CONCURRENT-{index:00}</p>", Css))
            .ToArray();
        var compiledCopies = Enumerable.Range(0, 8)
            .AsParallel()
            .Select(_ => compiled.Render())
            .ToArray();

        Assert.All(ordinary, Assert.NotEmpty);
        var expectedHash = SHA256.HashData(compiledCopies[0]);
        Assert.All(compiledCopies, pdf => Assert.Equal(expectedHash, SHA256.HashData(pdf)));
    }

    [Fact]
    public void AssetsTaggedConfigurationAndCompatibilityFacadeAreWired()
    {
        using var temporary = TemporaryDirectory.Create();
        var assetCss = System.Text.Encoding.UTF8.GetBytes("p { color: #d00000; }");
        using var configured = new FullBleedEngine(new FullBleedEngineOptions
        {
            PdfProfile = PdfProfile.Tagged,
            DocumentLanguage = "en-US",
            DocumentTitle = "Tagged configuration smoke",
            Assets = [FullBleedAsset.FromBytes(assetCss, FullBleedAssetKind.Css, "theme.css")],
        });
        using var plain = new FullBleedEngine();
        var path = Path.Combine(temporary.Path, "tagged.pdf");
        configured.RenderPdfToFile("<main><p>TAGGED-ASSET-001</p></main>", "", path);
        var configuredBytes = File.ReadAllBytes(path);
        var plainBytes = plain.RenderPdf("<main><p>TAGGED-ASSET-001</p></main>", "");
        var compatibilityBytes = FullBleedRenderer.Render("<p>COMPATIBILITY-001</p>");
        var inspection = FullBleedEngine.InspectPdf(path);

        Assert.False(SHA256.HashData(plainBytes).SequenceEqual(SHA256.HashData(configuredBytes)));
        Assert.NotEmpty(compatibilityBytes);
        Assert.True(inspection.Profile.StructTreeRootPresent);
        Assert.True(inspection.Profile.MarkInfoPresent);
        Assert.True(inspection.Profile.LangPresent);
    }

    private sealed record Invoice(string Id, string Customer);

    private sealed record Narrative(string Title, string Text);

    private sealed record Badge(string Attendee, string Role, string Seat);

    private static string FullBleedOfficialFontPath(string fileName)
    {
        var repository = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.GetFullPath(
            Path.Combine(
                repository,
                "..",
                "fullbleed-official",
                "python",
                "fullbleed_assets",
                "fonts",
                fileName));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The Fullbleed source repository must be checked out beside fullbleed-dotnet.",
                path);
        }

        return path;
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    private TemporaryDirectory(string path)
    {
        Path = path;
    }

    internal string Path { get; }

    internal static TemporaryDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "fullbleed-dotnet-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TemporaryDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
