using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FullBleed.DotNet;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRenderOptions
{
    public float PageWidthPt;
    public float PageHeightPt;
    public float MarginTopPt;
    public float MarginRightPt;
    public float MarginBottomPt;
    public float MarginLeftPt;

    public static NativeRenderOptions FromManaged(RenderOptions options) => new()
    {
        PageWidthPt = options.PageWidthPt,
        PageHeightPt = options.PageHeightPt,
        MarginTopPt = options.MarginTopPt,
        MarginRightPt = options.MarginRightPt,
        MarginBottomPt = options.MarginBottomPt,
        MarginLeftPt = options.MarginLeftPt,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeByteBuffer
{
    public IntPtr Ptr;
    public nuint Len;
}

internal sealed class SafeEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeEngineHandle()
        : base(ownsHandle: true)
    {
    }

    internal SafeEngineHandle(IntPtr handle)
        : this()
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.EngineFree(handle);
        return true;
    }
}

internal sealed class SafeCompiledHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeCompiledHandle()
        : base(ownsHandle: true)
    {
    }

    internal SafeCompiledHandle(IntPtr handle)
        : this()
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.CompiledFree(handle);
        return true;
    }
}

internal static unsafe partial class NativeMethods
{
    internal const string LibraryName = "fullbleed_dotnet_native";

    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveLibrary);
    }

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_dotnet_abi_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint AbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_dotnet_build_features")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode BuildFeatures(
        out NativeByteBuffer json,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_engine_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode EngineCreate(
        byte* optionsJson,
        nuint optionsLength,
        out IntPtr engine,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_engine_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void EngineFree(IntPtr engine);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_compiled_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CompiledFree(IntPtr compiled);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_engine_render_pdf")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode EngineRenderPdf(
        SafeEngineHandle engine,
        byte* html,
        nuint htmlLength,
        byte* css,
        nuint cssLength,
        out NativeByteBuffer pdf,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_engine_render_pdf_to_file")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode EngineRenderPdfToFile(
        SafeEngineHandle engine,
        byte* html,
        nuint htmlLength,
        byte* css,
        nuint cssLength,
        byte* path,
        nuint pathLength,
        out nuint bytesWritten,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_engine_render_with_diagnostics")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode EngineRenderWithDiagnostics(
        SafeEngineHandle engine,
        byte* html,
        nuint htmlLength,
        byte* css,
        nuint cssLength,
        out NativeByteBuffer pdf,
        out NativeByteBuffer json,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_engine_render_with_metrics")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode EngineRenderWithMetrics(
        SafeEngineHandle engine,
        byte* html,
        nuint htmlLength,
        byte* css,
        nuint cssLength,
        out NativeByteBuffer pdf,
        out NativeByteBuffer json,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_engine_render_image_pages_to_dir")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode EngineRenderImagePagesToDirectory(
        SafeEngineHandle engine,
        byte* html,
        nuint htmlLength,
        byte* css,
        nuint cssLength,
        byte* outputDirectory,
        nuint outputDirectoryLength,
        byte* stem,
        nuint stemLength,
        uint dpi,
        out NativeByteBuffer json,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_engine_render_finalized_pdf_image_pages_to_dir")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode EngineRenderFinalizedPdfImagePagesToDirectory(
        SafeEngineHandle engine,
        byte* pdfPath,
        nuint pdfPathLength,
        byte* outputDirectory,
        nuint outputDirectoryLength,
        byte* stem,
        nuint stemLength,
        uint dpi,
        out NativeByteBuffer json,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_engine_render_batch")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode EngineRenderBatch(
        SafeEngineHandle engine,
        byte* requestJson,
        nuint requestLength,
        out NativeByteBuffer pdf,
        out NativeByteBuffer json,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_engine_render_batch_to_file")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode EngineRenderBatchToFile(
        SafeEngineHandle engine,
        byte* requestJson,
        nuint requestLength,
        byte* path,
        nuint pathLength,
        out nuint bytesWritten,
        out NativeByteBuffer json,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_engine_compile")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode EngineCompile(
        SafeEngineHandle engine,
        byte* html,
        nuint htmlLength,
        byte* css,
        nuint cssLength,
        out IntPtr compiled,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_compiled_stats")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode CompiledStats(
        SafeCompiledHandle compiled,
        out NativeByteBuffer json,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_compiled_render")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode CompiledRender(
        SafeCompiledHandle compiled,
        nuint copies,
        out NativeByteBuffer pdf,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_compiled_render_to_file")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode CompiledRenderToFile(
        SafeCompiledHandle compiled,
        nuint copies,
        byte* path,
        nuint pathLength,
        out nuint bytesWritten,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_compiled_render_bindings")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode CompiledRenderBindings(
        SafeCompiledHandle compiled,
        byte* bindingsJson,
        nuint bindingsLength,
        [MarshalAs(UnmanagedType.I1)] bool reflow,
        byte* compression,
        nuint compressionLength,
        out NativeByteBuffer pdf,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_compiled_render_bindings_to_file")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode CompiledRenderBindingsToFile(
        SafeCompiledHandle compiled,
        byte* bindingsJson,
        nuint bindingsLength,
        [MarshalAs(UnmanagedType.I1)] bool reflow,
        byte* compression,
        nuint compressionLength,
        byte* path,
        nuint pathLength,
        out nuint bytesWritten,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_inspect_pdf")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode InspectPdf(
        byte* path,
        nuint pathLength,
        out NativeByteBuffer json,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_finalize_stamp")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode FinalizeStamp(
        byte* template,
        nuint templateLength,
        byte* overlay,
        nuint overlayLength,
        byte* output,
        nuint outputLength,
        byte* requestJson,
        nuint requestLength,
        out NativeByteBuffer json,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_finalize_compose")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode FinalizeCompose(
        byte* overlay,
        nuint overlayLength,
        byte* output,
        nuint outputLength,
        byte* requestJson,
        nuint requestLength,
        out NativeByteBuffer json,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_render_html_to_pdf")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial FullBleedStatusCode RenderHtmlToPdf(
        byte* html,
        nuint htmlLength,
        byte* css,
        nuint cssLength,
        ref NativeRenderOptions options,
        out NativeByteBuffer pdf,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_buffer_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void FreeBuffer(IntPtr pointer, nuint length);

    [LibraryImport(LibraryName, EntryPoint = "fullbleed_string_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void FreeString(IntPtr pointer);

    private static IntPtr ResolveLibrary(
        string libraryName,
        Assembly _,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        var explicitPath = Environment.GetEnvironmentVariable("FULLBLEED_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return NativeLibrary.Load(explicitPath);
        }

        var fileName = GetNativeFileName();
        foreach (var directory in CandidateDirectories())
        {
            var direct = Path.Combine(directory, fileName);
            if (File.Exists(direct))
            {
                return NativeLibrary.Load(direct);
            }

            var runtimeAsset = Path.Combine(
                directory,
                "runtimes",
                RuntimeInformation.RuntimeIdentifier,
                "native",
                fileName);
            if (File.Exists(runtimeAsset))
            {
                return NativeLibrary.Load(runtimeAsset);
            }

            var localBuild = Path.Combine(
                directory,
                "native",
                "fullbleed-dotnet-native",
                "target",
                "release",
                fileName);
            if (File.Exists(localBuild))
            {
                return NativeLibrary.Load(localBuild);
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var origin in new[]
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
        })
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                continue;
            }

            var directory = new DirectoryInfo(Path.GetFullPath(origin));
            for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
            {
                if (seen.Add(directory.FullName))
                {
                    yield return directory.FullName;
                }
            }
        }
    }

    private static string GetNativeFileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "fullbleed_dotnet_native.dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "libfullbleed_dotnet_native.dylib";
        }

        return "libfullbleed_dotnet_native.so";
    }
}
