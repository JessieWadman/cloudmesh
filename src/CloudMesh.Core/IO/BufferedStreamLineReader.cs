using System.Text;
using CloudMesh.Memory;

namespace CloudMesh.IO;

/// <summary>
/// Optimized, statically allocated, buffered stream reader for text files than can read one line of text at a time 
/// </summary>
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

    public BufferedStreamLineReader(Encoding? encoding = null, byte delimiter = (byte)'\n', 
        int readBufferSize = DefaultReadBufferSize)
    {
        this.delimiter = delimiter;
        this.encoding = encoding ?? Encoding.UTF8;
        readBuffer = new(GC.AllocateUninitializedArray<byte>(readBufferSize));
        remainderFromPreviousReadBuffer = new(GC.AllocateUninitializedArray<byte>(InitialRemainderBufferSize));
    }
    
    public BufferedStreamLineReader(Stream stream, Encoding? encoding = null, byte delimiter = (byte)'\n', 
        int readBufferSize = DefaultReadBufferSize)
        : this(encoding, delimiter, readBufferSize)
    {
        this.Reset(stream);
    }
    
    public int CurrentLineNumber { get; private set; }
    public ReadOnlyMemory<byte> CurrentLineBytes => currentLineBytes;

    public void Reset(Stream newStream)
    {
        this.stream = newStream ?? throw new ArgumentNullException(nameof(newStream));
        ShrinkRemainderBuffer();
        readBufferToExamine = ReadOnlyMemory<byte>.Empty;
        CurrentLineNumber = 0;
        endOfStream = false;
    }
    
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