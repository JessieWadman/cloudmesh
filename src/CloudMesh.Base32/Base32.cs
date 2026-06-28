using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace System;

/// <summary>
/// Provides high-performance methods for encoding values using Crockford Base32.
/// </summary>
/// <remarks>
/// Uses the Crockford Base32 alphabet:
/// <c>0123456789ABCDEFGHJKMNPQRSTVWXYZ</c>.
/// This implementation does not emit padding characters ('=') and always uses
/// the Crockford Base32 alphabet.
/// </remarks>
public static class Base32
{
    private static ReadOnlySpan<byte> Alphabet =>
        "0123456789ABCDEFGHJKMNPQRSTVWXYZ"u8;

    /// <summary>
    /// Calculates the number of Base32 characters required to encode the specified number of bytes.
    /// </summary>
    /// <param name="byteLength">The number of input bytes.</param>
    /// <returns>The number of Base32 characters required.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBase32CharCount(int byteLength)
        => (byteLength * 8 + 4) / 5;

    /// <summary>
    /// Formats a 64-bit signed integer as a fixed-width 13-character Crockford Base32 value.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="destination">The destination buffer.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is shorter than 13 characters.
    /// </exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Format(long value, Span<char> destination)
    {
        if (destination.Length < 13)
            ThrowDestinationTooSmall();

        var x = unchecked((ulong)value);

        destination[12] = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[11] = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[10] = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[9]  = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[8]  = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[7]  = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[6]  = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[5]  = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[4]  = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[3]  = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[2]  = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[1]  = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[0]  = (char)Alphabet[(int)x];
    }

    /// <summary>
    /// Formats a 32-bit signed integer as a fixed-width 7-character Crockford Base32 value.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="destination">The destination buffer.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is shorter than 7 characters.
    /// </exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Format(int value, Span<char> destination)
    {
        if (destination.Length < 7)
            ThrowDestinationTooSmall();

        var x = unchecked((uint)value);

        destination[6] = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[5] = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[4] = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[3] = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[2] = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[1] = (char)Alphabet[(int)(x & 31)]; x >>= 5;
        destination[0] = (char)Alphabet[(int)x];
    }

    /// <summary>
    /// Encodes a sequence of bytes using Crockford Base32.
    /// </summary>
    /// <param name="value">The bytes to encode.</param>
    /// <param name="destination">The destination buffer.</param>
    /// <remarks>
    /// The destination buffer must be at least
    /// <see cref="GetBase32CharCount(int)"/> characters long.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is too small.
    /// </exception>
    public static void Format(scoped ReadOnlySpan<byte> value, Span<char> destination)
    {
        var required = GetBase32CharCount(value.Length);

        if (destination.Length < required)
            ThrowDestinationTooSmall();

        ulong buffer = 0;
        var bits = 0;
        var o = 0;

        for (var i = 0; i < value.Length; i++)
        {
            buffer = (buffer << 8) | value[i];
            bits += 8;

            while (bits >= 5)
            {
                bits -= 5;
                destination[o++] = (char)Alphabet[(int)((buffer >> bits) & 31)];
            }
        }

        if (bits != 0)
            destination[o] = (char)Alphabet[(int)((buffer << (5 - bits)) & 31)];
    }

    /// <summary>
    /// Encodes UTF-8 text using Crockford Base32.
    /// </summary>
    /// <param name="value">The text to encode.</param>
    /// <param name="destination">The destination buffer.</param>
    /// <remarks>
    /// The input is first encoded as UTF-8 before being Base32 encoded.
    /// The destination buffer must be large enough to hold the encoded UTF-8 byte sequence.
    /// Use <see cref="Encoding.UTF8"/> and <see cref="GetBase32CharCount(int)"/>
    /// to calculate the required size if necessary.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is too small.
    /// </exception>
    public static void FormatUtf8(scoped ReadOnlySpan<char> value, Span<char> destination)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        var required = GetBase32CharCount(byteCount);

        if (destination.Length < required)
            ThrowDestinationTooSmall();

        var bytes = byteCount <= 512
            ? stackalloc byte[byteCount]
            : new byte[byteCount];

