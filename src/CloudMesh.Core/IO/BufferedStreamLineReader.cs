using System.Text;
using CloudMesh.Memory;

namespace CloudMesh.IO;

/// <summary>
/// A high-throughput, low-allocation line reader for large text streams. It reads the stream in big fixed-size
/// chunks and exposes each line as a <see cref="ReadOnlyMemory{T}"/> slice over its reusable buffer, avoiding a
/// string allocation per line. Decode a line to characters on demand via <see cref="GetCurrentLine"/>.
/// </summary>
/// <remarks>
/// The buffers are allocated once and reused, so a single reader can be re-pointed at a new stream with
/// <see cref="Reset"/> without re-allocating. Lines are split on a single byte <c>delimiter</c> (default
/// <c>'\n'</c>). Because <see cref="CurrentLineBytes"/> points into the internal buffer, copy it out (or decode it)
/// before the next <see cref="ReadAsync"/> call if you need to retain it.
/// </remarks>
/// <example>
/// <code>
/// await using var stream = File.OpenRead("large.csv");
/// var reader = new BufferedStreamLineReader(stream);
/// while (await reader.ReadAsync(cancellationToken))
/// {
///     reader.GetCurrentLine(out var chars);
///     Process(chars.Span);
/// }
/// </code>
/// </example>
public class BufferedStreamLineReader
{
    private readonly byte delimiter;
    private const int DefaultReadBufferSize = 16 * 1024 * 1024; // 16 MB
    private const int InitialRemainderBufferSize = 128;

    private Stream? stream;
    private readonly Encoding encoding;
    
    // Statically allocated buffer that holds what was last read from underlying stream
    private readonly Memory<byte> readBuffer;
    // Window over readBuffer that contains the bytes we've not yet examined.
    // Will shrink every time we extract a line from the buffer, until it becomes empty, at which
    // point we'll read in more bytes from disk.
    private ReadOnlyMemory<byte> readBufferToExamine = ReadOnlyMemory<byte>.Empty;
    
    
    // Statically allocated buffer to hold any lingering bytes from the end of readBuffer which
    // was not a whole line when we needed more bytes from disk
    private Memory<byte> remainderFromPreviousReadBuffer;
    // Window over remainingBuffer of the exact bytes stored
    private ReadOnlyMemory<byte> remainderFromPreviousRead = ReadOnlyMemory<byte>.Empty;
    
    
    // Statically allocated buffer to hold decoded characters from byte buffers
    private Memory<char> charBuffer = Memory<char>.Empty;
    
    // Points to the current line within the buffers
    private ReadOnlyMemory<byte> currentLineBytes = Memory<byte>.Empty;

    private bool endOfStream;

    /// <summary>
    /// Creates a reader with pre-allocated buffers but no stream attached. Call <see cref="Reset"/> to point it at
    /// a stream before reading.
    /// </summary>
    /// <param name="encoding">The text encoding used by <see cref="GetCurrentLine"/>. Defaults to UTF-8.</param>
    /// <param name="delimiter">The byte that separates lines. Defaults to <c>'\n'</c>.</param>
    /// <param name="readBufferSize">The size, in bytes, of the chunk read from the stream at a time.</param>
    public BufferedStreamLineReader(Encoding? encoding = null, byte delimiter = (byte)'\n',
        int readBufferSize = DefaultReadBufferSize)
    {
        this.delimiter = delimiter;
        this.encoding = encoding ?? Encoding.UTF8;
        readBuffer = new(GC.AllocateUninitializedArray<byte>(readBufferSize));
        remainderFromPreviousReadBuffer = new(GC.AllocateUninitializedArray<byte>(InitialRemainderBufferSize));
    }
    
    /// <summary>Creates a reader and immediately attaches it to <paramref name="stream"/>.</summary>
    /// <param name="stream">The stream to read lines from.</param>
    /// <param name="encoding">The text encoding used by <see cref="GetCurrentLine"/>. Defaults to UTF-8.</param>
    /// <param name="delimiter">The byte that separates lines. Defaults to <c>'\n'</c>.</param>
    /// <param name="readBufferSize">The size, in bytes, of the chunk read from the stream at a time.</param>
    public BufferedStreamLineReader(Stream stream, Encoding? encoding = null, byte delimiter = (byte)'\n',
        int readBufferSize = DefaultReadBufferSize)
        : this(encoding, delimiter, readBufferSize)
    {
        this.Reset(stream);
    }

