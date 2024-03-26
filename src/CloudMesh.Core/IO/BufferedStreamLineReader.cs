#if (NET8_0_OR_GREATER)
using System.Text;

namespace CloudMesh.IO;

/// <summary>
/// Optimized, statically allocated, buffered stream reader for text files than can read one line at a time 
/// </summary>
public class BufferedStreamLineReader
{
    private readonly byte delimiter;
    private const int DefaultReadBufferSize = 16 * 1024 * 1024; // 16 MB
    private const int DefaultInitialRemainderBufferSize = 1024; // 1 KB

    private readonly int initialRemainderBufferSize;
    
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

    public BufferedStreamLineReader(
        Encoding? encoding = null, 
        byte delimiter = (byte)'\n',
        int readBufferSize = DefaultReadBufferSize,
        int initialRemainderBufferSize = DefaultInitialRemainderBufferSize)
    {
        this.delimiter = delimiter;
        this.initialRemainderBufferSize = initialRemainderBufferSize;
        this.encoding = encoding ?? Encoding.UTF8;
        readBuffer = new(GC.AllocateUninitializedArray<byte>(readBufferSize));
        remainderFromPreviousReadBuffer = new(GC.AllocateUninitializedArray<byte>(this.initialRemainderBufferSize));
    }
    
    public int CurrentLineNumber { get; private set; }

    private void ShrinkRemainderBuffer()
    {
        if (remainderFromPreviousReadBuffer.Length > initialRemainderBufferSize)
            remainderFromPreviousReadBuffer = new(GC.AllocateUninitializedArray<byte>(initialRemainderBufferSize));
        remainderFromPreviousRead = Memory<byte>.Empty;
    }

    // Allows reuse of this instance and its statically allocated memory for processing next file 
    public void Reset(Stream newStream)
    {
        this.stream = newStream ?? throw new ArgumentNullException(nameof(newStream));
        ShrinkRemainderBuffer();
        readBufferToExamine = ReadOnlyMemory<byte>.Empty;
        CurrentLineNumber = 0;
    }

    public async Task<bool> TryReadMoreAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(stream == null, this);
        
        var numberOfBytesRead = await stream.ReadAsync(readBuffer, cancellationToken);
        readBufferToExamine = readBuffer[..numberOfBytesRead];
        
        // Use the opportunity when we try to read more from stream and fail to release remainder buffer
        if (numberOfBytesRead < 1)
            ShrinkRemainderBuffer();
        
        return numberOfBytesRead > 0;
    }

    private static void GrowBuffer<T>(ref Memory<T> buffer, int minSize, bool preserveContents)
    {
        var size = minSize;
        if (size < buffer.Length)
            size = buffer.Length;
        
        // Align to next 8KB boundary to reduce heap fragmentation
        size = (size + 8191) & ~8192;
        
        if (size < minSize)
            size += 8192;
        
        var temp = new Memory<T>(GC.AllocateUninitializedArray<T>(size));

        if (preserveContents)
        {
            buffer.CopyTo(temp);
        }

        buffer = temp;
    }
    
    private void CopyToRemainder()
    {
        // Ensure remainder buffer capacity
        if (remainderFromPreviousReadBuffer.Length < readBufferToExamine.Length)
            GrowBuffer(ref remainderFromPreviousReadBuffer, readBufferToExamine.Length, false);
        
        // Copy from read buffer to remainder buffer
        readBufferToExamine.CopyTo(remainderFromPreviousReadBuffer);
        remainderFromPreviousRead = remainderFromPreviousReadBuffer[..readBufferToExamine.Length];
    }

    private void ConvertToText(ReadOnlySpan<byte> bytes, out ReadOnlyMemory<char> chars)
    {
        // Repeatedly grow buffer by 8KB to try and accommodate the decoded text from "bytes", until
        // we either can fully read the text.
        var estimatedSize = encoding.GetMaxCharCount(bytes.Length);
        if (estimatedSize < 8192) // Reduce number of allocations by keeping it at least 8KB
            estimatedSize = 8192; // this can be fine-tuned based on expected line lengths.
        
        do
        {
            if (encoding.TryGetChars(bytes, charBuffer.Span, out var written))
            {
                chars = charBuffer[..written];
                return;
            }
            
            GrowBuffer(ref charBuffer, estimatedSize, false);
        } while (true);
    }

    public bool TryGetNextLine(out ReadOnlyMemory<char> line)
    {
        ObjectDisposedException.ThrowIf(stream == null, this);
        
        // Do we have any bytes left in our read buffer? 
        if (readBufferToExamine.Length == 0)
        {
            // No, we reached the end of it and need to read more bytes
            line = ReadOnlyMemory<char>.Empty;
            return false;
        }

        var indexOfNewLine = readBufferToExamine.Span.IndexOf(delimiter);

        // No new-line character in remainder of bytes read
        if (indexOfNewLine < 0)
        {
            // Set aside what's left in the read buffer for after next read 
            CopyToRemainder();
            readBufferToExamine = ReadOnlyMemory<byte>.Empty;
            line = ReadOnlyMemory<char>.Empty;
            return false;
        }

        // -- We have a new line --

        // Is it an empty line?
        if (indexOfNewLine == 0)
        {
            readBufferToExamine = readBufferToExamine[1..];
            line = ReadOnlyMemory<char>.Empty;
            CurrentLineNumber++;
            return true;
        }

        // We have a line, with text in it
        var bytesBeforeNewLine = readBufferToExamine[..indexOfNewLine];

        // Do we still have characters from previous file read we need to prepend?
        if (remainderFromPreviousRead.Length > 0)
        {
            // Will the rest of the line fit in the remainder buffer?
            var concatenatedLineLength = remainderFromPreviousRead.Length + bytesBeforeNewLine.Length;
            if (remainderFromPreviousReadBuffer.Length < concatenatedLineLength)
            {
                var bytesPreserved = remainderFromPreviousRead.Length;
                GrowBuffer(ref remainderFromPreviousReadBuffer, concatenatedLineLength, true);
                remainderFromPreviousRead = remainderFromPreviousReadBuffer[..bytesPreserved];
            }

            // Yes, append it after the remainder
            bytesBeforeNewLine.CopyTo(remainderFromPreviousReadBuffer[remainderFromPreviousRead.Length..]);
            ConvertToText(remainderFromPreviousReadBuffer[..concatenatedLineLength].Span, out line);
            
            // We've used up the remainder, so clear it
            remainderFromPreviousRead = ReadOnlyMemory<byte>.Empty;
            // Advance read window in our buffer
            readBufferToExamine = readBufferToExamine[(indexOfNewLine + 1)..];
            CurrentLineNumber++;
            return true;
        }

        // No, all bytes are in the bytesRead buffer, return as is
        ConvertToText(bytesBeforeNewLine.Span, out line);
        readBufferToExamine = readBufferToExamine[(indexOfNewLine + 1)..];
        CurrentLineNumber++;
        return true;
    }
}
#endif