        Encoding.UTF8.GetBytes(value, bytes);
        Format(bytes, destination);
    }

    // Reverse lookup: Crockford Base32 char -> 5-bit value, or -1 if not a valid symbol. Decoding is
    // lenient per the Crockford spec: case-insensitive, the confusable letters I/L map to 1 and O to 0.
    // (U is intentionally not a symbol and stays invalid.)
    private static readonly sbyte[] DecodeMap = CreateDecodeMap();

    private static sbyte[] CreateDecodeMap()
    {
        var map = new sbyte[256];
        Array.Fill(map, (sbyte)-1);

        ReadOnlySpan<char> alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        for (var v = 0; v < alphabet.Length; v++)
        {
            var c = alphabet[v];
            map[c] = (sbyte)v;
            map[char.ToLowerInvariant(c)] = (sbyte)v;
        }

        map['O'] = map['o'] = 0;
        map['I'] = map['i'] = map['L'] = map['l'] = 1;
        return map;
    }

    /// <summary>
    /// Decodes a Crockford Base32 string into a 64-bit signed integer (the inverse of
    /// <see cref="Format(long, Span{char})"/>).
    /// </summary>
    /// <remarks>
    /// Decoding is lenient: case-insensitive, the confusable letters <c>I</c>/<c>L</c> are read as
    /// <c>1</c> and <c>O</c> as <c>0</c>, and <c>'-'</c> separators are ignored. Up to 13 significant
    /// symbols are accepted; more (or any value beyond 64 bits) fails.
    /// </remarks>
    /// <param name="source">The Crockford Base32 text.</param>
    /// <param name="value">The decoded value, or <c>0</c> when decoding fails.</param>
    /// <returns><see langword="true"/> if <paramref name="source"/> was a valid value; otherwise <see langword="false"/>.</returns>
    public static bool TryDecodeInt64(scoped ReadOnlySpan<char> source, out long value)
    {
        var map = DecodeMap;
        ulong acc = 0;
        var count = 0;

        foreach (var c in source)
        {
            if (c == '-')
                continue;
            if (c > byte.MaxValue)
            {
                value = 0;
                return false;
            }

            var digit = map[c];
            if (digit < 0)
            {
                value = 0;
                return false;
            }

            if (acc > (ulong.MaxValue >> 5)) // the next shift would overflow 64 bits
            {
                value = 0;
                return false;
            }

            acc = (acc << 5) | (byte)digit; // digit is 0-31; via byte to avoid sign-extension
            count++;
        }

        if (count == 0)
        {
            value = 0;
            return false;
        }

        value = unchecked((long)acc);
        return true;
    }

    /// <summary>
    /// Decodes a Crockford Base32 string into a 64-bit signed integer, throwing on invalid input.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="source"/> is not a valid Crockford Base32 value.</exception>
    public static long DecodeInt64(scoped ReadOnlySpan<char> source)
    {
        if (!TryDecodeInt64(source, out var value))
            throw new FormatException("Input is not a valid Crockford Base32 value.");
        return value;
    }

    /// <summary>
    /// Decodes a Crockford Base32 string into a 32-bit signed integer (the inverse of
    /// <see cref="Format(int, Span{char})"/>). See <see cref="TryDecodeInt64"/> for the leniency rules.
    /// </summary>
    public static bool TryDecodeInt32(scoped ReadOnlySpan<char> source, out int value)
    {
        var map = DecodeMap;
        uint acc = 0;
        var count = 0;

        foreach (var c in source)
        {
            if (c == '-')
                continue;
            if (c > byte.MaxValue)
            {
                value = 0;
                return false;
            }

            var digit = map[c];
            if (digit < 0)
            {
                value = 0;
                return false;
            }

            if (acc > (uint.MaxValue >> 5))
            {
                value = 0;
                return false;
            }

            acc = (acc << 5) | (byte)digit; // digit is 0-31; via byte to avoid sign-extension
            count++;
        }

        if (count == 0)
        {
            value = 0;
            return false;
        }

        value = unchecked((int)acc);
        return true;
    }

    /// <summary>
    /// Decodes a Crockford Base32 string into a 32-bit signed integer, throwing on invalid input.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="source"/> is not a valid Crockford Base32 value.</exception>
    public static int DecodeInt32(scoped ReadOnlySpan<char> source)
    {
        if (!TryDecodeInt32(source, out var value))
            throw new FormatException("Input is not a valid Crockford Base32 value.");
        return value;
    }

    /// <summary>
    /// Calculates the maximum number of bytes that decoding the specified number of Base32 characters
    /// can produce. The actual count may be smaller when the input contains <c>'-'</c> separators.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMaxByteCount(int charCount) => charCount * 5 / 8;

    /// <summary>
    /// Decodes Crockford Base32 text into bytes (the inverse of
    /// <see cref="Format(ReadOnlySpan{byte}, Span{char})"/>).
    /// </summary>
    /// <param name="source">The Base32 text.</param>
    /// <param name="destination">Receives the decoded bytes; size with <see cref="GetMaxByteCount(int)"/>.</param>
    /// <param name="bytesWritten">The number of bytes written to <paramref name="destination"/>.</param>
    /// <returns>
    /// <see langword="true"/> on success; <see langword="false"/> if the input contained an invalid
    /// symbol or <paramref name="destination"/> was too small. See <see cref="TryDecodeInt64"/> for the
    /// Crockford leniency rules (case-insensitive, I/L→1, O→0, <c>'-'</c> ignored).
    /// </returns>
    public static bool TryDecode(scoped ReadOnlySpan<char> source, Span<byte> destination, out int bytesWritten)
    {
        var state = new DecodeState();
        DecodeChunk(source, destination, ref state);
        return Complete(ref state, out bytesWritten);
    }

    /// <inheritdoc cref="TryDecode(ReadOnlySpan{char}, Span{byte}, out int)"/>
    /// <remarks>The source is UTF-8/ASCII-encoded Base32 text.</remarks>
    public static bool TryDecode(scoped ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
    {
        var state = new DecodeState();
        DecodeChunk(source, destination, ref state);
        return Complete(ref state, out bytesWritten);
    }

    /// <inheritdoc cref="TryDecode(ReadOnlySpan{char}, Span{byte}, out int)"/>
    public static bool TryDecode(in ReadOnlySequence<char> source, Span<byte> destination, out int bytesWritten)
    {
        var state = new DecodeState();
        foreach (var segment in source)
        {
            DecodeChunk(segment.Span, destination, ref state);
            if (state.Status != DecodeStatus.Ok)
                break;
        }

        return Complete(ref state, out bytesWritten);
    }

    /// <inheritdoc cref="TryDecode(ReadOnlySpan{char}, Span{byte}, out int)"/>
    /// <remarks>The source is UTF-8/ASCII-encoded Base32 text.</remarks>
    public static bool TryDecode(in ReadOnlySequence<byte> source, Span<byte> destination, out int bytesWritten)
    {
        var state = new DecodeState();
        foreach (var segment in source)
        {
            DecodeChunk(segment.Span, destination, ref state);
            if (state.Status != DecodeStatus.Ok)
                break;
        }

        return Complete(ref state, out bytesWritten);
    }

    /// <summary>
    /// Decodes Crockford Base32 text into bytes, throwing on invalid input or a too-small destination.
    /// </summary>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="FormatException"><paramref name="source"/> contained an invalid symbol.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> was too small.</exception>
    public static int Decode(scoped ReadOnlySpan<char> source, Span<byte> destination)
    {
        var state = new DecodeState();
        DecodeChunk(source, destination, ref state);
        return CompleteOrThrow(ref state);
    }

    /// <inheritdoc cref="Decode(ReadOnlySpan{char}, Span{byte})"/>
    public static int Decode(scoped ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var state = new DecodeState();
        DecodeChunk(source, destination, ref state);
        return CompleteOrThrow(ref state);
    }

    /// <inheritdoc cref="Decode(ReadOnlySpan{char}, Span{byte})"/>
    public static int Decode(in ReadOnlySequence<char> source, Span<byte> destination)
    {
        var state = new DecodeState();
        foreach (var segment in source)
        {
            DecodeChunk(segment.Span, destination, ref state);
            if (state.Status != DecodeStatus.Ok)
                break;
        }

        return CompleteOrThrow(ref state);
    }

    /// <inheritdoc cref="Decode(ReadOnlySpan{char}, Span{byte})"/>
    public static int Decode(in ReadOnlySequence<byte> source, Span<byte> destination)
    {
        var state = new DecodeState();
        foreach (var segment in source)
        {
            DecodeChunk(segment.Span, destination, ref state);
            if (state.Status != DecodeStatus.Ok)
                break;
        }

        return CompleteOrThrow(ref state);
    }

    // Streaming 5-bit -> 8-bit regrouping. State carries across chunks so a ReadOnlySequence decodes
    // correctly regardless of where its segment boundaries fall.
    private enum DecodeStatus : byte { Ok, InvalidSymbol, DestinationTooSmall }

    private struct DecodeState
    {
        public ulong Buffer;
        public int Bits;
        public int Written;
        public DecodeStatus Status;
    }

    private static void DecodeChunk(scoped ReadOnlySpan<char> chunk, Span<byte> destination, ref DecodeState state)
    {
        var map = DecodeMap;
        foreach (var c in chunk)
        {
            if (c == '-')
                continue;
            if (c > byte.MaxValue)
            {
                state.Status = DecodeStatus.InvalidSymbol;
                return;
            }

            if (!Emit(map[c], destination, ref state))
                return;
        }
    }

    private static void DecodeChunk(scoped ReadOnlySpan<byte> chunk, Span<byte> destination, ref DecodeState state)
    {
        var map = DecodeMap;
        foreach (var b in chunk)
        {
            if (b == (byte)'-')
                continue;

            if (!Emit(map[b], destination, ref state))
                return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Emit(sbyte digit, Span<byte> destination, ref DecodeState state)
    {
        if (digit < 0)
        {
            state.Status = DecodeStatus.InvalidSymbol;
            return false;
        }

        state.Buffer = (state.Buffer << 5) | (byte)digit;
        state.Bits += 5;

        if (state.Bits >= 8)
        {
            state.Bits -= 8;
            if (state.Written >= destination.Length)
            {
                state.Status = DecodeStatus.DestinationTooSmall;
                return false;
            }

            destination[state.Written++] = (byte)(state.Buffer >> state.Bits);
        }

        return true;
    }

    private static bool Complete(ref DecodeState state, out int bytesWritten)
    {
        if (state.Status != DecodeStatus.Ok)
        {
            bytesWritten = 0;
            return false;
        }

        bytesWritten = state.Written;
        return true;
    }

    private static int CompleteOrThrow(ref DecodeState state)
    {
        switch (state.Status)
        {
            case DecodeStatus.DestinationTooSmall:
                ThrowDestinationTooSmall();
                break;
            case DecodeStatus.InvalidSymbol:
                throw new FormatException("Input is not a valid Crockford Base32 value.");
        }

        return state.Written;
    }

    [DoesNotReturn]
    private static void ThrowDestinationTooSmall()
        => throw new ArgumentException("Destination too small.", "destination");
}