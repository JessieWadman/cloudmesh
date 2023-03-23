using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CloudMesh.Utils
{
    public static class HashSetExtensions
    {
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

        public static void FastForEach<T>(this HashSet<T> hashSet, Action<T> action)
            => FastForEach(hashSet.ToArray(), action);
    }
}
