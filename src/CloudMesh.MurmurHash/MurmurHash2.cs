using System.Text;

namespace CloudMesh.Utils
{
    /// <summary>
    /// Implements the 64 bit A version of the murmur2 hash
    /// </summary>
    /// <remarks>
    /// Copied from Wikipedia
    /// </remarks>
    public class MurmurHash2
    {
        private const ulong Magic = 0xc6a4a7935bd1e995;
        private const int r = 47;

        public static ulong Hash(ushort key, ulong seed = 0)
        {
            var h = seed ^ unchecked(2UL * Magic);

            h ^= (ulong)key << 8;
            h ^= key;
            h *= Magic;

            h = Finish(h);

            return h;
        }

        public static ulong Hash(ulong key, ulong seed = 0)
        {
            var h = seed ^ unchecked(8UL * Magic);

            h = Mix(key, h);

            h = Finish(h);

            return h;
        }

        public static ulong Hash(byte key, ulong seed = 0)
        {
            var h = seed ^ Magic;

            h ^= key;
            h *= Magic;

            h = Finish(h);

            return h;
        }

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
