using System.Buffers;
using System.Text;
using CloudMesh.Xml.CodePages;

namespace CloudMesh.Xml;

/// <summary>
/// XML reader that detects source character encoding via <see cref="XmlEncodingDetector"/>,
/// conditionally transforms data to UTF-8 via <see cref="CodePageTransformer"/> if needed,
/// and presents the same API shape and semantics as <see cref="Utf8XmlReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// When reading non-UTF-8 XML payloads, <see cref="EncodedXmlReader"/> rents intermediate memory buffers from
/// <see cref="System.Buffers.ArrayPool{T}"/>. Always dispose this instance (for example, via a <c>using</c> statement)
/// to return rented buffers to the pool.
/// </para>
/// <para>
/// All span properties (<see cref="NameSpan"/>, <see cref="ValueSpan"/>, <see cref="RawTokenSpan"/>) and attribute lookup
/// methods present token content as UTF-8 byte spans.
/// </para>
/// </remarks>
public ref struct EncodedXmlReader : IDisposable
{
    private readonly Encoding _encoding;
    private PooledBufferWriter? _pooledWriter;
    private Utf8XmlReader _reader;

    /// <summary>
    /// Initializes a new instance of the <see cref="EncodedXmlReader"/> ref struct from a <see cref="ReadOnlySpan{Byte}"/>.
    /// </summary>
    /// <param name="xml">The XML byte span to read.</param>
    /// <param name="isFinalBlock">
    /// <see langword="true"/> if the input span contains the complete XML document;
    /// <see langword="false"/> if the input is a partial chunk and more data will follow.
    /// </param>
    /// <param name="state">The reader state to resume from, or <see langword="default"/> for the start of a document.</param>
    /// <param name="knownEncoding">
    /// An optional explicit source encoding. If <see langword="null"/>, the encoding is automatically detected via <see cref="XmlEncodingDetector"/>.
    /// </param>
    public EncodedXmlReader(
        ReadOnlySpan<byte> xml,
        bool isFinalBlock = true,
        Utf8XmlReaderState state = default,
        Encoding? knownEncoding = null)
    {
        _encoding = knownEncoding ?? XmlEncodingDetector.Detect(xml);

        if (IsUtf8Encoding(_encoding))
        {
            _pooledWriter = null;
            _reader = new Utf8XmlReader(xml, isFinalBlock, state);
        }
        else
        {
            _pooledWriter = new PooledBufferWriter(Math.Max(256, xml.Length * 2));
            using var transformer = new CodePageTransformer(_encoding, Encoding.UTF8);
            transformer.Transform(xml, _pooledWriter, flush: isFinalBlock);
            _reader = new Utf8XmlReader(_pooledWriter.WrittenSpan, isFinalBlock, state);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EncodedXmlReader"/> ref struct from a <see cref="ReadOnlySequence{Byte}"/>.
    /// </summary>
    /// <param name="xml">The XML byte sequence to read.</param>
    /// <param name="isFinalBlock">
    /// <see langword="true"/> if the sequence contains the complete XML document;
    /// <see langword="false"/> if the sequence is a partial chunk and more data will follow.
    /// </param>
    /// <param name="state">The reader state to resume from, or <see langword="default"/> for the start of a document.</param>
    /// <param name="knownEncoding">
    /// An optional explicit source encoding. If <see langword="null"/>, the encoding is automatically detected via <see cref="XmlEncodingDetector"/>.
    /// </param>
    public EncodedXmlReader(
        in ReadOnlySequence<byte> xml,
        bool isFinalBlock = true,
        Utf8XmlReaderState state = default,
        Encoding? knownEncoding = null)
    {
        _encoding = knownEncoding ?? XmlEncodingDetector.Detect(xml);

        if (IsUtf8Encoding(_encoding))
        {
            if (xml.IsSingleSegment)
            {
                _pooledWriter = null;
                _reader = new Utf8XmlReader(xml.FirstSpan, isFinalBlock, state);
            }
            else
            {
                _pooledWriter = new PooledBufferWriter((int)Math.Min(xml.Length, int.MaxValue));
                var span = _pooledWriter.GetSpan((int)xml.Length);
                xml.CopyTo(span);
                _pooledWriter.Advance((int)xml.Length);
                _reader = new Utf8XmlReader(_pooledWriter.WrittenSpan, isFinalBlock, state);
            }
        }
        else
        {
            _pooledWriter = new PooledBufferWriter(Math.Max(256, (int)Math.Min(xml.Length * 2, int.MaxValue)));
            using var transformer = new CodePageTransformer(_encoding, Encoding.UTF8);
            transformer.Transform(xml, _pooledWriter, flush: isFinalBlock);
            _reader = new Utf8XmlReader(_pooledWriter.WrittenSpan, isFinalBlock, state);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EncodedXmlReader"/> ref struct from a <see cref="ReadOnlyMemory{Byte}"/>.
    /// </summary>
    /// <param name="xml">The XML byte memory to read.</param>
    /// <param name="isFinalBlock">
    /// <see langword="true"/> if the memory contains the complete XML document;
    /// <see langword="false"/> if the memory is a partial chunk and more data will follow.
    /// </param>
    /// <param name="state">The reader state to resume from, or <see langword="default"/> for the start of a document.</param>
    /// <param name="knownEncoding">
    /// An optional explicit source encoding. If <see langword="null"/>, the encoding is automatically detected via <see cref="XmlEncodingDetector"/>.
    /// </param>
    public EncodedXmlReader(
        ReadOnlyMemory<byte> xml,
        bool isFinalBlock = true,
        Utf8XmlReaderState state = default,
        Encoding? knownEncoding = null)
        : this(xml.Span, isFinalBlock, state, knownEncoding)
    {
    }

    /// <summary>
    /// Gets the type of the XML token currently positioned under the reader.
    /// </summary>
    public Utf8XmlTokenType TokenType => _reader.TokenType;

    /// <summary>
    /// Gets the current element nesting depth within the XML document hierarchy.
    /// </summary>
    public int Depth => _reader.Depth;

    /// <summary>
    /// Gets a value indicating whether the current <see cref="Utf8XmlTokenType.StartElement"/> token is an empty self-closing tag (e.g. <c>&lt;item /&gt;</c>).
    /// </summary>
    public bool IsEmptyElement => _reader.IsEmptyElement;

    /// <summary>
    /// Gets the total number of bytes consumed so far from the transcoded UTF-8 buffer.
    /// </summary>
    public long BytesConsumed => _reader.BytesConsumed;

    /// <summary>
    /// Gets the current reader state, which can be captured and passed to a subsequent reader instance.
    /// </summary>
    public Utf8XmlReaderState CurrentState => _reader.CurrentState;

    /// <summary>
    /// Gets the character encoding detected or specified for the XML payload.
    /// </summary>
    public Encoding DetectedEncoding => _encoding;

    /// <summary>
    /// Gets the character encoding detected or specified for the XML payload.
    /// </summary>
    public Encoding Encoding => _encoding;

    /// <summary>
    /// Gets the UTF-8 byte span representing the element name or processing instruction target of the current token.
    /// </summary>
    public ReadOnlySpan<byte> NameSpan => _reader.NameSpan;

    /// <summary>
    /// Gets the UTF-8 byte span representing the text value, CDATA content, comment text, processing instruction data, or DOCTYPE body of the current token.
    /// </summary>
    public ReadOnlySpan<byte> ValueSpan => _reader.ValueSpan;

    /// <summary>
    /// Gets the exact raw UTF-8 byte span comprising the entire current token, including markup delimiters.
    /// </summary>
    public ReadOnlySpan<byte> RawTokenSpan => _reader.RawTokenSpan;

    /// <summary>
    /// Reads the next XML token from the source data.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if a token was successfully read; <see langword="false"/> if the end of the input was reached.
    /// </returns>
    public bool Read() => _reader.Read();

    /// <summary>
    /// Attempts to find an attribute by name on the current <see cref="Utf8XmlTokenType.StartElement"/> without allocating.
    /// </summary>
    /// <param name="utf8Name">The UTF-8 encoded name of the attribute to find.</param>
    /// <param name="value">
    /// When this method returns, contains the raw UTF-8 byte span of the attribute value (excluding surrounding quotes)
    /// if found; otherwise, <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if an attribute with the specified name was found; otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// The returned span points to raw attribute content in the underlying UTF-8 buffer without entity decoding.
    /// </remarks>
    /// <exception cref="InvalidDataException">The XML attribute syntax in the element is malformed.</exception>
    public bool TryGetAttribute(
        ReadOnlySpan<byte> utf8Name,
        out ReadOnlySpan<byte> value) =>
        _reader.TryGetAttribute(utf8Name, out value);

    /// <summary>
    /// Sequentially reads the next attribute from the current <see cref="Utf8XmlTokenType.StartElement"/> without allocating.
    /// </summary>
    /// <param name="position">
    /// An offset tracking the current attribute iteration position. Set to <c>0</c> on the first call to begin reading attributes.
    /// </param>
    /// <param name="name">
    /// When this method returns, contains the UTF-8 byte span of the attribute name if an attribute was read;
    /// otherwise, <see langword="default"/>.
    /// </param>
    /// <param name="value">
    /// When this method returns, contains the raw UTF-8 byte span of the attribute value (excluding surrounding quotes)
    /// if an attribute was read; otherwise, <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if an attribute was successfully read; <see langword="false"/> if there are no more attributes or the reader is not on a <see cref="Utf8XmlTokenType.StartElement"/>.
    /// </returns>
    /// <remarks>
    /// The returned <paramref name="value"/> span points to raw attribute content in the underlying UTF-8 buffer without entity decoding.
    /// </remarks>
    /// <exception cref="InvalidDataException">The XML attribute syntax in the element is malformed.</exception>
    public bool TryReadNextAttribute(
        ref int position,
        out ReadOnlySpan<byte> name,
        out ReadOnlySpan<byte> value) =>
        _reader.TryReadNextAttribute(ref position, out name, out value);

    /// <summary>
    /// Releases any resources and returns rented memory buffers used for transcoding back to <see cref="System.Buffers.ArrayPool{T}"/>.
    /// </summary>
    public void Dispose()
    {
        _pooledWriter?.Dispose();
        _pooledWriter = null;
    }

    private static bool IsUtf8Encoding(Encoding encoding) =>
        encoding.CodePage == Encoding.UTF8.CodePage;

    private sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[]? _rented;
        private int _index;

        public PooledBufferWriter(int initialCapacity = 4096)
        {
            _rented = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 256));
            _index = 0;
        }

        public ReadOnlySpan<byte> WrittenSpan =>
            _rented is not null ? _rented.AsSpan(0, _index) : ReadOnlySpan<byte>.Empty;

        public void Advance(int count)
        {
            if (_rented is null)
                throw new ObjectDisposedException(nameof(PooledBufferWriter));

            if (count < 0 || _index + count > _rented.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            _index += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _rented.AsMemory(_index);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _rented.AsSpan(_index);
        }

        private void EnsureCapacity(int sizeHint)
        {
            if (_rented is null)
                throw new ObjectDisposedException(nameof(PooledBufferWriter));

            var min = Math.Max(1, sizeHint);
            if (_index + min > _rented.Length)
            {
                var newSize = Math.Max(_rented.Length * 2, _index + min);
                var newRented = ArrayPool<byte>.Shared.Rent(newSize);
                _rented.AsSpan(0, _index).CopyTo(newRented);
                ArrayPool<byte>.Shared.Return(_rented);
                _rented = newRented;
            }
        }

        public void Dispose()
        {
            var rented = Interlocked.Exchange(ref _rented, null);
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}