namespace CloudMesh.Memory;

/// <summary>
/// Helpers for working with pooled/reusable <see cref="Memory{T}"/> buffers.
/// </summary>
public static class MemoryHelper
{
    /// <summary>
    /// Replaces <paramref name="buffer"/> with a newly allocated buffer large enough to hold at least
    /// <paramref name="minCount"/> elements, rounded up to <paramref name="alignment"/>. Optionally copies the
    /// existing contents into the new buffer.
    /// </summary>
    /// <typeparam name="T">The element type of the buffer.</typeparam>
    /// <param name="buffer">The buffer to grow, replaced in place with the larger allocation.</param>
    /// <param name="minCount">The minimum number of elements the new buffer must hold.</param>
    /// <param name="preserveContents">When <see langword="true"/>, copies the current contents into the new buffer.</param>
    /// <param name="clearItems">When <see langword="true"/>, zero-initializes the new buffer; otherwise it is uninitialized.</param>
    /// <param name="alignment">The size boundary the new capacity is rounded up to, to reduce the number of reallocations.</param>
    public static void GrowBuffer<T>(
        ref Memory<T> buffer,
        int minCount,
        bool preserveContents = false,
        bool clearItems = false,
        int alignment = 8192)
    {
        var newSize = minCount > buffer.Length ? minCount : buffer.Length;
        newSize = (newSize + alignment + 1) & ~alignment;
        var temp = new Memory<T>(clearItems ? GC.AllocateArray<T>(newSize) : GC.AllocateUninitializedArray<T>(newSize));
        if (preserveContents)
            buffer.CopyTo(temp);
        buffer = temp;
    }
}