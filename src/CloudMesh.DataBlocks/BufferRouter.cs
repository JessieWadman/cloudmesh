namespace CloudMesh.DataBlocks;

public class BufferRouter<T> : BufferBlock<T>
{
    private readonly ICanSubmit target;

    public BufferRouter(int maxCapacity, TimeSpan maxWaitTimeToFlush, ICanSubmit target)
        : base(maxCapacity, maxWaitTimeToFlush)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
    }

    protected override async ValueTask FlushAsync(T[] messages)
    {
        await target.SubmitAsync(messages, this);
    }
}