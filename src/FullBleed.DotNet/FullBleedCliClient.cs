using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace FullBleed.DotNet;

public sealed record FullBleedCliOptions
{
    public string ExecutablePath { get; init; } = "fullbleed";

    public string? WorkingDirectory { get; init; }

    public Dictionary<string, string?> Environment { get; init; } = new(StringComparer.Ordinal);
}

public sealed class FullBleedCliException : Exception
{
    public FullBleedCliException(string message, int? exitCode = null, string? standardError = null)
        : base(message)
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }

    public int? ExitCode { get; }

    public string? StandardError { get; }
}

public sealed record FullBleedCliResult
{
    public int ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    public JsonElement? Payload { get; init; }

    public bool ExitedSuccessfully => ExitCode == 0;

    public bool ReportedSuccess =>
        Payload is not { } payload
        || !payload.TryGetProperty("ok", out var ok)
        || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
        || ok.GetBoolean();

    public FullBleedCliResult EnsureSuccess()
    {
        if (!ExitedSuccessfully || !ReportedSuccess)
        {
            var message = Payload is { } payload
                && payload.TryGetProperty("message", out var detail)
                && detail.ValueKind == JsonValueKind.String
                    ? detail.GetString()
                    : null;
            throw new FullBleedCliException(
                message ?? $"Fullbleed CLI exited with code {ExitCode}.",
                ExitCode,
                StandardError);
        }

        return this;
    }

    public JsonElement RequirePayload()
    {
        EnsureSuccess();
        return Payload ?? throw new FullBleedCliException(
            "Fullbleed CLI produced no JSON payload.",
            ExitCode,
            StandardError);
    }
}

public sealed class FullBleedCliClient
{
    private readonly FullBleedCliOptions _options;

    public FullBleedCliClient(FullBleedCliOptions? options = null)
    {
        _options = options ?? new FullBleedCliOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ExecutablePath);
    }

    public async Task<JsonElement> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunJsonAsync(["capabilities", "--json"], cancellationToken)
            .ConfigureAwait(false);
        return result.RequirePayload();
    }

    public async Task<JsonElement> GetAgentContractAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunJsonAsync(
                ["agent-contract", "--format", "json"],
                cancellationToken)
            .ConfigureAwait(false);
        return result.RequirePayload();
    }

    public async Task<JsonElement> GetSchemaAsync(
        IEnumerable<string> commandPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandPath);
        var arguments = new List<string> { "--schema" };
        arguments.AddRange(commandPath);
        if (arguments.Count == 1)
        {
            throw new ArgumentException("At least one command token is required.", nameof(commandPath));
        }

        var result = await RunJsonAsync(arguments, cancellationToken).ConfigureAwait(false);
        return result.RequirePayload();
    }

    public Task<FullBleedCliResult> RenderAsync(
        FullBleedCliRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        var arguments = new List<string> { "--json-only", "render" };
        FullBleedCliArguments.AddDocumentArguments(arguments, request);
        arguments.Add("--out");
        arguments.Add(request.OutputPath);
        arguments.AddRange(request.AdditionalArguments);
        return RunJsonAsync(arguments, cancellationToken);
    }

    public Task<FullBleedCliResult> VerifyAsync(
        FullBleedCliVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var arguments = new List<string> { "--json-only", "verify" };
        FullBleedCliArguments.AddDocumentArguments(arguments, request);
        FullBleedCliArguments.AddValue(arguments, "--emit-pdf", request.EmitPdfPath);
        arguments.AddRange(request.AdditionalArguments);
        return RunJsonAsync(arguments, cancellationToken);
    }

    public Task<FullBleedCliResult> InspectPdfAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return RunJsonAsync(["--json-only", "inspect", "pdf", path], cancellationToken);
    }

    public async Task<FullBleedCliResult> RunJsonAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(arguments, cancellationToken).ConfigureAwait(false);
        return result with { Payload = TryParseJson(result.StandardOutput) };
    }

    public async Task<FullBleedCliResult> RunAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            WorkingDirectory = _options.WorkingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in _options.Environment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new FullBleedCliException($"Could not start {_options.ExecutablePath}.");
            }
        }
        catch (Win32Exception error)
        {
            throw new FullBleedCliException(
                $"Could not start Fullbleed CLI at '{_options.ExecutablePath}': {error.Message}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between cancellation and Kill.
            }

            throw;
        }

        return new FullBleedCliResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = await stdoutTask.ConfigureAwait(false),
            StandardError = await stderrTask.ConfigureAwait(false),
        };
    }

    private static JsonElement? TryParseJson(string output)
    {
        var trimmed = output.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (TryParse(trimmed, out var payload))
        {
            return payload;
        }

        foreach (var line in Enumerable.Reverse(
                     trimmed.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)))
        {
            if (TryParse(line, out payload))
            {
                return payload;
            }
        }

        return null;
    }

    private static bool TryParse(string json, out JsonElement payload)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            payload = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            payload = default;
            return false;
        }
    }
}

