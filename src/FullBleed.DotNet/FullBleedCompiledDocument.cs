namespace FullBleed.DotNet;

public sealed class FullBleedCompiledDocument : IDisposable
{
    private readonly SafeCompiledHandle _handle;
    private bool _disposed;

    internal FullBleedCompiledDocument(SafeCompiledHandle handle)
    {
        _handle = handle;
    }

    public CompiledDocumentStats GetStats()
    {
        ThrowIfDisposed();
        NativeByteBuffer json = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            var status = NativeMethods.CompiledStats(_handle, out json, out error);
            NativeResult.EnsureSuccess(status, error);
            error = IntPtr.Zero;
            return NativeResult.ReadJsonAndFree(
                ref json,
                FullBleedJsonContext.Default.CompiledDocumentStats);
        }
        finally
        {
            FreeNativeOutputs(ref json, ref error);
        }
    }

    public byte[] Render(int copies = 1)
    {
        ThrowIfDisposed();
        ValidateCopies(copies);
        NativeByteBuffer pdf = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            var status = NativeMethods.CompiledRender(
                _handle,
                (nuint)copies,
                out pdf,
                out error);
            NativeResult.EnsureSuccess(status, error);
            error = IntPtr.Zero;
            return NativeResult.CopyAndFree(ref pdf);
        }
        finally
        {
            FreeNativeOutputs(ref pdf, ref error);
        }
    }

    public long RenderToFile(string outputPath, int copies = 1)
    {
        ThrowIfDisposed();
        ValidateCopies(copies);
        NativeResult.EnsureOutputParent(outputPath);
        var path = NativeResult.Utf8(outputPath);
        IntPtr error = IntPtr.Zero;
        try
        {
            nuint bytesWritten;
            unsafe
            {
                fixed (byte* pathPointer = path)
                {
                    var status = NativeMethods.CompiledRenderToFile(
                        _handle,
                        (nuint)copies,
                        pathPointer,
                        (nuint)path.Length,
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

    public byte[] RenderBindings(IReadOnlyDictionary<string, IReadOnlyList<string>> bindings) =>
        RenderBindingsCore(NormalizeBindings(bindings), reflow: false, CompiledFlowCompression.Throughput);

    public long RenderBindingsToFile(
        IReadOnlyDictionary<string, IReadOnlyList<string>> bindings,
        string outputPath) =>
        RenderBindingsToFileCore(
            NormalizeBindings(bindings),
            outputPath,
            reflow: false,
            CompiledFlowCompression.Throughput);

    public byte[] RenderReflowBindings(
        IReadOnlyDictionary<string, IReadOnlyList<string>> bindings,
        CompiledFlowCompression compression = CompiledFlowCompression.Throughput) =>
        RenderBindingsCore(NormalizeBindings(bindings), reflow: true, compression);

    public long RenderReflowBindingsToFile(
        IReadOnlyDictionary<string, IReadOnlyList<string>> bindings,
        string outputPath,
        CompiledFlowCompression compression = CompiledFlowCompression.Throughput) =>
        RenderBindingsToFileCore(NormalizeBindings(bindings), outputPath, reflow: true, compression);

    public byte[] RenderBindings<T>(
        IEnumerable<T> records,
        Action<BindingMap<T>> configure)
    {
        var columns = CreateBindings(records, configure);
        return RenderBindingsCore(columns, reflow: false, CompiledFlowCompression.Throughput);
    }

    public long RenderBindingsToFile<T>(
        IEnumerable<T> records,
        Action<BindingMap<T>> configure,
        string outputPath)
    {
        var columns = CreateBindings(records, configure);
        return RenderBindingsToFileCore(
            columns,
            outputPath,
            reflow: false,
            CompiledFlowCompression.Throughput);
    }

    public byte[] RenderReflowBindings<T>(
        IEnumerable<T> records,
        Action<BindingMap<T>> configure,
        CompiledFlowCompression compression = CompiledFlowCompression.Throughput)
    {
        var columns = CreateBindings(records, configure);
        return RenderBindingsCore(columns, reflow: true, compression);
    }

    public long RenderReflowBindingsToFile<T>(
        IEnumerable<T> records,
        Action<BindingMap<T>> configure,
        string outputPath,
        CompiledFlowCompression compression = CompiledFlowCompression.Throughput)
    {
        var columns = CreateBindings(records, configure);
        return RenderBindingsToFileCore(columns, outputPath, reflow: true, compression);
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

    private byte[] RenderBindingsCore(
        Dictionary<string, List<string>> bindings,
        bool reflow,
        CompiledFlowCompression compression)
    {
        ThrowIfDisposed();
        var jsonBytes = NativeResult.Serialize(
            bindings,
            FullBleedJsonContext.Default.DictionaryStringListString);
        var compressionBytes = NativeResult.Utf8(compression.ToString());
        NativeByteBuffer pdf = default;
        IntPtr error = IntPtr.Zero;
        try
        {
            unsafe
            {
                fixed (byte* jsonPointer = jsonBytes)
                fixed (byte* compressionPointer = compressionBytes)
                {
                    var status = NativeMethods.CompiledRenderBindings(
                        _handle,
                        jsonPointer,
                        (nuint)jsonBytes.Length,
                        reflow,
                        compressionPointer,
                        (nuint)compressionBytes.Length,
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

    private long RenderBindingsToFileCore(
        Dictionary<string, List<string>> bindings,
        string outputPath,
        bool reflow,
        CompiledFlowCompression compression)
    {
        ThrowIfDisposed();
        NativeResult.EnsureOutputParent(outputPath);
        var jsonBytes = NativeResult.Serialize(
            bindings,
            FullBleedJsonContext.Default.DictionaryStringListString);
        var compressionBytes = NativeResult.Utf8(compression.ToString());
        var pathBytes = NativeResult.Utf8(outputPath);
        IntPtr error = IntPtr.Zero;
        try
        {
            nuint bytesWritten;
            unsafe
            {
                fixed (byte* jsonPointer = jsonBytes)
                fixed (byte* compressionPointer = compressionBytes)
                fixed (byte* pathPointer = pathBytes)
                {
                    var status = NativeMethods.CompiledRenderBindingsToFile(
                        _handle,
                        jsonPointer,
                        (nuint)jsonBytes.Length,
                        reflow,
                        compressionPointer,
                        (nuint)compressionBytes.Length,
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

    private static Dictionary<string, List<string>> NormalizeBindings(
        IReadOnlyDictionary<string, IReadOnlyList<string>> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return bindings.ToDictionary(
            pair => pair.Key,
            pair => pair.Value?.ToList()
                ?? throw new ArgumentException($"Binding column '{pair.Key}' is null.", nameof(bindings)),
            StringComparer.Ordinal);
    }

    private static Dictionary<string, List<string>> CreateBindings<T>(
        IEnumerable<T> records,
        Action<BindingMap<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var map = new BindingMap<T>();
        configure(map);
        return map.Materialize(records);
    }

    private static void ValidateCopies(int copies)
    {
        if (copies < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(copies), copies, "Copies must be at least one.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FullBleedCompiledDocument));
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
}
