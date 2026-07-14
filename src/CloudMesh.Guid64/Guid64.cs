using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.IO.Hashing;

namespace System
{
    /// <summary>
    /// Represents a compact, time-sortable 64-bit identifier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Guid64"/> generates identifiers using a Twitter Snowflake-inspired algorithm,
    /// combining a timestamp, node identifier, and sequence number into a single signed 64-bit value.
    /// Identifiers are approximately ordered by creation time and are unique across configured nodes.
    /// </para>
    /// <para>
    /// Unlike <see cref="Guid"/>, the generated value fits in a <see cref="long"/>, making it suitable
    /// for systems that cannot exchange GUIDs or where a compact numeric identifier is preferred.
    /// </para>
    /// <para>
    /// <b>Recommended usage:</b>
    /// <list type="bullet">
    /// <item>
    /// <description>Use wherever a globally unique identifier is required, but chronological ordering is beneficial.</description>
    /// </item>
    /// <item>
    /// <description>Use when identifiers must fit within a signed 64-bit integer.</description>
    /// </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Not recommended:</b>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Do not use it for security-sensitive identifiers where creation time, request rates,
    /// or identifier predictability must remain hidden, such as invoice numbers,
    /// public order numbers, password reset tokens, or other externally visible secrets.
    /// </description>
    /// </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Formatting:</b>
    /// <list type="table">
    /// <item>
    /// <term><c>B</c></term>
    /// <description>Crockford Base32 (default), fixed-width 13-character representation.</description>
    /// </item>
    /// <item>
    /// <term><c>D</c></term>
    /// <description>Decimal representation of the underlying <see cref="long"/>.</description>
    /// </item>
    /// <item>
    /// <term><c>X</c></term>
    /// <description>Hexadecimal representation of the underlying <see cref="long"/>.</description>
    /// </item>
    /// </list>
    /// </para>
    /// <para>
    /// For distributed deployments, configure <see cref="NodeId"/> with a unique value per node
    /// to prevent identifier collisions.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("{ToString()}")]
    public readonly struct Guid64 : IEquatable<Guid64>, IComparable<Guid64>, IComparable, ISpanFormattable, IParsable<Guid64>, ISpanParsable<Guid64>
    {
        private readonly long value;

        /// <summary>The zero-valued <see cref="Guid64"/> (<c>0</c>). Use as a default/sentinel; never returned by <see cref="NewGuid"/>.</summary>
        public static Guid64 Empty => new();

        /// <summary>
        /// Gets or sets the node id used by <see cref="NewGuid"/>.
        /// </summary>
        /// <remarks>
        /// Must be in the range 0-1023. Configure this explicitly in clustered deployments to avoid
        /// collisions between nodes. If not configured, a best-effort node id is derived from local
        /// network interfaces.
        /// </remarks>
        public static int NodeId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Engine.NodeId;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Engine.NodeId = value;
        }

        private Guid64(long value) => this.value = value;

        /// <summary>Initializes an empty (<c>0</c>) <see cref="Guid64"/>. Use <see cref="NewGuid"/> to generate a real id.</summary>
        public Guid64() => value = 0;

        /// <summary>
        /// Generates a new, roughly time-sortable id. Thread-safe and monotonic within a process: it will spin to
        /// the next millisecond rather than emit an out-of-order or duplicate value.
        /// </summary>
        /// <returns>A fresh <see cref="Guid64"/> combining timestamp, <see cref="NodeId"/>, and per-millisecond sequence.</returns>
        /// <exception cref="InvalidOperationException">
        /// The configured epoch has expired (41-bit timestamp overflow, ~69 years after 2015-01-01), or the system
        /// clock moved backwards far enough that ordering cannot be preserved.
        /// </exception>
        public static Guid64 NewGuid() => Engine.Next();

        /// <summary>Implicitly unwraps the id to its underlying signed 64-bit value (sortable by creation time).</summary>
        public static implicit operator long(Guid64 guid) => guid.value;
        /// <summary>Implicitly wraps a raw signed 64-bit value as a <see cref="Guid64"/> (no validation).</summary>
        public static implicit operator Guid64(long value) => new(value);

        /// <summary>The underlying signed 64-bit value. Equivalent to the implicit <see cref="long"/> conversion.</summary>
        public long Value => value;

        /// <summary>Returns the default Crockford Base32 form (fixed-width, 13 characters, lexically sortable).</summary>
        public override string ToString() => ToString("B", CultureInfo.InvariantCulture);

        /// <inheritdoc cref="ToString(string?, IFormatProvider?)"/>
        public string ToString(string? format) => ToString(format, CultureInfo.InvariantCulture);

