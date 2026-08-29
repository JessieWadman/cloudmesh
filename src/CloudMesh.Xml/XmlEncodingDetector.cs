using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace CloudMesh.Xml;

/// <summary>
/// Detects the character encoding of XML data by inspecting Byte Order Marks (BOM),
/// multibyte XML signatures, and the XML declaration (<c>&lt;?xml ... encoding="..."?&gt;</c>).
/// </summary>
/// <remarks>
/// <para>
/// Detection proceeds in four stages:
/// <list type="number">
/// <item>
/// <term>Byte Order Marks (BOM)</term>
/// <description>UTF-8, UTF-16 (Big/Little Endian), and UTF-32 (Big/Little Endian) BOMs.</description>
/// </item>
/// <item>
/// <term>Multibyte XML signatures without BOM</term>
/// <description>Recognizes UTF-16, UTF-32, and EBCDIC byte patterns at the start of the payload.</description>
/// </item>
/// <item>
/// <term>XML declaration</term>
/// <description>Parses the <c>encoding</c> attribute from the <c>&lt;?xml ...?&gt;</c> header in ASCII-compatible, UTF-16, or EBCDIC text.</description>
/// </item>
/// <item>
/// <term>W3C Fallback</term>
/// <description>Defaults to <see cref="Encoding.UTF8"/> if no BOM or encoding attribute is present.</description>
/// </item>
/// </list>
/// </para>
/// </remarks>
public static class XmlEncodingDetector
{
    static XmlEncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Detects the character encoding of the XML payload in a <see cref="ReadOnlySequence{Byte}"/>.
    /// </summary>
    /// <param name="sequence">The XML byte sequence to inspect.</param>
    /// <returns>The detected <see cref="Encoding"/>, or <see cref="Encoding.UTF8"/> by default.</returns>
    public static Encoding Detect(ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsEmpty)
        {
            return Encoding.UTF8;
        }

        if (sequence.IsSingleSegment)
        {
            return Detect(sequence.FirstSpan);
        }

