//-----------------------------------------------------------------------
// <copyright file="MurmurHash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System.Collections;

namespace CloudMesh.Utils
{
    /// <summary>
    /// A streaming 32-bit MurmurHash implementation producing well-distributed <see cref="int"/> hash codes for
    /// strings, arrays, and sets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Adapted from the <see href="https://github.com/akkadotnet/akka.net">Akka.NET</see> project
    /// (original author: Aaron Stannard / "Aaronontheweb"), under the Apache 2.0 license. All credit for the
    /// implementation goes to the Akka.NET contributors.
    /// </para>
    /// <para>
    /// The convenience entry points are <see cref="StringHash"/>, <see cref="ArrayHash{T}"/>,
    /// <see cref="ByteHash"/>, and <see cref="SymmetricHash{T}"/>. The lower-level <see cref="StartHash"/> /
    /// <see cref="ExtendHash"/> / <see cref="FinalizeHash"/> primitives let you fold values into a running
    /// hash incrementally.
    /// </para>
    /// <para>
    /// On .NET 9 and later this type is marked obsolete: prefer the BCL-native
    /// <c>System.IO.Hashing.XxHash</c> family unless you need compatibility with previously computed values.
    /// </para>
    /// </remarks>
#if (NET9_0_OR_GREATER)
    [Obsolete("XxHash is BCL native, consider using that instead, unless you need compatibility")]
#endif
    public static class MurmurHash
    {
        // Magic values used for MurmurHash's 32 bit hash.
        // Don't change these without consulting a hashing expert!
        private const uint VisibleMagic = 0x971e137b;
        private const uint HiddenMagicA = 0x95543787;
        private const uint HiddenMagicB = 0x2ad7eb25;
        private const uint VisibleMixer = 0x52dce729;
        private const uint HiddenMixerA = 0x7b7d159c;
        private const uint HiddenMixerB = 0x6bce6396;
        private const uint FinalMixer1 = 0x85ebca6b;
        private const uint FinalMixer2 = 0xc2b2ae35;

        // Arbitrary values used for hashing certain classes

        private const uint StringSeed = 0x331df49;
        private const uint ArraySeed = 0x3c074a61;

        /** The first 23 magic integers from the first stream are stored here */
        private static readonly uint[] StoredMagicA;

        /** The first 23 magic integers from the second stream are stored here */
        private static readonly uint[] StoredMagicB;

        /// <summary>
        /// The initial magic integer in the first stream.
        /// </summary>
        public const uint StartMagicA = HiddenMagicA;

        /// <summary>
        /// The initial magic integer in the second stream.
        /// </summary>
        public const uint StartMagicB = HiddenMagicB;

        /// <summary>
        /// Precomputes the fixed magic-integer streams used by <see cref="SymmetricHash{T}"/>.
        /// </summary>
        static MurmurHash()
        {
            //compute range of values for StoredMagicA
            var storedMagicA = new List<uint>();
            var nextMagicA = HiddenMagicA;
            foreach (var i in Enumerable.Repeat(0, 23))
            {
                nextMagicA = NextMagicA(nextMagicA);
                storedMagicA.Add(nextMagicA);
            }
            StoredMagicA = storedMagicA.ToArray();

            //compute range of values for StoredMagicB
            var storedMagicB = new List<uint>();
            var nextMagicB = HiddenMagicB;
            foreach (var i in Enumerable.Repeat(0, 23))
            {
                nextMagicB = NextMagicB(nextMagicB);
                storedMagicB.Add(nextMagicB);
            }
            StoredMagicB = storedMagicB.ToArray();
        }

        /// <summary>
        /// Begins a new hash from a seed value.
        /// </summary>
        /// <param name="seed">The initial seed to derive the starting hash state from.</param>
        /// <returns>The initial hash state, ready to be fed to <see cref="ExtendHash"/>.</returns>
        public static uint StartHash(uint seed)
        {
            return seed ^ VisibleMagic;
        }

        /// <summary>
        /// Given a magic integer from the first stream, computes the next one in that stream.
        /// </summary>
        /// <param name="magicA">The current magic integer from the first stream.</param>
        /// <returns>The next magic integer in the first stream.</returns>
        public static uint NextMagicA(uint magicA)
        {
            return magicA * 5 + HiddenMixerA;
        }

        /// <summary>
        /// Given a magic integer from the second stream, computes the next one in that stream.
        /// </summary>
        /// <param name="magicB">The current magic integer from the second stream.</param>
        /// <returns>The next magic integer in the second stream.</returns>
        public static uint NextMagicB(uint magicB)
        {
            return magicB * 5 + HiddenMixerB;
        }

        /// <summary>
        /// Incorporates a new value into an existing hash
        /// </summary>
        /// <param name="hash">The prior hash value</param>
        /// <param name="value">The new value to incorporate</param>
        /// <param name="magicA">A magic integer from the left of the stream</param>
        /// <param name="magicB">A magic integer from a different stream</param>
        /// <returns>The updated hash value</returns>
        public static uint ExtendHash(uint hash, uint value, uint magicA, uint magicB)
        {
            return (hash ^ RotateLeft32(value * magicA, 11) * magicB) * 3 + VisibleMixer;
        }

