namespace CloudMesh.DataBlocks.Streams;

internal sealed class BufferStage<T> : BufferBlock<T>
{
    private readonly ICanSubmit downstream;
    public BufferStage(int maxItems, TimeSpan maxWaitTime, ICanSubmit downstream) : base(maxItems, maxWaitTime)
        => this.downstream = downstream;

    protected override ValueTask FlushAsync(T[] batch) => downstream.SubmitAsync(batch, this);
}