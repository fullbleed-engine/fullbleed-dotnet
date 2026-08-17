namespace FullBleed.DotNet;

public sealed class FullBleedEngine : IDisposable
{
    public const uint SupportedNativeAbiVersion = 1;

    private readonly SafeEngineHandle _handle;
    private bool _disposed;

    public FullBleedEngine(FullBleedEngineOptions? options = null)
    {
        var actualAbi = NativeMethods.AbiVersion();
        if (actualAbi != SupportedNativeAbiVersion)
        {
            throw new FullBleedException(
                FullBleedStatusCode.InvalidOptions,
                $"Native ABI {actualAbi} is incompatible with managed ABI {SupportedNativeAbiVersion}.");
        }

        var json = NativeResult.Serialize(
            options ?? new FullBleedEngineOptions(),
            FullBleedJsonContext.Default.FullBleedEngineOptions);
        IntPtr nativeHandle = IntPtr.Zero;
        IntPtr error = IntPtr.Zero;

        try
        {
            unsafe
            {
                fixed (byte* jsonPointer = json)
                {
                    var status = NativeMethods.EngineCreate(
                        jsonPointer,
                        (nuint)json.Length,
                        out nativeHandle,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                }
            }
        }
        finally
        {
            if (error != IntPtr.Zero)
            {
                NativeMethods.FreeString(error);
            }
        }

        if (nativeHandle == IntPtr.Zero)
        {
            throw new FullBleedException(
                FullBleedStatusCode.InvalidHandle,
                "The native bridge returned a null engine handle.");
        }

        _handle = new SafeEngineHandle(nativeHandle);
    }

    public static NativeFeatures GetNativeFeatures()
    {
        NativeByteBuffer json = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            var status = NativeMethods.BuildFeatures(out json, out error);
            NativeResult.EnsureSuccess(status, error);
            error = IntPtr.Zero;
            return NativeResult.ReadJsonAndFree(
                ref json,
                FullBleedJsonContext.Default.NativeFeatures);
        }
        finally
        {
            if (json.Ptr != IntPtr.Zero)
            {
                NativeResult.CopyAndFree(ref json);
            }

            if (error != IntPtr.Zero)
            {
                NativeMethods.FreeString(error);
            }
        }
    }

    public byte[] RenderPdf(string html, string css = "")
    {
        ThrowIfDisposed();
        var htmlBytes = NativeResult.Utf8(html);
        var cssBytes = NativeResult.Utf8(css ?? string.Empty);
        NativeByteBuffer pdf = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            unsafe
            {
                fixed (byte* htmlPointer = htmlBytes)
                fixed (byte* cssPointer = cssBytes)
                {
                    var status = NativeMethods.EngineRenderPdf(
                        _handle,
                        htmlPointer,
                        (nuint)htmlBytes.Length,
                        cssPointer,
                        (nuint)cssBytes.Length,
                        out pdf,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                    error = IntPtr.Zero;
                }
            }

            return NativeResult.CopyAndFree(ref pdf);
        }
        finally
        {
            FreeNativeOutputs(ref pdf, ref error);
        }
    }

