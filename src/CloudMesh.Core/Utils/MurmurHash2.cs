using System.Text;

namespace CloudMesh.Utils
{
    /// <summary>
    /// Implements the 64 bit A version of the murmur2 hash
    /// </summary>
    public class MurmurHash2
    {
        private const ulong m = 0xc6a4a7935bd1e995;
        private const int r = 47;

        public static ulong Hash(ushort key, ulong seed = 0)
        {
            ulong h = seed ^ unchecked(2UL * m);

            h ^= (ulong)key << 8;
            h ^= key;
            h *= m;

            h = Finish(h);

            return h;
        }

        public static ulong Hash(ulong key, ulong seed = 0)
        {
            ulong h = seed ^ unchecked(8UL * m);

            h = Mix(key, h);

            h = Finish(h);

            return h;
        }

        public static ulong Hash(byte key, ulong seed = 0)
        {
            ulong h = seed ^ m;

            h ^= key;
            h *= m;

            h = Finish(h);

            return h;
        }

        public static ulong Hash(byte[] key, ulong seed = 0)
        {
            int len = key.Length;
            ulong h = seed ^ (ulong)len * m;

            unsafe
            {
                fixed (byte* data = &key[0])
                {
                    int blocks = len / 8;
                    int remainder = len & 7;

                    ulong* b = (ulong*)data;

                    while (blocks-- > 0)
                    {
                        ulong k = *b++;
                        h = Mix(k, h);
                    }

                    byte* remainderBlock = (byte*)b;

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
                            h *= m;
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
            k *= m;
            k ^= k >> r;
            k *= m;

            h ^= k;
            h *= m;

            return h;
        }

        private static ulong Finish(ulong h)
        {
            h ^= h >> r;
            h *= m;
            h ^= h >> r;

            return h;
        }
    }
}