        /// <summary>Formats the id.</summary>
        /// <param name="format">
        /// <c>B</c> (default) Crockford Base32, fixed-width 13 chars, lexically sortable; <c>D</c> decimal;
        /// <c>X</c>/<c>x</c> hexadecimal. <see langword="null"/> or empty means <c>B</c>.
        /// </param>
        /// <param name="formatProvider">Culture used for the <c>D</c>/<c>X</c> numeric forms; ignored for <c>B</c>.</param>
        /// <exception cref="FormatException"><paramref name="format"/> is not one of <c>B</c>, <c>D</c>, or <c>X</c>.</exception>
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            format = NormalizeFormat(format);

            if (format == "B")
                return ToBase32String();

            return value.ToString(format, formatProvider);
        }

        /// <summary>Tries to write the formatted id into <paramref name="destination"/> without allocating.</summary>
        /// <param name="destination">Target buffer. The <c>B</c> form needs at least 13 characters.</param>
        /// <param name="charsWritten">The number of characters written on success; otherwise <c>0</c>.</param>
        /// <param name="format">Same options as <see cref="ToString(string?, IFormatProvider?)"/>; empty means <c>B</c>.</param>
        /// <param name="provider">Culture for the <c>D</c>/<c>X</c> forms; ignored for <c>B</c>.</param>
        /// <returns><see langword="true"/> if the id fit; <see langword="false"/> if <paramref name="destination"/> was too small.</returns>
        public bool TryFormat(
            Span<char> destination,
            out int charsWritten,
            ReadOnlySpan<char> format = default,
            IFormatProvider? provider = null)
        {
            var f = NormalizeFormat(format);

            if (f == "B")
            {
                if (destination.Length < 13)
                {
                    charsWritten = 0;
                    return false;
                }

                ToBase32String(destination);
                charsWritten = 13;
                return true;
            }

            return value.TryFormat(destination, out charsWritten, f, provider);
        }

        /// <summary>Writes the fixed-width 13-character Crockford Base32 form into <paramref name="destination"/> (zero-alloc).</summary>
        /// <param name="destination">Target buffer; must be at least 13 characters long.</param>
        /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 13 characters.</exception>
        public void ToBase32String(Span<char> destination) => Base32.Format(value, destination);

        /// <summary>Returns the fixed-width 13-character Crockford Base32 form. Because the width is fixed, these strings sort lexically in the same order as the ids.</summary>
        public string ToBase32String()
        {
            Span<char> buffer = stackalloc char[13];
            ToBase32String(buffer);
            return new string(buffer);
        }

        /// <summary>
        /// Parses the Crockford Base32 form (the default <see cref="ToString()"/> output) back into a
        /// <see cref="Guid64"/>. Decoding is lenient (case-insensitive, I/L→1, O→0, '-' ignored).
        /// </summary>
        /// <exception cref="FormatException">The input is not a valid <see cref="Guid64"/>.</exception>
        public static Guid64 Parse(ReadOnlySpan<char> s)
        {
            if (!TryParse(s, out var result))
                throw new FormatException("Input is not a valid Guid64 value.");
            return result;
        }

        /// <inheritdoc cref="Parse(ReadOnlySpan{char})"/>
        public static Guid64 Parse(string s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return Parse(s.AsSpan());
        }

        /// <summary>Tries to parse the Crockford Base32 form back into a <see cref="Guid64"/>.</summary>
        public static bool TryParse(ReadOnlySpan<char> s, out Guid64 result)
        {
            if (Base32.TryDecodeInt64(s, out var value))
            {
                result = new Guid64(value);
                return true;
            }

            result = default;
            return false;
        }

        /// <inheritdoc cref="TryParse(ReadOnlySpan{char}, out Guid64)"/>
        public static bool TryParse([NotNullWhen(true)] string? s, out Guid64 result)
        {
            if (s is null)
            {
                result = default;
                return false;
            }

            return TryParse(s.AsSpan(), out result);
        }

        // IParsable<Guid64> / ISpanParsable<Guid64> — the format provider is not used.
        /// <inheritdoc/>
        public static Guid64 Parse(string s, IFormatProvider? provider) => Parse(s);

        /// <inheritdoc/>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Guid64 result)
            => TryParse(s, out result);

        /// <inheritdoc/>
        public static Guid64 Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);

        /// <inheritdoc/>
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Guid64 result)
            => TryParse(s, out result);

        /// <summary>Indicates whether two ids have the same underlying value.</summary>
        public bool Equals(Guid64 other) => value == other.value;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Guid64 guid && Equals(guid);

        /// <inheritdoc/>
        public override int GetHashCode() => value.GetHashCode();

        /// <summary>Orders ids by their underlying value, which is (approximately) chronological order of creation.</summary>
        public int CompareTo(Guid64 other) => value.CompareTo(other.value);

        /// <inheritdoc cref="CompareTo(Guid64)"/>
        /// <exception cref="ArgumentException"><paramref name="obj"/> is not a <see cref="Guid64"/>.</exception>
        public int CompareTo(object? obj)
        {
            if (obj is null)
                return 1;

            if (obj is Guid64 other)
                return CompareTo(other);

            throw new ArgumentException($"Object must be of type {nameof(Guid64)}.", nameof(obj));
        }

        /// <summary>Equality by underlying value.</summary>
        public static bool operator ==(Guid64 left, Guid64 right) => left.value == right.value;
        /// <summary>Inequality by underlying value.</summary>
        public static bool operator !=(Guid64 left, Guid64 right) => left.value != right.value;

        /// <summary>Equality between an id's underlying value and a <c>long</c>.</summary>
        public static bool operator ==(Guid64 left, long right) => left.value == right;
        /// <summary>Inequality between an id's underlying value and a <c>long</c>.</summary>
        public static bool operator !=(Guid64 left, long right) => left.value != right;
        /// <summary>Equality between a <c>long</c> and an id's underlying value.</summary>
        public static bool operator ==(long left, Guid64 right) => left == right.value;
        /// <summary>Inequality between a <c>long</c> and an id's underlying value.</summary>
        public static bool operator !=(long left, Guid64 right) => left != right.value;

        /// <summary>Orders two ids by underlying value (approximately chronological).</summary>
        public static bool operator <(Guid64 left, Guid64 right) => left.value < right.value;
        /// <summary>Orders two ids by underlying value (approximately chronological).</summary>
        public static bool operator >(Guid64 left, Guid64 right) => left.value > right.value;
        /// <summary>Orders two ids by underlying value (approximately chronological).</summary>
        public static bool operator <=(Guid64 left, Guid64 right) => left.value <= right.value;
        /// <summary>Orders two ids by underlying value (approximately chronological).</summary>
        public static bool operator >=(Guid64 left, Guid64 right) => left.value >= right.value;

        /// <summary>Orders an id's underlying value against a <c>long</c>.</summary>
        public static bool operator <(Guid64 left, long right) => left.value < right;
        /// <summary>Orders an id's underlying value against a <c>long</c>.</summary>
        public static bool operator >(Guid64 left, long right) => left.value > right;
        /// <summary>Orders an id's underlying value against a <c>long</c>.</summary>
        public static bool operator <=(Guid64 left, long right) => left.value <= right;
        /// <summary>Orders an id's underlying value against a <c>long</c>.</summary>
        public static bool operator >=(Guid64 left, long right) => left.value >= right;

        /// <summary>Orders a <c>long</c> against an id's underlying value.</summary>
        public static bool operator <(long left, Guid64 right) => left < right.value;
        /// <summary>Orders a <c>long</c> against an id's underlying value.</summary>
        public static bool operator >(long left, Guid64 right) => left > right.value;
        /// <summary>Orders a <c>long</c> against an id's underlying value.</summary>
        public static bool operator <=(long left, Guid64 right) => left <= right.value;
        /// <summary>Orders a <c>long</c> against an id's underlying value.</summary>
        public static bool operator >=(long left, Guid64 right) => left >= right.value;

        private static string NormalizeFormat(string? format)
        {
            if (string.IsNullOrEmpty(format))
                return "B";

            if (format.Length != 1)
                throw new FormatException("Format must be one of: B, D, or X.");

            return NormalizeFormat(format[0]);
        }

        private static string NormalizeFormat(ReadOnlySpan<char> format)
        {
            if (format.Length == 0)
                return "B";

            if (format.Length != 1)
                throw new FormatException("Format must be one of: B, D, or X.");

            return NormalizeFormat(format[0]);
        }

        private static string NormalizeFormat(char format)
        {
            return format switch
            {
                'B' or 'b' => "B",
                'D' or 'd' => "D",
                'X' => "X",
                'x' => "x",
                _ => throw new FormatException("Format must be one of: B, D, or X.")
            };
        }

        /// <summary>
        /// The low-level Snowflake id generator behind <see cref="Guid64.NewGuid"/>. Exposed for advanced
        /// scenarios; most callers should use <see cref="Guid64.NewGuid"/> and <see cref="Guid64.NodeId"/> instead.
        /// </summary>
        /// <remarks>
        /// Layout of the 64-bit value: 41 bits timestamp (ms since a 2015-01-01 epoch, ~69 years of range) ·
        /// 10 bits node id (0–1023 machines) · 12 bits sequence (4096 ids per node per millisecond). Generation
        /// is serialized by a lock and is monotonic within the process.
        /// </remarks>
        public static class Engine
        {
            // Twitter Snowflake
            // -----------------
            // Twitter snowflake generates 64-bit unique IDs at high scale. 
            // It can create up to 4096 unique ID's per machine per millisecond in a cluster of up to 1024 machines.

            // The IDs generated by this service are roughly time sortable.
            // The IDs are made up of the following components:
            // Epoch timestamp in millisecond precision - 41 bits (gives us 69 years with a custom epoch)
            // Configured machine id - 10 bits(gives us up to 1024 machines)
            // Sequence number - 12 bits(A local counter per machine that rolls over every 4096)

            private const int EpochBits = 41;
            private const int NodeIdBits = 10;
            private const int SequenceBits = 12;

            private const int TimestampShift = NodeIdBits + SequenceBits;
            private const int NodeIdShift = SequenceBits;

            private const int MaxNodeId = (1 << NodeIdBits) - 1;
            private const long MaxSequence = (1L << SequenceBits) - 1;
            private const long MaxTimestamp = (1L << EpochBits) - 1;

            // Custom Epoch (January 1, 2015 Midnight UTC = 2015-01-01T00:00:00Z)
            private const long CustomEpoch = 1420070400000L;

            private static readonly object Sync = new();

            private static int _nodeId = CreateNodeId();
            private static long _lastTimestamp = -1L;
            private static long _sequence = 0L;

            /// <summary>The node id (0–1023) embedded in generated ids. See <see cref="Guid64.NodeId"/>.</summary>
            /// <exception cref="ArgumentOutOfRangeException">The value is outside 0–1023.</exception>
            public static int NodeId
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Volatile.Read(ref _nodeId);

                set
                {
                    if ((uint)value > MaxNodeId)
                        throw new ArgumentOutOfRangeException(nameof(value), value,
                            $"Node id must be between 0 and {MaxNodeId}.");

                    Volatile.Write(ref _nodeId, value);
                }
            }

            /// <summary>Generates the next raw 64-bit id. Thread-safe; backs <see cref="Guid64.NewGuid"/>.</summary>
            /// <exception cref="InvalidOperationException">The epoch expired, or the clock moved backwards unrecoverably.</exception>
            public static long Next()
            {
                lock (Sync)
                {
                    var currentTimestamp = Timestamp();

                    if ((ulong)currentTimestamp > MaxTimestamp)
                        throw new InvalidOperationException("The configured Guid64 epoch has expired.");

                    if (currentTimestamp < _lastTimestamp)
                    {
                        for (var i = 0; i < 3; i++)
                        {
                            currentTimestamp = WaitNextMillis(currentTimestamp);
                            _sequence = 0L;
                            if (currentTimestamp > _lastTimestamp)
                                break;
                        }

                        if (currentTimestamp < _lastTimestamp)
                            throw new InvalidOperationException(
                                $"Invalid System Clock. Current timestamp {currentTimestamp} is before last timestamp {_lastTimestamp}");
                    }

                    if (currentTimestamp == _lastTimestamp)
                    {
                        _sequence = (_sequence + 1) & MaxSequence;
                        if (_sequence == 0)
                        {
                            // Sequence Exhausted, wait till next millisecond.
                            currentTimestamp = WaitNextMillis(currentTimestamp);
                        }
                    }
                    else
                    {
                        // reset sequence to start with zero for the next millisecond
                        _sequence = 0;
                    }

                    _lastTimestamp = currentTimestamp;

                    return
                        (currentTimestamp << TimestampShift) |
                        ((long)NodeId << NodeIdShift) |
                        _sequence;
                }
            }

            // Get current timestamp in milliseconds, adjust for the custom epoch.
            private static long Timestamp()
                => HighResolutionTimestamp.Now.ToUnixTimeMilliseconds() - CustomEpoch;

            // Block and wait till the next millisecond. Called when sequence numbers are 
            // exhausted within a millisecond. I.e. we create more than 4096 IDs per millisecond.
            private static long WaitNextMillis(long currentTimestamp)
            {
                while (currentTimestamp <= _lastTimestamp)
                    currentTimestamp = Timestamp();

                return currentTimestamp;
            }

            private static int CreateNodeId()
            {
                int nodeId;
                try
                {
                    var buffer = new ArrayBufferWriter<byte>();
                    var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                    foreach (var networkInterface in networkInterfaces)
                    {
                        var mac = networkInterface.GetPhysicalAddress().GetAddressBytes();
                        buffer.Write(mac);
                    }

#if NET9_0_OR_GREATER
                    var nodeHash = XxHash32.Hash(buffer.WrittenSpan);
                    nodeId = BitConverter.ToInt32(nodeHash);
#else
                    nodeId = unchecked((int)XxHash32.HashToUInt32(buffer.WrittenSpan));
#endif
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch
                {
                    using var rng = RandomNumberGenerator.Create();
                    var rand = new byte[sizeof(int)];
                    rng.GetBytes(rand);
                    nodeId = BitConverter.ToInt32(rand, 0);
                }
#pragma warning restore CA1031 // Do not catch general exception types

                nodeId &= MaxNodeId;
                return nodeId;
            }
        }
    }
}