using System.Buffers;

namespace CloudMesh.Base32Tests;

// Builds a (possibly multi-segment) ReadOnlySequence<T> so decoders can be exercised across segment
// boundaries that fall in the middle of a 5-bit group.
internal static class SequenceFactory
{
    public static ReadOnlySequence<char> Create(string text, int segmentSize)
        => Create(text.AsMemory(), segmentSize);

    public static ReadOnlySequence<T> Create<T>(ReadOnlyMemory<T> data, int segmentSize)
    {
        if (data.Length <= segmentSize)
            return new ReadOnlySequence<T>(data);

        Segment<T>? first = null;
        Segment<T>? last = null;

        for (var offset = 0; offset < data.Length; offset += segmentSize)
        {
            var length = Math.Min(segmentSize, data.Length - offset);
            var slice = data.Slice(offset, length);
            if (first is null)
            {
                first = new Segment<T>(slice);
                last = first;
            }
            else
            {
                last = last!.Append(slice);
            }
        }

        return new ReadOnlySequence<T>(first!, 0, last!, last!.Memory.Length);
    }

    private sealed class Segment<T> : ReadOnlySequenceSegment<T>
    {
        public Segment(ReadOnlyMemory<T> memory) => Memory = memory;

        public Segment<T> Append(ReadOnlyMemory<T> memory)
        {
            var next = new Segment<T>(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