    /// <summary>The 1-based number of the line currently exposed by <see cref="CurrentLineBytes"/>.</summary>
    public int CurrentLineNumber { get; private set; }

    /// <summary>
    /// The raw bytes of the current line (without the delimiter), as a slice over the internal buffer. Valid only
    /// until the next <see cref="ReadAsync"/> call; copy or decode it before advancing.
    /// </summary>
    public ReadOnlyMemory<byte> CurrentLineBytes => currentLineBytes;

    /// <summary>
    /// Re-points the reader at a new stream and resets line tracking, reusing the existing buffers.
    /// </summary>
    /// <param name="newStream">The new stream to read from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="newStream"/> is <see langword="null"/>.</exception>
    public void Reset(Stream newStream)
    {
        this.stream = newStream ?? throw new ArgumentNullException(nameof(newStream));
        ShrinkRemainderBuffer();
        readBufferToExamine = ReadOnlyMemory<byte>.Empty;
        CurrentLineNumber = 0;
        endOfStream = false;
    }
    
    /// <summary>
    /// Advances to the next line. On success, <see cref="CurrentLineBytes"/> and <see cref="CurrentLineNumber"/>
    /// reflect the new line.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the underlying stream read.</param>
    /// <returns><see langword="true"/> if a line was read; <see langword="false"/> at end of stream.</returns>
    /// <exception cref="ObjectDisposedException">No stream is attached.</exception>
    public ValueTask<bool> ReadAsync(CancellationToken cancellationToken)
    {
        if (stream == null)
            throw new ObjectDisposedException(nameof(stream));
                
        if (endOfStream && readBufferToExamine.Length == 0)
            return new(false);

        if (TryGetNextLineFromBuffer())
        {
            return new(true);
        }

        return new(ReadMoreAsync(cancellationToken));
    }
    
    private async Task<bool> ReadMoreAsync(CancellationToken cancellationToken)
    {
        var numberOfBytesRead = await stream!.ReadAsync(readBuffer, cancellationToken);
        endOfStream = numberOfBytesRead < readBuffer.Length;
        readBufferToExamine = readBuffer[..numberOfBytesRead];

        // Reached end of stream, and still have remainder?
        if (numberOfBytesRead == 0 && remainderFromPreviousRead.Length > 0)
        {
            endOfStream = true;
            currentLineBytes = remainderFromPreviousRead;
            remainderFromPreviousRead = ReadOnlyMemory<byte>.Empty;
            return true;
        }

        // Use the opportunity when we try to read more from stream and fail to release remainder buffer
        if (numberOfBytesRead < 1)
            ShrinkRemainderBuffer();
        
        return TryGetNextLineFromBuffer();
    }

    private void ConcatenateRemainder(ReadOnlyMemory<byte> readBufferToUse)
    {
        // Will the rest of the line fit in the remainder buffer?
        var concatenatedLineLength = remainderFromPreviousRead.Length + readBufferToUse.Length;
        if (remainderFromPreviousReadBuffer.Length < concatenatedLineLength)
        {
            var bytesPreserved = remainderFromPreviousRead.Length;
            MemoryHelper.GrowBuffer(
                ref remainderFromPreviousReadBuffer, 
                concatenatedLineLength, 
                true);
                
            remainderFromPreviousRead = remainderFromPreviousReadBuffer[..bytesPreserved];
        }

        if (readBufferToUse.Length > 0)
        {
            // Yes, append it after the remainder
            readBufferToUse.CopyTo(remainderFromPreviousReadBuffer[remainderFromPreviousRead.Length..]);
        }

        currentLineBytes = remainderFromPreviousReadBuffer[..concatenatedLineLength];
            
        // We've used up the remainder, so clear it
        remainderFromPreviousRead = ReadOnlyMemory<byte>.Empty;
    }
    
