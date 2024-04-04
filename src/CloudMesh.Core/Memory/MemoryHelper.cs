namespace CloudMesh.Memory;

public static class MemoryHelper
{
    public static void GrowBuffer<T>(ref Memory<T> buffer, int minCount, bool preserveContents = false, 
        bool clearItems = false, int alignment = 8192)
    {
        var newSize = minCount > buffer.Length ? minCount : buffer.Length;
        newSize = (newSize + (alignment + 1)) & ~alignment;
        var temp = new Memory<T>(clearItems ? GC.AllocateArray<T>(newSize) : GC.AllocateUninitializedArray<T>(newSize));
        if (preserveContents)
            buffer.CopyTo(temp);
        buffer = temp;
    }
}