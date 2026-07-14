using System.Runtime.CompilerServices;

namespace CloudMesh.Utils;

/// <summary>
/// Unsafe helpers for wiping character memory in place, so secrets (passwords, tokens) held in a
/// <see cref="string"/> or char buffer can be scrubbed from memory rather than left for the GC.
/// </summary>
/// <remarks>
/// Overwriting a <see cref="string"/> in place violates string immutability. Only use this on strings you own and
/// know are not interned or shared; interned or shared instances must never be passed here.
/// </remarks>
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

    /// <summary>Overwrites <paramref name="count"/> characters starting at <paramref name="ptr"/> with null characters.</summary>
    /// <param name="ptr">A pointer to the first character to clear.</param>
    /// <param name="count">The number of characters to overwrite.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ClearMemory(char* ptr, int count)
    {
        for (var i = 0; i < count; i++)
            ptr[i] = (char)0;
    }
}