    private bool TryGetNextLineFromBuffer()
    {
        if (stream == null)
            throw new ObjectDisposedException(nameof(stream));

        // Do we have any bytes left in our read buffer? 
        if (readBufferToExamine.Length == 0)
        {
            // No, we reached the end of it and need to read more bytes
            currentLineBytes = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        var indexOfNewLine = readBufferToExamine.Span.IndexOf(delimiter);

        // No new-line character in remainder of bytes read
        if (indexOfNewLine < 0)
        {
            if (endOfStream)
            {
                if (remainderFromPreviousRead.Length > 0)
                    ConcatenateRemainder(readBufferToExamine);
                else
                    currentLineBytes = readBufferToExamine;
                readBufferToExamine = ReadOnlyMemory<byte>.Empty;
                return currentLineBytes.Length > 0;
            }
            
            // Set aside what's left in the read buffer for after next read 
            CopyToRemainder();
            readBufferToExamine = ReadOnlyMemory<byte>.Empty;
            currentLineBytes = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        // -- We have a new line --

        // We have a line, with text in it
        var bytesBeforeNewLine = readBufferToExamine[..indexOfNewLine];

        // Do we still have characters from previous file read we need to prepend?
        if (remainderFromPreviousRead.Length > 0)
        {
            ConcatenateRemainder(bytesBeforeNewLine);
            // Advance read window in our buffer
            readBufferToExamine = readBufferToExamine[(indexOfNewLine + 1)..];
            CurrentLineNumber++;
            return true;
        }
        
        // Is it an empty line?
        if (indexOfNewLine == 0)
        {
            readBufferToExamine = readBufferToExamine[1..];
            currentLineBytes = ReadOnlyMemory<byte>.Empty;
            CurrentLineNumber++;
            return true;
        }

        // No, all bytes are in the bytesRead buffer, return as is
        currentLineBytes = bytesBeforeNewLine;
        readBufferToExamine = readBufferToExamine[(indexOfNewLine + 1)..];
        CurrentLineNumber++;
        return true;
    }
    
    /// <summary>
    /// Decodes the current line's bytes into characters using the reader's encoding, into a reusable char buffer.
    /// </summary>
    /// <param name="chars">Receives the decoded characters as a slice over the internal char buffer, valid until the next call.</param>
    public void GetCurrentLine(out ReadOnlyMemory<char> chars)
    {
        // Repeatedly grow buffer by 8KB to try and accommodate the decoded text from "bytes", until
        // we can fully read the text.
        var estimatedSize = encoding.GetMaxCharCount(currentLineBytes.Length);
        if (estimatedSize < 8192) // Reduce number of allocations by keeping it at least 8KB
            estimatedSize = 8192; // this can be fine-tuned based on expected line lengths.
        
#if NET8_0_OR_GREATER 
        do
        {
            if (encoding.TryGetChars(currentLineBytes.Span, charBuffer.Span, out var written))
            {
                chars = charBuffer[..written];
                return;
            }
            MemoryHelper.GrowBuffer(ref charBuffer, estimatedSize);
        } while (true);
#else
        if (charBuffer.Length < estimatedSize)
            MemoryHelper.GrowBuffer(ref charBuffer, estimatedSize);
        
        var count = encoding.GetChars(currentLineBytes.Span, charBuffer.Span);
        chars = charBuffer[..count];
        return;
#endif
    }
    
    private void ShrinkRemainderBuffer()
    {
        if (remainderFromPreviousReadBuffer.Length > InitialRemainderBufferSize)
            remainderFromPreviousReadBuffer = new(GC.AllocateUninitializedArray<byte>(InitialRemainderBufferSize));
        remainderFromPreviousRead = Memory<byte>.Empty;
    }

    private void CopyToRemainder()
    {
        if (readBufferToExamine.Length == 0 || (readBufferToExamine.Length == 1 && readBufferToExamine.Span[0] == delimiter))
        {
            remainderFromPreviousRead = ReadOnlyMemory<byte>.Empty;
            return;
        }
        
        // Ensure remainder buffer capacity
        if (remainderFromPreviousReadBuffer.Length < readBufferToExamine.Length)
            MemoryHelper.GrowBuffer(
                ref remainderFromPreviousReadBuffer, 
                readBufferToExamine.Length, 
                alignment: 128);
        
        // Copy from read buffer to remainder buffer
        readBufferToExamine.CopyTo(remainderFromPreviousReadBuffer);
        remainderFromPreviousRead = remainderFromPreviousReadBuffer[..readBufferToExamine.Length];
    }
}