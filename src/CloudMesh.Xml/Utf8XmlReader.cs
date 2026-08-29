namespace CloudMesh.Xml;

// ============================================================================
// Tokens
// ============================================================================

/// <summary>
/// Specifies the type of an XML token encountered by the reader.
/// </summary>
public enum Utf8XmlTokenType : byte
{
    /// <summary>
    /// Indicates that the reader has not yet read a token, or has reached the end of the input.
    /// </summary>
    None,

    /// <summary>
    /// Indicates the start of an XML element tag (for example, <c>&lt;item&gt;</c> or <c>&lt;item /&gt;</c>).
    /// </summary>
    StartElement,

    /// <summary>
    /// Indicates the end of an XML element tag (for example, <c>&lt;/item&gt;</c>).
    /// </summary>
    EndElement,

    /// <summary>
    /// Indicates text content between element tags.
    /// </summary>
    Text,

    /// <summary>
    /// Indicates a CDATA section (for example, <c>&lt;![CDATA[...]]&gt;</c>).
    /// </summary>
    CData,

    /// <summary>
    /// Indicates an XML comment (for example, <c>&lt;!-- ... --&gt;</c>).
    /// </summary>
    Comment,

    /// <summary>
    /// Indicates an XML processing instruction (for example, <c>&lt;?target data?&gt;</c> or <c>&lt;?xml ...?&gt;</c>).
    /// </summary>
    ProcessingInstruction,

    /// <summary>
    /// Indicates a document type declaration (for example, <c>&lt;!DOCTYPE ...&gt;</c>).
    /// </summary>
    DocType
}


// ============================================================================
// Reader state
//
// Same idea as JsonReaderState:
//
// var reader = new Utf8XmlReader(
//     buffer,
//     isFinalBlock,
//     previousState);
//
// while (reader.Read())
// {
//     ...
// }
//
// previousState = reader.CurrentState;
// consumed = reader.BytesConsumed;
// ============================================================================

/// <summary>
/// Provides a store for caching reader state (such as element nesting depth) when reading chunked XML streams.
/// </summary>
public readonly struct Utf8XmlReaderState
{
    internal Utf8XmlReaderState(
        int depth)
    {
        Depth = depth;
    }

    /// <summary>
    /// Gets the element nesting depth at the time the state was captured.
    /// </summary>
    public int Depth { get; }
}


// ============================================================================
// Utf8XmlReader
//
// This is intentionally a tokenizer, not a DOM.
//
// Properties reference the supplied input span and are valid only until the
// reader is discarded / the backing buffer is reused.
//
// Supported:
//
//   <foo>
//   <foo />
//   </foo>
//   attributes
//   text
//   <![CDATA[...]]>
//   <!-- comments -->
//   <?processing instructions?>
//   <!DOCTYPE ...>
//
// XML namespaces are not interpreted. "x:name" is simply the UTF-8 name
// "x:name".
// ============================================================================