        /// <summary>
        /// Once all values have been incorporated, performs the final avalanche mixing to produce the hash.
        /// </summary>
        /// <param name="hash">The accumulated hash state.</param>
        /// <returns>The finalized, well-distributed 32-bit hash.</returns>
        public static uint FinalizeHash(uint hash)
        {
            var h = (hash ^ (hash >> 16));
            h *= FinalMixer1;
            h ^= h >> 13;
            h *= FinalMixer2;
            h ^= h >> 16;
            return h;
        }

        #region Internal 32-bit hashing helpers

        /// <summary>
        /// Rotate a 32-bit unsigned integer to the left by <paramref name="shift"/> bits
        /// </summary>
        /// <param name="original">Original value</param>
        /// <param name="shift">The shift value</param>
        /// <returns>The rotated 32-bit integer</returns>
        private static uint RotateLeft32(uint original, int shift)
        {
            return (original << shift) | (original >> (32 - shift));
        }

        #endregion

        /// <summary>
        /// Computes a high-quality 32-bit hash of a byte array.
        /// </summary>
        /// <param name="b">The bytes to hash.</param>
        /// <returns>A 32-bit hash of the array contents.</returns>
        public static int ByteHash(byte[] b)
        {
            return ArrayHash(b);
        }

        /// <summary>
        /// Computes a high-quality 32-bit hash of an array by folding each element's
        /// <see cref="object.GetHashCode"/> into the running hash.
        /// </summary>
        /// <typeparam name="T">The element type of the array.</typeparam>
        /// <param name="a">The array to hash.</param>
        /// <returns>An order-sensitive 32-bit hash of the array's elements.</returns>
        public static int ArrayHash<T>(T[] a)
        {
            unchecked
            {
                var h = StartHash((uint)a.Length * ArraySeed);
                var c = HiddenMagicA;
                var k = HiddenMagicB;
                var j = 0;
                while (j < a.Length)
                {
                    h = ExtendHash(h, (uint)a[j]!.GetHashCode(), c, k);
                    c = NextMagicA(c);
                    k = NextMagicB(k);
                    j += 1;
                }
                return (int)FinalizeHash(h);
            }
        }

        /// <summary>
        /// Computes a high-quality, deterministic 32-bit hash of a string from its UTF-16 code units.
        /// </summary>
        /// <param name="s">The string to hash.</param>
        /// <returns>A stable 32-bit hash of the string.</returns>
        /// <remarks>
        /// Unlike <see cref="string.GetHashCode()"/>, this is stable across processes and runtimes, which
        /// makes it suitable for sharding, bucketing, and consistent-hashing scenarios.
        /// </remarks>
        public static int StringHash(string s)
        {
            unchecked
            {
                var span = s.AsSpan();
                var h = StartHash((uint)s.Length * StringSeed);
                var c = HiddenMagicA;
                var k = HiddenMagicB;
                var j = 0;
                while (j + 1 < s.Length)
                {
                    var i = (uint)((span[j] << 16) + span[j + 1]);
                    h = ExtendHash(h, i, c, k);
                    c = NextMagicA(c);
                    k = NextMagicB(k);
                    j += 2;
                }
                if (j < s.Length) h = ExtendHash(h, span[j], c, k);
                return (int)FinalizeHash(h);
            }
        }

        /// <summary>
        /// Compute a hash that is symmetric in its arguments--that is,
        /// where the order of appearance of elements does not matter.
        /// This is useful for hashing sets, for example.
        /// </summary>
        /// <typeparam name="T">The element type of the sequence.</typeparam>
        /// <param name="xs">The elements to hash; iteration order does not affect the result.</param>
        /// <param name="seed">A seed that scales with the element count to derive the starting state.</param>
        /// <returns>An order-independent 32-bit hash of the elements.</returns>
        public static int SymmetricHash<T>(IEnumerable<T> xs, uint seed)
        {
            unchecked
            {
                uint a = 0, b = 0, n = 0;
                uint c = 1;
                foreach (var i in xs)
                {
                    var u = (uint)i!.GetHashCode();
                    a += u;
                    b ^= u;
                    if (u != 0) c *= u;
                    n += 1;
                }

                var h = StartHash(seed * n);
                h = ExtendHash(h, a, StoredMagicA[0], StoredMagicB[0]);
                h = ExtendHash(h, b, StoredMagicA[1], StoredMagicB[1]);
                h = ExtendHash(h, c, StoredMagicA[2], StoredMagicB[2]);
                return (int)FinalizeHash(h);
            }
        }
    }

    /// <summary>
    /// Extension method class to make it easier to work with <see cref="BitArray"/> instances
    /// </summary>
    public static class BitArrayHelpers
    {
        /// <summary>
        /// Converts a <see cref="BitArray"/> into an array of <see cref="byte"/>
        /// </summary>
        /// <param name="arr">The bit array to convert; must contain exactly 8 bits.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if there aren't enough bits in the given <paramref name="arr"/> to make a byte.
        /// </exception>
        /// <returns>A single-byte array holding the packed bits.</returns>
        public static byte[] ToBytes(this BitArray arr)
        {
            if (arr.Length != 8)
            {
                throw new ArgumentException("Not enough bits to make a byte!", nameof(arr));
            }
            var bytes = new byte[(arr.Length - 1) / 8 + 1];
            ((ICollection)arr).CopyTo(bytes, 0);
            return bytes;
        }
    }
}
