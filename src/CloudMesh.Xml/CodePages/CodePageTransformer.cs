using System.Buffers;
using System.Text;

namespace CloudMesh.Xml.CodePages;

/// <summary>
/// Provides high-performance, streaming character transcoding between two <see cref="Encoding"/> instances,
/// using intermediate character buffers rented from <see cref="ArrayPool{T}"/> and writing directly to an <see cref="IBufferWriter{Byte}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Instances of <see cref="CodePageTransformer"/> maintain internal encoder and decoder states, allowing chunked
/// or streaming payloads to be transcoded incrementally across multiple <see cref="Transform(ReadOnlySpan{byte}, IBufferWriter{byte}, bool)"/> calls.
/// </para>
/// <para>
/// Always dispose this instance (for example, via a <c>using</c> statement) to return rented memory back to <see cref="ArrayPool{T}"/>.
/// </para>
/// </remarks>
public sealed class CodePageTransformer : IDisposable
{
    private const int CharBufferSize = 4096;

    private readonly Decoder _decoder;
    private readonly Encoder _encoder;

    private char[]? _chars;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodePageTransformer"/> class for transcoding between the specified encodings.
    /// </summary>
    /// <param name="sourceEncoding">The character encoding of the incoming source bytes.</param>
    /// <param name="destinationEncoding">The character encoding of the outgoing transcoded bytes.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sourceEncoding"/> or <paramref name="destinationEncoding"/> is <see langword="null"/>.
    /// </exception>
    public CodePageTransformer(
        Encoding sourceEncoding,
        Encoding destinationEncoding)
    {
        ArgumentNullException.ThrowIfNull(sourceEncoding);
        ArgumentNullException.ThrowIfNull(destinationEncoding);

        _decoder = sourceEncoding.GetDecoder();
        _encoder = destinationEncoding.GetEncoder();

        _chars = ArrayPool<char>.Shared.Rent(CharBufferSize);
    }

    /// <summary>
    /// Transcodes a span of bytes from the source encoding to the destination encoding and writes the result to a buffer writer.
    /// </summary>
    /// <param name="source">The input byte span to transcode.</param>
    /// <param name="destination">The buffer writer to receive the transcoded bytes.</param>
    /// <param name="flush">
    /// <see langword="true"/> to flush internal state and write any remaining shift bytes or trailing characters;
    /// <see langword="false"/> if more chunks will be supplied.
    /// </param>
    /// <exception cref="ObjectDisposedException">The transformer has already been disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The source decoder or destination encoder failed to make progress.</exception>
    public void Transform(
        ReadOnlySpan<byte> source,
        IBufferWriter<byte> destination,
        bool flush = false)
    {
        ObjectDisposedException.ThrowIf(_chars is null, this);
        ArgumentNullException.ThrowIfNull(destination);

        Span<char> chars = _chars;

        while (!source.IsEmpty || flush)
        {
            _decoder.Convert(
                source,
                chars,
                flush,
                out int bytesUsed,
                out int charsUsed,
                out bool decoderCompleted);

            source = source[bytesUsed..];

            Encode(
                chars[..charsUsed],
                destination,
                flush && decoderCompleted);

            if (decoderCompleted)
                break;

            if (bytesUsed == 0 && charsUsed == 0)
                throw new InvalidOperationException(
                    "Source decoder failed to make progress.");
        }
    }

    /// <summary>
    /// Transcodes a sequence of bytes from the source encoding to the destination encoding and writes the result to a buffer writer.
    /// </summary>
    /// <param name="source">The input byte sequence to transcode.</param>
    /// <param name="destination">The buffer writer to receive the transcoded bytes.</param>
    /// <param name="flush">
    /// <see langword="true"/> to flush internal state and write any remaining shift bytes or trailing characters;
    /// <see langword="false"/> if more chunks will be supplied.
    /// </param>
    /// <exception cref="ObjectDisposedException">The transformer has already been disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    public void Transform(
        in ReadOnlySequence<byte> source,
        IBufferWriter<byte> destination,
        bool flush = false)
    {
        foreach (ReadOnlyMemory<byte> segment in source)
            Transform(segment.Span, destination);

        if (flush)
            Transform(ReadOnlySpan<byte>.Empty, destination, flush: true);
    }

    private void Encode(
        ReadOnlySpan<char> chars,
        IBufferWriter<byte> destination,
        bool flush)
    {
        while (!chars.IsEmpty || flush)
        {
            var output = destination.GetSpan(4096);

            _encoder.Convert(
                chars,
                output,
                flush,
                out var charsUsed,
                out var bytesUsed,
                out var completed);

            if (bytesUsed != 0)
                destination.Advance(bytesUsed);

            chars = chars[charsUsed..];

            if (completed)
                return;

            if (charsUsed == 0 && bytesUsed == 0)
                throw new InvalidOperationException(
                    "Destination encoder failed to make progress.");
        }
    }

    /// <summary>
    /// Resets the internal state of the source decoder and destination encoder.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The transformer has already been disposed.</exception>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_chars is null, this);

        _decoder.Reset();
        _encoder.Reset();
    }

    /// <summary>
    /// Releases resources and returns rented character buffers to <see cref="ArrayPool{T}"/>.
    /// </summary>
    public void Dispose()
    {
        var chars = Interlocked.Exchange(ref _chars, null);

        if (chars is not null)
            ArrayPool<char>.Shared.Return(chars);
    }
}