internal static class FullBleedCliArguments
{
    internal static void AddDocumentArguments(
        List<string> arguments,
        FullBleedCliDocumentRequest request)
    {
        ValidateInputs(request);
        AddValue(arguments, "--html", request.HtmlPath);
        AddValue(arguments, "--html-str", request.Html);
        AddValue(arguments, "--css", request.CssPath);
        AddValue(arguments, "--css-str", request.Css);
        AddValue(arguments, "--page-size", request.PageSize);
        AddInvariant(arguments, "--page-width", request.PageWidthPt);
        AddInvariant(arguments, "--page-height", request.PageHeightPt);
        AddInvariant(arguments, "--margin", request.MarginPt);
        AddValue(arguments, "--page-margins", request.PageMargins);
        AddToggle(arguments, request.ReuseXobjects, "--reuse-xobjects", "--no-reuse-xobjects");
        AddToggle(arguments, request.SvgFormXobjects, "--svg-form-xobjects", "--no-svg-form-xobjects");
        AddToggle(arguments, request.SvgRasterFallback, "--svg-raster-fallback", "--no-svg-raster-fallback");
        AddToggle(arguments, request.UnicodeSupport, "--unicode-support", "--no-unicode-support");
        AddToggle(arguments, request.ShapeText, "--shape-text", "--no-shape-text");
        AddToggle(arguments, request.UnicodeMetrics, "--unicode-metrics", "--no-unicode-metrics");
        AddValue(arguments, "--pdf-version", request.PdfVersion);
        AddValue(arguments, "--pdf-profile", request.PdfProfile);
        AddValue(arguments, "--output-intent-icc", request.OutputIntentIccPath);
        AddValue(arguments, "--output-intent-identifier", request.OutputIntentIdentifier);
        AddValue(arguments, "--output-intent-info", request.OutputIntentInfo);
        AddInvariant(arguments, "--output-intent-components", request.OutputIntentComponents);
        AddValue(arguments, "--color-space", request.ColorSpace);
        AddValue(arguments, "--document-lang", request.DocumentLanguage);
        AddValue(arguments, "--document-title", request.DocumentTitle);
        AddValue(arguments, "--header-each", request.HeaderEach);
        AddValue(arguments, "--header-html-each", request.HeaderHtmlEach);
        AddValue(arguments, "--footer-each", request.FooterEach);
        AddValue(arguments, "--footer-html-each", request.FooterHtmlEach);
        AddValue(arguments, "--watermark-text", request.WatermarkText);
        AddValue(arguments, "--watermark-html", request.WatermarkHtml);
        AddValue(arguments, "--watermark-image", request.WatermarkImage);
        AddValue(arguments, "--watermark-layer", request.WatermarkLayer);
        AddValue(arguments, "--watermark-semantics", request.WatermarkSemantics);
        AddInvariant(arguments, "--watermark-opacity", request.WatermarkOpacity);
        AddInvariant(arguments, "--watermark-rotation", request.WatermarkRotation);
        AddValue(arguments, "--jit-mode", request.JitMode);
        AddValue(arguments, "--emit-jit", request.EmitJitPath);
        AddValue(arguments, "--emit-perf", request.EmitPerfPath);
        AddValue(arguments, "--emit-glyph-report", request.EmitGlyphReportPath);
        AddValue(arguments, "--emit-page-data", request.EmitPageDataPath);
        AddValue(arguments, "--emit-compose-plan", request.EmitComposePlanPath);
        AddValue(arguments, "--emit-image", request.EmitImageDirectory);
        AddInvariant(arguments, "--image-dpi", request.ImageDpi);
        AddValue(arguments, "--emit-manifest", request.EmitManifestPath);
        AddValue(arguments, "--template-binding", request.TemplateBinding);
        AddValue(arguments, "--templates", request.Templates);
        AddInvariant(arguments, "--template-dx", request.TemplateDx);
        AddInvariant(arguments, "--template-dy", request.TemplateDy);
        AddValue(arguments, "--compose-annotation-mode", request.ComposeAnnotationMode);
        foreach (var asset in request.Assets)
        {
            arguments.Add("--asset");
            arguments.Add(asset);
        }

        AddValue(arguments, "--asset-kind", request.AssetKind);
        AddValue(arguments, "--asset-name", request.AssetName);
        AddFlag(arguments, request.AssetTrusted, "--asset-trusted");
        AddFlag(arguments, request.AllowRemoteAssets, "--allow-remote-assets");
        AddFlag(arguments, request.VerboseAssets, "--verbose-assets");
        AddValue(arguments, "--profile", request.Profile);
        AddFlag(arguments, request.AllowFallbacks, "--allow-fallbacks");
        foreach (var failure in request.FailOn)
        {
            arguments.Add("--fail-on");
            arguments.Add(failure);
        }

        AddValue(arguments, "--deterministic-hash", request.DeterministicHashPath);
        AddInvariant(arguments, "--budget-max-pages", request.BudgetMaxPages);
        AddInvariant(arguments, "--budget-max-bytes", request.BudgetMaxBytes);
        AddInvariant(arguments, "--budget-max-ms", request.BudgetMaxMs);
        AddValue(arguments, "--repro-record", request.ReproRecordPath);
        AddValue(arguments, "--repro-check", request.ReproCheckPath);
    }

    internal static void AddValue(List<string> arguments, string flag, string? value)
    {
        if (value is null)
        {
            return;
        }

        arguments.Add(flag);
        arguments.Add(value);
    }

    private static void AddInvariant<T>(List<string> arguments, string flag, T? value)
        where T : struct, IFormattable
    {
        if (value is null)
        {
            return;
        }

        arguments.Add(flag);
        arguments.Add(value.Value.ToString(null, CultureInfo.InvariantCulture));
    }

    private static void AddToggle(
        List<string> arguments,
        bool? value,
        string enabledFlag,
        string disabledFlag)
    {
        if (value is not null)
        {
            arguments.Add(value.Value ? enabledFlag : disabledFlag);
        }
    }

    private static void AddFlag(List<string> arguments, bool value, string flag)
    {
        if (value)
        {
            arguments.Add(flag);
        }
    }

    private static void ValidateInputs(FullBleedCliDocumentRequest request)
    {
        if (request.Html is not null && request.HtmlPath is not null)
        {
            throw new ArgumentException("Set Html or HtmlPath, not both.");
        }

        if (request.Css is not null && request.CssPath is not null)
        {
            throw new ArgumentException("Set Css or CssPath, not both.");
        }
    }
}