        const int maxHeaderBytes = 4096;
        var length = (int)Math.Min(sequence.Length, maxHeaderBytes);
        Span<byte> buffer = stackalloc byte[length];
        sequence.Slice(0, length).CopyTo(buffer);
        return Detect(buffer);
    }

    /// <summary>
    /// Detects the character encoding of the XML payload in a <see cref="ReadOnlyMemory{Byte}"/>.
    /// </summary>
    /// <param name="memory">The XML byte memory to inspect.</param>
    /// <returns>The detected <see cref="Encoding"/>, or <see cref="Encoding.UTF8"/> by default.</returns>
    public static Encoding Detect(ReadOnlyMemory<byte> memory) => Detect(memory.Span);

    /// <summary>
    /// Detects the character encoding of the XML payload in a <see cref="ReadOnlySpan{Byte}"/>.
    /// </summary>
    /// <param name="span">The XML byte span to inspect.</param>
    /// <returns>The detected <see cref="Encoding"/>, or <see cref="Encoding.UTF8"/> by default.</returns>
    public static Encoding Detect(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty)
        {
            return Encoding.UTF8;
        }

        // --------------------------------------------------------------------
        // 1. Check Byte Order Marks (BOM)
        // --------------------------------------------------------------------

        // UTF-32 (4-byte BOMs)
        if (span.Length >= 4)
        {
            if (span[0] == 0x00 && span[1] == 0x00 && span[2] == 0xFE && span[3] == 0xFF)
            {
                return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
            }

            if (span[0] == 0xFF && span[1] == 0xFE && span[2] == 0x00 && span[3] == 0x00)
            {
                return Encoding.UTF32;
            }
        }

        // UTF-8 (3-byte BOM)
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        // UTF-16 (2-byte BOMs)
        if (span.Length >= 2)
        {
            if (span[0] == 0xFE && span[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode;
            }

            if (span[0] == 0xFF && span[1] == 0xFE)
            {
                return Encoding.Unicode;
            }
        }

        // --------------------------------------------------------------------
        // 2. Check 4-byte / 2-byte Multi-byte Signatures Without BOM
        // --------------------------------------------------------------------

        if (span.Length >= 4)
        {
            // UTF-32 Big-Endian without BOM: "<" is 00 00 00 3C
            if (span[0] == 0x00 && span[1] == 0x00 && span[2] == 0x00 && span[3] == 0x3C)
            {
                return new UTF32Encoding(bigEndian: true, byteOrderMark: false);
            }

            // UTF-32 Little-Endian without BOM: "<" is 3C 00 00 00
            if (span[0] == 0x3C && span[1] == 0x00 && span[2] == 0x00 && span[3] == 0x00)
            {
                return new UTF32Encoding(bigEndian: false, byteOrderMark: false);
            }

            // UTF-16 Big-Endian without BOM: "<?" is 00 3C 00 3F
            if (span[0] == 0x00 && span[1] == 0x3C && span[2] == 0x00 && span[3] == 0x3F)
            {
                return DetectUtf16BigEndian(span);
            }

            // UTF-16 Little-Endian without BOM: "<?" is 3C 00 3F 00
            if (span[0] == 0x3C && span[1] == 0x00 && span[2] == 0x3F && span[3] == 0x00)
            {
                return DetectUtf16LittleEndian(span);
            }

            // EBCDIC (e.g. IBM037): "<?xm" is 4C 6F A7 94
            if (span[0] == 0x4C && span[1] == 0x6F && span[2] == 0xA7 && span[3] == 0x94)
            {
                return DetectEbcdic(span);
            }
        }

        // UTF-16 BE without XML declaration (e.g. "<root")
        if (span.Length >= 2 && span[0] == 0x00 && span[1] == 0x3C)
        {
            return Encoding.BigEndianUnicode;
        }

        // UTF-16 LE without XML declaration (e.g. "<root")
        if (span.Length >= 2 && span[0] == 0x3C && span[1] == 0x00)
        {
            return Encoding.Unicode;
        }

        // --------------------------------------------------------------------
        // 3. ASCII-Compatible Family (UTF-8, ISO-8859-*, Windows-125*, etc.)
        // --------------------------------------------------------------------

        if (span.StartsWith("<?xml"u8))
        {
            if (span.Length == 5 || IsWhitespace(span[5]) || span[5] == (byte)'?')
            {
                var declEnd = span.IndexOf("?>"u8);
                var declSlice = declEnd >= 0 ? span.Slice(0, declEnd) : span;

                if (TryExtractEncoding(declSlice.Slice(5), out var encodingSpan))
                {
                    var encodingName = Encoding.ASCII.GetString(encodingSpan).Trim();
                    if (encodingName.Length > 0)
                    {
                        return ResolveEncoding(encodingName);
                    }
                }
            }
        }

        // Default XML encoding per W3C Recommendation is UTF-8
        return Encoding.UTF8;
    }

    private static Encoding DetectUtf16LittleEndian(ReadOnlySpan<byte> span)
    {
        if (BitConverter.IsLittleEndian)
        {
            var chars = MemoryMarshal.Cast<byte, char>(span);
            if (chars.StartsWith("<?xml", StringComparison.Ordinal))
            {
                if (chars.Length == 5 || char.IsWhiteSpace(chars[5]) || chars[5] == '?')
                {
                    var declEnd = chars.IndexOf("?>", StringComparison.Ordinal);
                    var declSlice = declEnd >= 0 ? chars.Slice(0, declEnd) : chars;

                    if (TryExtractEncoding(declSlice.Slice(5), out var encodingSpan))
                    {
                        var encodingName = encodingSpan.Trim().ToString();
                        if (encodingName.Length > 0)
                        {
                            return ResolveEncoding(encodingName);
                        }
                    }
                }
            }
        }
        else
        {
            var headerLength = Math.Min(span.Length, 2048);
            var header = Encoding.Unicode.GetString(span[..headerLength]);
            if (header.StartsWith("<?xml", StringComparison.Ordinal))
            {
                if (header.Length == 5 || char.IsWhiteSpace(header[5]) || header[5] == '?')
                {
                    var declEnd = header.IndexOf("?>", StringComparison.Ordinal);
                    var declSlice = declEnd >= 0 ? header.AsSpan(0, declEnd) : header.AsSpan();

                    if (TryExtractEncoding(declSlice.Slice(5), out var encodingSpan))
                    {
                        var encodingName = encodingSpan.Trim().ToString();
                        if (encodingName.Length > 0)
                        {
                            return ResolveEncoding(encodingName);
                        }
                    }
                }
            }
        }

        return Encoding.Unicode;
    }

    private static Encoding DetectUtf16BigEndian(ReadOnlySpan<byte> span)
    {
        var headerLength = Math.Min(span.Length, 2048);
        var header = Encoding.BigEndianUnicode.GetString(span[..headerLength]);
        if (header.StartsWith("<?xml", StringComparison.Ordinal))
        {
            if (header.Length == 5 || char.IsWhiteSpace(header[5]) || header[5] == '?')
            {
                var declEnd = header.IndexOf("?>", StringComparison.Ordinal);
                var declSlice = declEnd >= 0 ? header.AsSpan(0, declEnd) : header.AsSpan();

                if (TryExtractEncoding(declSlice.Slice(5), out var encodingSpan))
                {
                    var encodingName = encodingSpan.Trim().ToString();
                    if (encodingName.Length > 0)
                    {
                        return ResolveEncoding(encodingName);
                    }
                }
            }
        }

        return Encoding.BigEndianUnicode;
    }

    private static Encoding DetectEbcdic(ReadOnlySpan<byte> span)
    {
        var ebcdicEncoding = Encoding.GetEncoding(37);
        var headerLength = Math.Min(span.Length, 2048);
        var header = ebcdicEncoding.GetString(span[..headerLength]);
        if (header.StartsWith("<?xml", StringComparison.Ordinal))
        {
            if (header.Length == 5 || char.IsWhiteSpace(header[5]) || header[5] == '?')
            {
                var declEnd = header.IndexOf("?>", StringComparison.Ordinal);
                var declSlice = declEnd >= 0 ? header.AsSpan(0, declEnd) : header.AsSpan();

                if (TryExtractEncoding(declSlice.Slice(5), out var encodingSpan))
                {
                    var encodingName = encodingSpan.Trim().ToString();
                    if (encodingName.Length > 0)
                    {
                        return ResolveEncoding(encodingName);
                    }
                }
            }
        }

        return ebcdicEncoding;
    }

    private static bool TryExtractEncoding(ReadOnlySpan<byte> decl, out ReadOnlySpan<byte> encodingSpan)
    {
        encodingSpan = default;
        var i = 0;

        while (i < decl.Length)
        {
            while (i < decl.Length && IsWhitespace(decl[i]))
            {
                i++;
            }

            if (i >= decl.Length || decl[i] == (byte)'?' || decl[i] == (byte)'>')
            {
                break;
            }

            var nameStart = i;
            while (i < decl.Length && !IsWhitespace(decl[i]) && decl[i] != (byte)'=' && decl[i] != (byte)'?' && decl[i] != (byte)'>')
            {
                i++;
            }

            var name = decl.Slice(nameStart, i - nameStart);

            while (i < decl.Length && IsWhitespace(decl[i]))
            {
                i++;
            }

            if (i >= decl.Length || decl[i] != (byte)'=')
            {
                break;
            }

            i++; // Skip '='

            while (i < decl.Length && IsWhitespace(decl[i]))
            {
                i++;
            }

            if (i >= decl.Length)
            {
                break;
            }

            var quote = decl[i];
            if (quote is not ((byte)'"') and not ((byte)'\''))
            {
                break;
            }

            i++; // Skip quote
            var valueStart = i;
            while (i < decl.Length && decl[i] != quote)
            {
                i++;
            }

            if (i >= decl.Length)
            {
                break;
            }

            var value = decl.Slice(valueStart, i - valueStart);
            i++; // Skip closing quote

            if (Ascii.EqualsIgnoreCase(name, "encoding"u8))
            {
                encodingSpan = value;
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractEncoding(ReadOnlySpan<char> decl, out ReadOnlySpan<char> encodingSpan)
    {
        encodingSpan = default;
        var i = 0;

        while (i < decl.Length)
        {
            while (i < decl.Length && char.IsWhiteSpace(decl[i]))
            {
                i++;
            }

            if (i >= decl.Length || decl[i] == '?' || decl[i] == '>')
            {
                break;
            }

            var nameStart = i;
            while (i < decl.Length && !char.IsWhiteSpace(decl[i]) && decl[i] != '=' && decl[i] != '?' && decl[i] != '>')
            {
                i++;
            }

            var name = decl.Slice(nameStart, i - nameStart);

            while (i < decl.Length && char.IsWhiteSpace(decl[i]))
            {
                i++;
            }

            if (i >= decl.Length || decl[i] != '=')
            {
                break;
            }

            i++; // Skip '='

            while (i < decl.Length && char.IsWhiteSpace(decl[i]))
            {
                i++;
            }

            if (i >= decl.Length)
            {
                break;
            }

            var quote = decl[i];
            if (quote is not '"' and not '\'')
            {
                break;
            }

            i++; // Skip quote
            var valueStart = i;
            while (i < decl.Length && decl[i] != quote)
            {
                i++;
            }

            if (i >= decl.Length)
            {
                break;
            }

            var value = decl.Slice(valueStart, i - valueStart);
            i++; // Skip closing quote

            if (name.Equals("encoding", StringComparison.OrdinalIgnoreCase))
            {
                encodingSpan = value;
                return true;
            }
        }

        return false;
    }

    private static Encoding ResolveEncoding(string name)
    {
        if (string.Equals(name, "utf-8", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "utf8", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8;
        }

        if (string.Equals(name, "utf-16", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "utf16", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "utf-16le", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "ucs-2", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.Unicode;
        }

        if (string.Equals(name, "utf-16be", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.BigEndianUnicode;
        }

        if (string.Equals(name, "utf-32", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "utf32", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "utf-32le", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF32;
        }

        if (string.Equals(name, "utf-32be", StringComparison.OrdinalIgnoreCase))
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: false);
        }

        if (string.Equals(name, "us-ascii", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "ascii", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.ASCII;
        }

        if (string.Equals(name, "iso-8859-1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "latin1", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.Latin1;
        }

        return Encoding.GetEncoding(name);
    }

    private static bool IsWhitespace(byte value)
    {
        return value is
            (byte)' ' or
            (byte)'\t' or
            (byte)'\r' or
            (byte)'\n';
    }
}