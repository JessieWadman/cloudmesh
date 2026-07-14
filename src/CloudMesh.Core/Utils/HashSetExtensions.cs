using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CloudMesh.Utils
{
    /// <summary>
    /// Low-overhead bulk operations for <see cref="HashSet{T}"/> and arrays that iterate with
    /// <see cref="System.Runtime.CompilerServices.Unsafe"/> spans to avoid enumerator allocation.
    /// </summary>
    public static class HashSetExtensions
    {
        /// <summary>Adds every element of <paramref name="source"/> to the set.</summary>
        /// <param name="hashSet">The set to add to.</param>
        /// <param name="source">The items to add.</param>
        public static void AddRange<T>(this HashSet<T> hashSet, T[] source)
        {
            // Benched as fastest iter possible (allocation-free and linear scaling)
            // Copied from .NET Core source code.
            Span<T> span = source;
            ref var searchSpace = ref MemoryMarshal.GetReference(span);
            for (var i = 0; i < span.Length; i++)
            {
                var item = Unsafe.Add(ref searchSpace, i);
                hashSet.Add(item);
            }
        }
        
        /// <summary>Invokes <paramref name="action"/> for each element of the array using an allocation-free span scan.</summary>
        /// <param name="array">The array to iterate.</param>
        /// <param name="action">The action to run per element.</param>
        public static void FastForEach<T>(this T[] array, Action<T> action)
        {
            Span<T> span = array;
            ref var searchSpace = ref MemoryMarshal.GetReference(span);
            for (var i = 0; i < span.Length; i++)
            {
                var item = Unsafe.Add(ref searchSpace, i);
                action(item);
            }
        }

        /// <summary>
        /// Invokes <paramref name="action"/> for each element of the set. The set is snapshotted to an array first,
        /// so the action may safely modify the original set during iteration.
        /// </summary>
        /// <param name="hashSet">The set to iterate.</param>
        /// <param name="action">The action to run per element.</param>
        public static void FastForEach<T>(this HashSet<T> hashSet, Action<T> action)
            => FastForEach(hashSet.ToArray(), action);
    }
}
