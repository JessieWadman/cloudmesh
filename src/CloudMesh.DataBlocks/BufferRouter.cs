namespace CloudMesh.DataBlocks;

/// <summary>
/// A ready-made <see cref="BufferBlock{T}"/> that forwards each flushed batch (a <typeparamref name="T"/><c>[]</c>)
/// to a downstream <see cref="ICanSubmit"/> target. Use it to turn a stream of single messages into batched sends,
/// with no subclassing required.
/// </summary>
/// <typeparam name="T">The message type to buffer and forward.</typeparam>
public class BufferRouter<T> : BufferBlock<T>
{
    private readonly ICanSubmit target;

    /// <summary>Creates a buffer that forwards batches to <paramref name="target"/>.</summary>
    /// <param name="maxCapacity">Maximum batch size before an immediate flush.</param>
    /// <param name="maxWaitTimeToFlush">Maximum time to hold a partial batch before flushing.</param>
    /// <param name="target">The downstream block that receives each batch as an array.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    public BufferRouter(int maxCapacity, TimeSpan maxWaitTimeToFlush, ICanSubmit target)
        : base(maxCapacity, maxWaitTimeToFlush)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <inheritdoc/>
    protected override async ValueTask FlushAsync(T[] messages)
    {
        await target.SubmitAsync(messages, this);
    }
}