    public long RenderPdfToFile(string html, string css, string outputPath)
    {
        ThrowIfDisposed();
        NativeResult.EnsureOutputParent(outputPath);
        var htmlBytes = NativeResult.Utf8(html);
        var cssBytes = NativeResult.Utf8(css ?? string.Empty);
        var pathBytes = NativeResult.Utf8(outputPath);
        IntPtr error = IntPtr.Zero;
        try
        {
            nuint bytesWritten;
            unsafe
            {
                fixed (byte* htmlPointer = htmlBytes)
                fixed (byte* cssPointer = cssBytes)
                fixed (byte* pathPointer = pathBytes)
                {
                    var status = NativeMethods.EngineRenderPdfToFile(
                        _handle,
                        htmlPointer,
                        (nuint)htmlBytes.Length,
                        cssPointer,
                        (nuint)cssBytes.Length,
                        pathPointer,
                        (nuint)pathBytes.Length,
                        out bytesWritten,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                    error = IntPtr.Zero;
                }
            }

            return checked((long)bytesWritten);
        }
        finally
        {
            if (error != IntPtr.Zero)
            {
                NativeMethods.FreeString(error);
            }
        }
    }

    public RenderDiagnosticsResult RenderPdfWithDiagnostics(string html, string css = "")
    {
        ThrowIfDisposed();
        var htmlBytes = NativeResult.Utf8(html);
        var cssBytes = NativeResult.Utf8(css ?? string.Empty);
        NativeByteBuffer pdf = default;
        NativeByteBuffer json = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            unsafe
            {
                fixed (byte* htmlPointer = htmlBytes)
                fixed (byte* cssPointer = cssBytes)
                {
                    var status = NativeMethods.EngineRenderWithDiagnostics(
                        _handle,
                        htmlPointer,
                        (nuint)htmlBytes.Length,
                        cssPointer,
                        (nuint)cssBytes.Length,
                        out pdf,
                        out json,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                    error = IntPtr.Zero;
                }
            }

            var pdfBytes = NativeResult.CopyAndFree(ref pdf);
            var diagnostics = NativeResult.ReadJsonAndFree(
                ref json,
                FullBleedJsonContext.Default.RenderDiagnostics);
            return new RenderDiagnosticsResult(pdfBytes, diagnostics);
        }
        finally
        {
            FreeNativeOutputs(ref pdf, ref json, ref error);
        }
    }

    public RenderMetricsResult RenderPdfWithMetrics(string html, string css = "")
    {
        ThrowIfDisposed();
        var htmlBytes = NativeResult.Utf8(html);
        var cssBytes = NativeResult.Utf8(css ?? string.Empty);
        NativeByteBuffer pdf = default;
        NativeByteBuffer json = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            unsafe
            {
                fixed (byte* htmlPointer = htmlBytes)
                fixed (byte* cssPointer = cssBytes)
                {
                    var status = NativeMethods.EngineRenderWithMetrics(
                        _handle,
                        htmlPointer,
                        (nuint)htmlBytes.Length,
                        cssPointer,
                        (nuint)cssBytes.Length,
                        out pdf,
                        out json,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                    error = IntPtr.Zero;
                }
            }

            var pdfBytes = NativeResult.CopyAndFree(ref pdf);
            var metrics = NativeResult.ReadJsonAndFree(
                ref json,
                FullBleedJsonContext.Default.RenderMetrics);
            return new RenderMetricsResult(pdfBytes, metrics);
        }
        finally
        {
            FreeNativeOutputs(ref pdf, ref json, ref error);
        }
    }

    public ImagePagesResult RenderImagePagesToDirectory(
        string html,
        string css,
        string outputDirectory,
        uint dpi = 150,
        string stem = "render")
    {
        ThrowIfDisposed();
        ValidateDpi(dpi);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var htmlBytes = NativeResult.Utf8(html);
        var cssBytes = NativeResult.Utf8(css ?? string.Empty);
        var directoryBytes = NativeResult.Utf8(outputDirectory);
        var stemBytes = NativeResult.Utf8(stem ?? string.Empty);
        NativeByteBuffer json = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            unsafe
            {
                fixed (byte* htmlPointer = htmlBytes)
                fixed (byte* cssPointer = cssBytes)
                fixed (byte* directoryPointer = directoryBytes)
                fixed (byte* stemPointer = stemBytes)
                {
                    var status = NativeMethods.EngineRenderImagePagesToDirectory(
                        _handle,
                        htmlPointer,
                        (nuint)htmlBytes.Length,
                        cssPointer,
                        (nuint)cssBytes.Length,
                        directoryPointer,
                        (nuint)directoryBytes.Length,
                        stemPointer,
                        (nuint)stemBytes.Length,
                        dpi,
                        out json,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                    error = IntPtr.Zero;
                }
            }

            return NativeResult.ReadJsonAndFree(
                ref json,
                FullBleedJsonContext.Default.ImagePagesResult);
        }
        finally
        {
            FreeNativeOutputs(ref json, ref error);
        }
    }

    public ImagePagesResult RenderFinalizedPdfImagePagesToDirectory(
        string pdfPath,
        string outputDirectory,
        uint dpi = 150,
        string stem = "render")
    {
        ThrowIfDisposed();
        ValidateDpi(dpi);
        var pdfPathBytes = NativeResult.Utf8(pdfPath);
        var directoryBytes = NativeResult.Utf8(outputDirectory);
        var stemBytes = NativeResult.Utf8(stem ?? string.Empty);
        NativeByteBuffer json = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            unsafe
            {
                fixed (byte* pdfPathPointer = pdfPathBytes)
                fixed (byte* directoryPointer = directoryBytes)
                fixed (byte* stemPointer = stemBytes)
                {
                    var status = NativeMethods.EngineRenderFinalizedPdfImagePagesToDirectory(
                        _handle,
                        pdfPathPointer,
                        (nuint)pdfPathBytes.Length,
                        directoryPointer,
                        (nuint)directoryBytes.Length,
                        stemPointer,
                        (nuint)stemBytes.Length,
                        dpi,
                        out json,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                    error = IntPtr.Zero;
                }
            }

            return NativeResult.ReadJsonAndFree(
                ref json,
                FullBleedJsonContext.Default.ImagePagesResult);
        }
        finally
        {
            FreeNativeOutputs(ref json, ref error);
        }
    }

    public BatchRenderResult RenderBatch(
        IEnumerable<RenderJob> jobs,
        BatchRenderOptions? options = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(jobs);
        options ??= new BatchRenderOptions();
        var request = new NativeBatchRequest(jobs.ToList(), options.Parallel, options.IncludePageData);
        var requestJson = NativeResult.Serialize(
            request,
            FullBleedJsonContext.Default.NativeBatchRequest);
        NativeByteBuffer pdf = default;
        NativeByteBuffer json = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            unsafe
            {
                fixed (byte* requestPointer = requestJson)
                {
                    var status = NativeMethods.EngineRenderBatch(
                        _handle,
                        requestPointer,
                        (nuint)requestJson.Length,
                        out pdf,
                        out json,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                    error = IntPtr.Zero;
                }
            }

            return new BatchRenderResult
            {
                Pdf = NativeResult.CopyAndFree(ref pdf),
                Diagnostics = NativeResult.ReadJsonAndFree(
                    ref json,
                    FullBleedJsonContext.Default.BatchRenderDiagnostics),
            };
        }
        finally
        {
            FreeNativeOutputs(ref pdf, ref json, ref error);
        }
    }

    public BatchFileRenderResult RenderBatchToFile(
        IEnumerable<RenderJob> jobs,
        string outputPath,
        BatchRenderOptions? options = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(jobs);
        NativeResult.EnsureOutputParent(outputPath);
        options ??= new BatchRenderOptions();
        var request = new NativeBatchRequest(jobs.ToList(), options.Parallel, options.IncludePageData);
        var requestJson = NativeResult.Serialize(
            request,
            FullBleedJsonContext.Default.NativeBatchRequest);
        var pathBytes = NativeResult.Utf8(outputPath);
        NativeByteBuffer json = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            nuint bytesWritten;
            unsafe
            {
                fixed (byte* requestPointer = requestJson)
                fixed (byte* pathPointer = pathBytes)
                {
                    var status = NativeMethods.EngineRenderBatchToFile(
                        _handle,
                        requestPointer,
                        (nuint)requestJson.Length,
                        pathPointer,
                        (nuint)pathBytes.Length,
                        out bytesWritten,
                        out json,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                    error = IntPtr.Zero;
                }
            }

            return new BatchFileRenderResult
            {
                BytesWritten = checked((long)bytesWritten),
                Diagnostics = NativeResult.ReadJsonAndFree(
                    ref json,
                    FullBleedJsonContext.Default.BatchRenderDiagnostics),
            };
        }
        finally
        {
            FreeNativeOutputs(ref json, ref error);
        }
    }

    public FullBleedCompiledDocument Compile(string html, string css = "")
    {
        ThrowIfDisposed();
        var htmlBytes = NativeResult.Utf8(html);
        var cssBytes = NativeResult.Utf8(css ?? string.Empty);
        IntPtr compiled = IntPtr.Zero;
        IntPtr error = IntPtr.Zero;
        try
        {
            unsafe
            {
                fixed (byte* htmlPointer = htmlBytes)
                fixed (byte* cssPointer = cssBytes)
                {
                    var status = NativeMethods.EngineCompile(
                        _handle,
                        htmlPointer,
                        (nuint)htmlBytes.Length,
                        cssPointer,
                        (nuint)cssBytes.Length,
                        out compiled,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                    error = IntPtr.Zero;
                }
            }

            if (compiled == IntPtr.Zero)
            {
                throw new FullBleedException(
                    FullBleedStatusCode.InvalidHandle,
                    "The native bridge returned a null compiled-document handle.");
            }

            return new FullBleedCompiledDocument(new SafeCompiledHandle(compiled));
        }
        catch
        {
            if (compiled != IntPtr.Zero)
            {
                NativeMethods.CompiledFree(compiled);
            }

            throw;
        }
        finally
        {
            if (error != IntPtr.Zero)
            {
                NativeMethods.FreeString(error);
            }
        }
    }

    public static PdfInspection InspectPdf(string path)
    {
        var pathBytes = NativeResult.Utf8(path);
        NativeByteBuffer json = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            unsafe
            {
                fixed (byte* pathPointer = pathBytes)
                {
                    var status = NativeMethods.InspectPdf(
                        pathPointer,
                        (nuint)pathBytes.Length,
                        out json,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                    error = IntPtr.Zero;
                }
            }

            return NativeResult.ReadJsonAndFree(
                ref json,
                FullBleedJsonContext.Default.PdfInspection);
        }
        finally
        {
            FreeNativeOutputs(ref json, ref error);
        }
    }

    public static FinalizeResult StampPdf(
        string templatePath,
        string overlayPath,
        string outputPath,
        StampOptions? options = null)
    {
        NativeResult.EnsureOutputParent(outputPath);
        var template = NativeResult.Utf8(templatePath);
        var overlay = NativeResult.Utf8(overlayPath);
        var output = NativeResult.Utf8(outputPath);
        var request = NativeResult.Serialize(
            options ?? new StampOptions(),
            FullBleedJsonContext.Default.StampOptions);
        NativeByteBuffer json = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            unsafe
            {
                fixed (byte* templatePointer = template)
                fixed (byte* overlayPointer = overlay)
                fixed (byte* outputPointer = output)
                fixed (byte* requestPointer = request)
                {
                    var status = NativeMethods.FinalizeStamp(
                        templatePointer,
                        (nuint)template.Length,
                        overlayPointer,
                        (nuint)overlay.Length,
                        outputPointer,
                        (nuint)output.Length,
                        requestPointer,
                        (nuint)request.Length,
                        out json,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                    error = IntPtr.Zero;
                }
            }

            return NativeResult.ReadJsonAndFree(
                ref json,
                FullBleedJsonContext.Default.FinalizeResult);
        }
        finally
        {
            FreeNativeOutputs(ref json, ref error);
        }
    }

    public static FinalizeResult ComposePdf(
        string overlayPath,
        string outputPath,
        ComposeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        NativeResult.EnsureOutputParent(outputPath);
        var overlay = NativeResult.Utf8(overlayPath);
        var output = NativeResult.Utf8(outputPath);
        var request = NativeResult.Serialize(options, FullBleedJsonContext.Default.ComposeOptions);
        NativeByteBuffer json = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            unsafe
            {
                fixed (byte* overlayPointer = overlay)
                fixed (byte* outputPointer = output)
                fixed (byte* requestPointer = request)
                {
                    var status = NativeMethods.FinalizeCompose(
                        overlayPointer,
                        (nuint)overlay.Length,
                        outputPointer,
                        (nuint)output.Length,
                        requestPointer,
                        (nuint)request.Length,
                        out json,
                        out error);
                    NativeResult.EnsureSuccess(status, error);
                    error = IntPtr.Zero;
                }
            }

            return NativeResult.ReadJsonAndFree(
                ref json,
                FullBleedJsonContext.Default.FinalizeResult);
        }
        finally
        {
            FreeNativeOutputs(ref json, ref error);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _handle.Dispose();
        _disposed = true;
    }

    private static void ValidateDpi(uint dpi)
    {
        if (dpi is < 36 or > 1200)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI must be between 36 and 1200.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FullBleedEngine));
        }
    }

    private static void FreeNativeOutputs(ref NativeByteBuffer buffer, ref IntPtr error)
    {
        if (buffer.Ptr != IntPtr.Zero)
        {
            NativeResult.CopyAndFree(ref buffer);
        }

        if (error != IntPtr.Zero)
        {
            NativeMethods.FreeString(error);
            error = IntPtr.Zero;
        }
    }

    private static void FreeNativeOutputs(
        ref NativeByteBuffer first,
        ref NativeByteBuffer second,
        ref IntPtr error)
    {
        if (first.Ptr != IntPtr.Zero)
        {
            NativeResult.CopyAndFree(ref first);
        }

        if (second.Ptr != IntPtr.Zero)
        {
            NativeResult.CopyAndFree(ref second);
        }

        if (error != IntPtr.Zero)
        {
            NativeMethods.FreeString(error);
            error = IntPtr.Zero;
        }
    }
}
