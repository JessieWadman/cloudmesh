#if (NET8_0_OR_GREATER)

using System.Runtime.CompilerServices;

namespace System.IO;

public static class MemoryStreamAccessor
{
    /// <summary>
    /// Access the internal buffer within a MemoryStream as ReadOnlyMemory
    /// </summary>
    /// <param name="memoryStream">The memory stream whose memory to get</param>
    /// <returns>A ReadOnlyMemory accessor</returns>
    /// <remarks>
    /// The ReadOnlyMemory is only valid until a write is made to the stream, or the stream is closed/disposed.
    /// Use this instead of copying the buffer to an array (double allocation), but do so with caution.
    /// </remarks>
    public static ReadOnlyMemory<byte> UnsafeGetMemory(this MemoryStream memoryStream)
    {
        var origin = GetOrigin(memoryStream);
        var length = GetLength(memoryStream);
        return new ReadOnlyMemory<byte>(GetBuffer(memoryStream), origin, length - origin);
    }

    /// <summary>
    /// Access the internal buffer within a MemoryStream as ReadOnlySpan
    /// </summary>
    /// <param name="memoryStream">The memory stream whose memory to get</param>
    /// <returns>A ReadOnlySpan accessor</returns>
    /// <remarks>
    /// The ReadOnlySpan is only valid until a write is made to the stream, or the stream is closed/disposed.
    /// Use this instead of copying the buffer to an array (double allocation), but do so with caution.
    /// </remarks>
    public static ReadOnlySpan<byte> UnsafeGetSpan(this MemoryStream memoryStream)
    {
        var origin = GetOrigin(memoryStream);
        var length = GetLength(memoryStream);
        return new ReadOnlySpan<byte>(GetBuffer(memoryStream), origin, length - origin);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_buffer")]
    private static extern ref byte[] GetBuffer(MemoryStream @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_origin")]
    private static extern ref int GetOrigin(MemoryStream @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_length")]
    private static extern ref int GetLength(MemoryStream @this);
}
#endif