/// <summary>
/// Provides a high-performance, forward-only, non-allocating tokenizer for UTF-8 encoded XML data.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Utf8XmlReader"/> operates directly on UTF-8 byte spans without materializing strings or DOM nodes.
/// Span properties (<see cref="NameSpan"/>, <see cref="ValueSpan"/>, <see cref="RawTokenSpan"/>) point directly into the underlying input buffer
/// and remain valid only while that buffer is alive and unmodified.
/// </para>
/// <para>
/// XML namespaces are preserved verbatim as raw UTF-8 prefix-qualified names without namespace resolution.
/// </para>
/// </remarks>
public ref struct Utf8XmlReader
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly bool _isFinalBlock;

    private int _position;
    private int _depth;

    private int _tokenStart;
    private int _tokenEnd;

    private int _nameStart;
    private int _nameLength;

    private int _valueStart;
    private int _valueLength;

    private int _attributesStart;

    private Utf8XmlTokenType _tokenType;
    private bool _isEmptyElement;

    /// <summary>
    /// Initializes a new instance of the <see cref="Utf8XmlReader"/> ref struct over a UTF-8 XML byte span.
    /// </summary>
    /// <param name="utf8Xml">The UTF-8 encoded XML byte span to read.</param>
    /// <param name="isFinalBlock">
    /// <see langword="true"/> if the input span contains the complete XML document;
    /// <see langword="false"/> if the input is a partial chunk and more data will follow.
    /// </param>
    /// <param name="state">The reader state to resume from, or <see langword="default"/> for the start of a document.</param>
    public Utf8XmlReader(
        ReadOnlySpan<byte> utf8Xml,
        bool isFinalBlock = true,
        Utf8XmlReaderState state = default)
    {
        _data = utf8Xml;
        _isFinalBlock = isFinalBlock;

        _position = 0;
        _depth = state.Depth;

        if (_depth == 0 && _data.StartsWith("\uFEFF"u8))
        {
            _position = 3;
        }

        _tokenStart = 0;
        _tokenEnd = 0;

        _nameStart = 0;
        _nameLength = 0;

        _valueStart = 0;
        _valueLength = 0;

        _attributesStart = 0;

        _tokenType = Utf8XmlTokenType.None;
        _isEmptyElement = false;
        Depth = _depth;
    }

    /// <summary>
    /// Gets the type of the XML token currently positioned under the reader.
    /// </summary>
    public Utf8XmlTokenType TokenType =>
        _tokenType;

    /// <summary>
    /// Gets the current element nesting depth within the XML document hierarchy.
    /// </summary>
    public int Depth { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the current <see cref="Utf8XmlTokenType.StartElement"/> token is an empty self-closing tag (for example, <c>&lt;item /&gt;</c>).
    /// </summary>
    public bool IsEmptyElement =>
        _isEmptyElement;

    /// <summary>
    /// Gets the total number of bytes consumed so far from the input span.
    /// </summary>
    public long BytesConsumed =>
        _position;

    /// <summary>
    /// Gets the current reader state, which can be captured and passed to a subsequent reader instance.
    /// </summary>
    public Utf8XmlReaderState CurrentState =>
        new(_depth);

    /// <summary>
    /// Gets the UTF-8 byte span representing the element name or processing instruction target of the current token.
    /// </summary>
    public ReadOnlySpan<byte> NameSpan =>
        _nameLength == 0
            ? default
            : _data.Slice(
                _nameStart,
                _nameLength);

    /// <summary>
    /// Gets the UTF-8 byte span representing the text value, CDATA content, comment text, processing instruction data, or DOCTYPE body of the current token.
    /// </summary>
    public ReadOnlySpan<byte> ValueSpan =>
        _valueLength == 0
            ? default
            : _data.Slice(
                _valueStart,
                _valueLength);

    /// <summary>
    /// Gets the exact raw UTF-8 byte span comprising the entire current token, including markup delimiters.
    /// </summary>
    public ReadOnlySpan<byte> RawTokenSpan =>
        _tokenEnd <= _tokenStart
            ? default
            : _data.Slice(
                _tokenStart,
                _tokenEnd - _tokenStart);

    /// <summary>
    /// Reads the next XML token from the source data.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if a token was successfully read; <see langword="false"/> if the end of the input was reached.
    /// </returns>
    /// <exception cref="InvalidDataException">The XML source data is malformed.</exception>
    public bool Read()
    {
        ResetToken();

        if (_position >= _data.Length)
            return false;

        _tokenStart = _position;

        if (_data[_position] != (byte)'<')
            return ReadText();

        if (StartsWith(
            _position,
            "<!--"u8))
        {
            return ReadComment();
        }

        if (StartsWith(
            _position,
            "<![CDATA["u8))
        {
            return ReadCData();
        }

        if (StartsWith(
            _position,
            "<?"u8))
        {
            return ReadProcessingInstruction();
        }

        if (StartsWith(
            _position,
            "</"u8))
        {
            return ReadEndElement();
        }

        if (StartsWith(
            _position,
            "<!DOCTYPE"u8))
        {
            return ReadDocType();
        }

        if (StartsWith(
            _position,
            "<!"u8))
        {
            if (!_isFinalBlock)
            {
                var remaining = _data.Length - _position;
                if (remaining < 9)
                {
                    var remSpan = _data.Slice(_position);
                    if ("<!--"u8.StartsWith(remSpan) ||
                        "<![CDATA["u8.StartsWith(remSpan) ||
                        "<!DOCTYPE"u8.StartsWith(remSpan))
                    {
                        return false;
                    }
                }
            }

            throw new InvalidDataException(
                "Unsupported XML declaration.");
        }

        return ReadStartElement();
    }


    // ========================================================================
    // Attribute lookup
    //
    // No allocation.
    //
    // Returned value is still XML escaped:
    //
    //      foo="a&amp;b"
    //
    // returns:
    //
    //      a&amp;b
    //
    // The extractor performs entity decoding.
    // ========================================================================

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
    /// The returned span points to raw attribute content in the underlying buffer without entity decoding (e.g. <c>&amp;amp;</c> remains unescaped).
    /// </remarks>
    /// <exception cref="InvalidDataException">The XML attribute syntax in the element is malformed.</exception>
    public bool TryGetAttribute(
        ReadOnlySpan<byte> utf8Name,
        out ReadOnlySpan<byte> value)
    {
        if (_tokenType !=
            Utf8XmlTokenType.StartElement)
        {
            value = default;
            return false;
        }

        var i = _attributesStart;

        while (i < _tokenEnd)
        {
            SkipWhitespace(ref i);

            if (i >= _tokenEnd)
                break;

            var c = _data[i];

            if (c is (byte)'/' or (byte)'>')
                break;

            var nameStart = i;

            while (i < _tokenEnd)
            {
                c = _data[i];

                if (IsWhitespace(c) ||
                    c == (byte)'=' ||
                    c == (byte)'/' ||
                    c == (byte)'>')
                {
                    break;
                }

                i++;
            }

            if (i == nameStart)
            {
                throw new InvalidDataException(
                    "Malformed XML attribute.");
            }

            var name =
                _data.Slice(
                    nameStart,
                    i - nameStart);

            SkipWhitespace(ref i);

            if (i >= _tokenEnd ||
                _data[i] != (byte)'=')
            {
                throw new InvalidDataException(
                    "Malformed XML attribute.");
            }

            i++;

            SkipWhitespace(ref i);

            if (i >= _tokenEnd)
            {
                value = default;
                return false;
            }

            var quote = _data[i];

            if (quote is not
                ((byte)'"') and not ((byte)'\''))
            {
                throw new InvalidDataException(
                    "XML attribute value must be quoted.");
            }

            i++;

            var valueStart = i;

            while (i < _tokenEnd &&
                   _data[i] != quote)
            {
                i++;
            }

            if (i >= _tokenEnd)
            {
                value = default;
                return false;
            }

            var attrValue =
                _data.Slice(
                    valueStart,
                    i - valueStart);

            i++;

            if (name.SequenceEqual(utf8Name))
            {
                value = attrValue;
                return true;
            }
        }

        value = default;
        return false;
    }

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
    /// The returned <paramref name="value"/> span points to raw attribute content in the underlying buffer without entity decoding.
    /// </remarks>
    /// <exception cref="InvalidDataException">The XML attribute syntax in the element is malformed.</exception>
    public bool TryReadNextAttribute(
        ref int position,
        out ReadOnlySpan<byte> name,
        out ReadOnlySpan<byte> value)
    {
        if (_tokenType != Utf8XmlTokenType.StartElement)
        {
            name = default;
            value = default;
            return false;
        }

        if (position == 0)
        {
            position = _attributesStart;
        }
        else if (position < _attributesStart || position >= _tokenEnd)
        {
            name = default;
            value = default;
            return false;
        }

        while (position < _tokenEnd)
        {
            SkipWhitespace(ref position);

            if (position >= _tokenEnd)
                break;

            var c = _data[position];

            if (c is (byte)'/' or (byte)'>')
                break;

            var nameStart = position;

            while (position < _tokenEnd)
            {
                c = _data[position];

                if (IsWhitespace(c) ||
                    c == (byte)'=' ||
                    c == (byte)'/' ||
                    c == (byte)'>')
                {
                    break;
                }

                position++;
            }

            if (position == nameStart)
            {
                throw new InvalidDataException(
                    "Malformed XML attribute.");
            }

            name = _data.Slice(nameStart, position - nameStart);

            SkipWhitespace(ref position);

            if (position >= _tokenEnd ||
                _data[position] != (byte)'=')
            {
                throw new InvalidDataException(
                    "Malformed XML attribute.");
            }

            position++;

            SkipWhitespace(ref position);

            if (position >= _tokenEnd)
            {
                name = default;
                value = default;
                return false;
            }

            var quote = _data[position];

            if (quote is not
                ((byte)'"') and not ((byte)'\''))
            {
                throw new InvalidDataException(
                    "XML attribute value must be quoted.");
            }

            position++;

            var valueStart = position;

            while (position < _tokenEnd &&
                   _data[position] != quote)
            {
                position++;
            }

            if (position >= _tokenEnd)
            {
                name = default;
                value = default;
                return false;
            }

            value = _data.Slice(valueStart, position - valueStart);
            position++;
            return true;
        }

        name = default;
        value = default;
        return false;
    }


    // ========================================================================
    // Text
    // ========================================================================

    private bool ReadText()
    {
        var remaining =
            _data[_position..];

        var relative =
            remaining.IndexOf((byte)'<');

        if (relative < 0)
        {
            if (!_isFinalBlock)
                return false;

            relative =
                remaining.Length;
        }

        if (relative == 0)
            return false;

        _valueStart =
            _position;

        _valueLength =
            relative;

        _position +=
            relative;

        _tokenEnd =
            _position;

        _tokenType =
            Utf8XmlTokenType.Text;

        Depth = _depth;

        return true;
    }


    // ========================================================================
    // Start element
    // ========================================================================

    private bool ReadStartElement()
    {
        var end =
            FindTagEnd(
                _position + 1);

        if (end < 0)
        {
            if (_isFinalBlock)
            {
                throw new InvalidDataException(
                    "Incomplete XML start element.");
            }

            return false;
        }

        var i =
            _position + 1;

        if (i >= end)
            throw new InvalidDataException(
                "Empty XML element name.");

        var nameStart =
            i;

        while (i < end)
        {
            var c = _data[i];

            if (IsWhitespace(c) ||
                c == (byte)'/' ||
                c == (byte)'>')
            {
                break;
            }

            i++;
        }

        if (i == nameStart)
            throw new InvalidDataException(
                "Empty XML element name.");

        _nameStart =
            nameStart;

        _nameLength =
            i - nameStart;

        _attributesStart =
            i;

        var tail =
            end - 1;

        while (tail > _position &&
               IsWhitespace(_data[tail]))
        {
            tail--;
        }

        _isEmptyElement =
            _data[tail] == (byte)'/';

        _tokenType =
            Utf8XmlTokenType.StartElement;

        _tokenEnd =
            end + 1;

        _position =
            _tokenEnd;

        Depth =
            _depth;

        if (!_isEmptyElement)
            _depth++;

        return true;
    }


    // ========================================================================
    // End element
    // ========================================================================

    private bool ReadEndElement()
    {
        var end =
            _data[_position..]
                .IndexOf((byte)'>');

        if (end < 0)
        {
            if (_isFinalBlock)
            {
                throw new InvalidDataException(
                    "Incomplete XML end element.");
            }

            return false;
        }

        end +=
            _position;

        var i =
            _position + 2;

        while (i < end &&
               IsWhitespace(_data[i]))
        {
            i++;
        }

        var nameStart =
            i;

        while (i < end &&
               !IsWhitespace(_data[i]) &&
               _data[i] != (byte)'>' &&
               _data[i] != (byte)'/')
        {
            i++;
        }

        if (i == nameStart)
        {
            throw new InvalidDataException(
                "Empty XML end element name.");
        }

        _nameStart =
            nameStart;

        _nameLength =
            i - nameStart;

        while (i < end &&
               IsWhitespace(_data[i]))
        {
            i++;
        }

        if (i != end)
        {
            throw new InvalidDataException(
                "Malformed XML end element.");
        }

        if (_depth == 0)
        {
            throw new InvalidDataException(
                "Unexpected XML end element.");
        }

        _depth--;

        Depth =
            _depth;

        _tokenType =
            Utf8XmlTokenType.EndElement;

        _tokenEnd =
            end + 1;

        _position =
            _tokenEnd;

        return true;
    }


    // ========================================================================
    // DOCTYPE
    // ========================================================================

    private bool ReadDocType()
    {
        const int prefixLength = 9; // "<!DOCTYPE".Length
        byte quote = 0;
        var bracketDepth = 0;

        for (var i = _position + prefixLength; i < _data.Length; i++)
        {
            var c = _data[i];

            if (quote != 0)
            {
                if (c == quote)
                {
                    quote = 0;
                }
                continue;
            }

            if (c is (byte)'"' or (byte)'\'')
            {
                quote = c;
                continue;
            }

            if (c == (byte)'[')
            {
                bracketDepth++;
                continue;
            }

            if (c == (byte)']')
            {
                if (bracketDepth > 0)
                    bracketDepth--;
                continue;
            }

            if (c == (byte)'>' && bracketDepth == 0)
            {
                _tokenEnd = i + 1;
                _position = _tokenEnd;
                _tokenType = Utf8XmlTokenType.DocType;
                Depth = _depth;
                return true;
            }
        }

        if (_isFinalBlock)
        {
            throw new InvalidDataException(
                "Incomplete DOCTYPE declaration.");
        }

        return false;
    }


    // ========================================================================
    // CDATA
    // ========================================================================

    private bool ReadCData()
    {
        const int prefixLength = 9;

        var bodyStart =
            _position + prefixLength;

        var relative =
            _data[bodyStart..]
                .IndexOf("]]>"u8);

        if (relative < 0)
        {
            if (_isFinalBlock)
            {
                throw new InvalidDataException(
                    "Incomplete CDATA section.");
            }

            return false;
        }

        _valueStart =
            bodyStart;

        _valueLength =
            relative;

        _tokenEnd =
            bodyStart +
            relative +
            3;

        _position =
            _tokenEnd;

        _tokenType =
            Utf8XmlTokenType.CData;

        Depth =
            _depth;

        return true;
    }


    // ========================================================================
    // Comment
    // ========================================================================

    private bool ReadComment()
    {
        const int prefixLength = 4;

        var bodyStart =
            _position + prefixLength;

        var relative =
            _data[bodyStart..]
                .IndexOf("-->"u8);

        if (relative < 0)
        {
            if (_isFinalBlock)
            {
                throw new InvalidDataException(
                    "Incomplete XML comment.");
            }

            return false;
        }

        _valueStart =
            bodyStart;

        _valueLength =
            relative;

        _tokenEnd =
            bodyStart +
            relative +
            3;

        _position =
            _tokenEnd;

        _tokenType =
            Utf8XmlTokenType.Comment;

        Depth =
            _depth;

        return true;
    }


    // ========================================================================
    // Processing instruction
    // ========================================================================

    private bool ReadProcessingInstruction()
    {
        var bodyStart =
            _position + 2;

        var relative =
            _data[bodyStart..]
                .IndexOf("?>"u8);

        if (relative < 0)
        {
            if (_isFinalBlock)
            {
                throw new InvalidDataException(
                    "Incomplete XML processing instruction.");
            }

            return false;
        }

        _valueStart =
            bodyStart;

        _valueLength =
            relative;

        _tokenEnd =
            bodyStart +
            relative +
            2;

        _position =
            _tokenEnd;

        _tokenType =
            Utf8XmlTokenType.ProcessingInstruction;

        Depth =
            _depth;

        return true;
    }


    // ========================================================================
    // Helpers
    // ========================================================================

    private int FindTagEnd(
        int start)
    {
        byte quote = 0;

        for (var i = start;
             i < _data.Length;
             i++)
        {
            var c =
                _data[i];

            if (quote != 0)
            {
                if (c == quote)
                    quote = 0;

                continue;
            }

            if (c is
                ((byte)'"') or ((byte)'\''))
            {
                quote = c;
                continue;
            }

            if (c == (byte)'>')
                return i;
        }

        return -1;
    }

    private void SkipWhitespace(
        ref int position)
    {
        while (position < _tokenEnd &&
               IsWhitespace(_data[position]))
        {
            position++;
        }
    }

    private bool StartsWith(
        int position,
        ReadOnlySpan<byte> value)
    {
        var remaining =
            _data.Length - position;

        if (remaining < value.Length)
            return false;

        return _data
            .Slice(
                position,
                value.Length)
            .SequenceEqual(value);
    }

    private static bool IsWhitespace(
        byte value)
    {
        return value is
            (byte)' ' or
            (byte)'\t' or
            (byte)'\r' or
            (byte)'\n';
    }

    private void ResetToken()
    {
        _tokenType =
            Utf8XmlTokenType.None;

        _tokenStart =
            _position;

        _tokenEnd =
            _position;

        _nameStart =
            0;

        _nameLength =
            0;

        _valueStart =
            0;

        _valueLength =
            0;

        _attributesStart =
            0;

        _isEmptyElement =
            false;

        Depth =
            _depth;
    }
}