using System.Threading.Channels;

namespace CloudMesh.DataBlocks;

/// <summary>
/// A terminal pipeline stage block that writes each item to a <see cref="ChannelWriter{T}"/>, and completes the
/// writer when the pipeline drains (on shutdown), so a downstream channel consumer's
/// <see cref="ChannelReader{T}.ReadAllAsync(System.Threading.CancellationToken)"/> loop terminates. Backing block
/// for the <c>To(ChannelWriter&lt;T&gt;)</c> sink.
/// </summary>
/// <typeparam name="T">The item type written to the channel.</typeparam>
/// <remarks>
/// The block awaits <see cref="ChannelWriter{T}.WriteAsync(T, System.Threading.CancellationToken)"/>, so a bounded
/// channel applies backpressure upstream through the pipeline. The writer is completed exactly once, in
/// <c>AfterStop</c>, after the mailbox has drained.
/// </remarks>
public sealed class ChannelSinkBlock<T> : DataBlock
{
    private readonly ChannelWriter<T> writer;

    /// <summary>Creates a channel sink block.</summary>
    /// <param name="writer">The channel writer each item is written to; completed when the pipeline drains.</param>
    public ChannelSinkBlock(ChannelWriter<T> writer)
    {
        this.writer = writer;
        ReceiveAsync<T>(async x => await writer.WriteAsync(x));
    }

    /// <inheritdoc/>
    protected override ValueTask AfterStop()
    {
        writer.TryComplete();
        return TaskHelper.CompletedTask;
    }
}
