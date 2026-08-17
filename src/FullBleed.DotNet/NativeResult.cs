using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FullBleed.DotNet;

internal static class NativeResult
{
    internal static void EnsureSuccess(FullBleedStatusCode status, IntPtr nativeError)
    {
        if (status == FullBleedStatusCode.Ok)
        {
            return;
        }

        var message = nativeError == IntPtr.Zero
            ? $"Fullbleed native call failed with status {status}."
            : System.Runtime.InteropServices.Marshal.PtrToStringUTF8(nativeError)
                ?? $"Fullbleed native call failed with status {status}.";
        // The caller owns and releases nativeError in its finally block. Keeping ownership in
        // one scope prevents failure paths from freeing the same allocation twice.
        throw new FullBleedException(status, message);
    }

    internal static byte[] CopyAndFree(ref NativeByteBuffer buffer)
    {
        try
        {
            if (buffer.Ptr == IntPtr.Zero || buffer.Len == 0)
            {
                return [];
            }

            if (buffer.Len > int.MaxValue)
            {
                throw new FullBleedException(
                    FullBleedStatusCode.RenderFailed,
                    $"Native output exceeded the managed array limit: {buffer.Len} bytes.");
            }

            var bytes = new byte[(int)buffer.Len];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Ptr, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (buffer.Ptr != IntPtr.Zero)
            {
                NativeMethods.FreeBuffer(buffer.Ptr, buffer.Len);
                buffer = default;
            }
        }
    }

    internal static T ReadJsonAndFree<T>(
        ref NativeByteBuffer buffer,
        JsonTypeInfo<T> typeInfo)
    {
        var bytes = CopyAndFree(ref buffer);
        return JsonSerializer.Deserialize(bytes, typeInfo)
            ?? throw new FullBleedException(
                FullBleedStatusCode.SerializationFailed,
                $"Native JSON result could not be deserialized as {typeof(T).Name}.");
    }

    internal static byte[] Utf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encoding.UTF8.GetBytes(value);
    }

    internal static byte[] Serialize<T>(T value, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);

    internal static void EnsureOutputParent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }
}
