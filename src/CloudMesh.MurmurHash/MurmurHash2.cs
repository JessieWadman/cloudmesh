using System.Text;

namespace CloudMesh.Utils
{
    /// <summary>
    /// Implements the 64-bit "A" variant of MurmurHash2 (<c>MurmurHash64A</c>), producing a
    /// <see cref="ulong"/> hash for scalar values, byte arrays, and strings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a straightforward port of the canonical <c>MurmurHash64A</c> reference algorithm. It is
    /// endian-dependent and intended for in-memory hashing (sharding, hash tables, bloom filters), not as a
    /// cryptographic or cross-architecture-stable digest.
    /// </para>
    /// <para>
    /// On .NET 9 and later this type is marked obsolete: prefer the BCL-native
    /// <c>System.IO.Hashing.XxHash</c> family unless you need compatibility with previously computed values.
    /// </para>
    /// </remarks>
#if (NET9_0_OR_GREATER)
    [Obsolete("XxHash is BCL native, consider using that instead, unless you need compatibility")]
#endif
    public class MurmurHash2
    {
        private const ulong Magic = 0xc6a4a7935bd1e995;
        private const int r = 47;

        /// <summary>Computes the 64-bit MurmurHash2 of a 16-bit value.</summary>
        /// <param name="key">The value to hash.</param>
        /// <param name="seed">An optional seed to vary the hash.</param>
        /// <returns>The 64-bit hash.</returns>
        public static ulong Hash(ushort key, ulong seed = 0)
        {
            var h = seed ^ unchecked(2UL * Magic);

            h ^= (ulong)key << 8;
            h ^= key;
            h *= Magic;

            h = Finish(h);

            return h;
        }

        /// <summary>Computes the 64-bit MurmurHash2 of a 64-bit value.</summary>
        /// <param name="key">The value to hash.</param>
        /// <param name="seed">An optional seed to vary the hash.</param>
        /// <returns>The 64-bit hash.</returns>
        public static ulong Hash(ulong key, ulong seed = 0)
        {
            var h = seed ^ unchecked(8UL * Magic);

            h = Mix(key, h);

            h = Finish(h);

            return h;
        }

        /// <summary>Computes the 64-bit MurmurHash2 of a single byte.</summary>
        /// <param name="key">The value to hash.</param>
        /// <param name="seed">An optional seed to vary the hash.</param>
        /// <returns>The 64-bit hash.</returns>
        public static ulong Hash(byte key, ulong seed = 0)
        {
            var h = seed ^ Magic;

            h ^= key;
            h *= Magic;

            h = Finish(h);

            return h;
        }

        /// <summary>Computes the 64-bit MurmurHash2 of a byte array.</summary>
        /// <param name="key">The bytes to hash.</param>
        /// <param name="seed">An optional seed to vary the hash.</param>
        /// <returns>The 64-bit hash.</returns>
        /// <remarks>Uses unsafe pointer access to read the input in 8-byte blocks; the result is endian-dependent.</remarks>
        public static ulong Hash(byte[] key, ulong seed = 0)
        {
            var len = key.Length;
            var h = seed ^ (ulong)len * Magic;

            unsafe
            {
                fixed (byte* data = &key[0])
                {
                    var blocks = len / 8;
                    var remainder = len & 7;

                    var b = (ulong*)data;

                    while (blocks-- > 0)
                    {
                        var k = *b++;
                        h = Mix(k, h);
                    }

                    var remainderBlock = (byte*)b;

                    switch (remainder)
                    {
                        case 7:
                            h ^= (ulong)*remainderBlock++ << 48;
                            goto case 6;

                        case 6:
                            h ^= (ulong)*remainderBlock++ << 40;
                            goto case 5;

                        case 5:
                            h ^= (ulong)*remainderBlock++ << 32;
                            goto case 4;

                        case 4:
                            h ^= (ulong)*remainderBlock++ << 24;
                            goto case 3;

                        case 3:
                            h ^= (ulong)*remainderBlock++ << 16;
                            goto case 2;

                        case 2:
                            h ^= (ulong)*remainderBlock++ << 8;
                            goto case 1;

                        case 1:
                            h ^= *remainderBlock;
                            h *= Magic;
                            break;
                    }
                }
            }

            h = Finish(h);

            return h;
        }

        /// <summary>Computes the 64-bit MurmurHash2 of a string over its UTF-8 encoding.</summary>
        /// <param name="value">The string to hash.</param>
        /// <param name="seed">An optional seed to vary the hash.</param>
        /// <returns>The 64-bit hash of the UTF-8 bytes of <paramref name="value"/>.</returns>
        public static ulong HashString(string value, ulong seed = 0)
            => Hash(Encoding.UTF8.GetBytes(value), seed);

        private static ulong Mix(ulong k, ulong h)
        {
            k *= Magic;
            k ^= k >> r;
            k *= Magic;

            h ^= k;
            h *= Magic;

            return h;
        }

        private static ulong Finish(ulong h)
        {
            h ^= h >> r;
            h *= Magic;
            h ^= h >> r;

            return h;
        }
    }
}
