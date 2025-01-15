using System.Runtime.CompilerServices;

namespace CloudMesh.Utils;

public static class UnsafeStringOperations
{
    /// <summary>
    /// Attempts to overwrite the memory occupied by a string, in order to clear out secret values from memory.
    /// </summary>
    /// <param name="stringContainingSecret">Pointer to the memory where the string exists</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ClearMemory(ref string stringContainingSecret)
    {
        // Overwrite the memory of the string with blanks.
        fixed (char* ptr = stringContainingSecret)
        {
            for (var i = 0; i < stringContainingSecret.Length; i++)
                ptr[i] = (char)0;
        }

        // Point string to empty string.
        stringContainingSecret = string.Empty;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ClearMemory(char* ptr, int count)
    {
        for (var i = 0; i < count; i++)
            ptr[i] = (char)0;
    }
}