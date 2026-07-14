namespace CloudMesh.DataBlocks;

/// <summary>
/// A pipeline stage block that maintains a running accumulator, folding each incoming item into it and emitting
/// the updated accumulator after every item. Backing block for <c>Scan</c> — a per-item running fold, in contrast
/// to the time-windowed <c>Aggregate</c> which emits once per window.
/// </summary>
/// <typeparam name="TIn">The incoming item type.</typeparam>
/// <typeparam name="TAccumulate">The accumulator type emitted downstream.</typeparam>
/// <remarks>
/// The accumulator is per-instance state; because a block handles one message at a time it needs no locking. If the
/// accumulate function throws, the exception is caught and reported to the pipeline's error sink; the item is
/// dropped and the accumulator is left unchanged.
/// </remarks>
public sealed class ScanBlock<TIn, TAccumulate> : DataBlock
{
    private TAccumulate state;

    /// <summary>Creates a scan (running fold) block.</summary>
    /// <param name="seed">The initial accumulator value.</param>
    /// <param name="accumulate">Combines the current accumulator with the next item to produce the new accumulator.</param>
    /// <param name="downstream">The block that receives the accumulator after each item.</param>
    /// <param name="onError">Optional sink invoked with the exception and offending item if the fold throws.</param>
    public ScanBlock(TAccumulate seed, Func<TAccumulate, TIn, TAccumulate> accumulate, ICanSubmit downstream,
        Action<Exception, object?>? onError = null)
    {
        state = seed;
        ReceiveAsync<TIn>(async x =>
        {
            TAccumulate next;
            try
            {
                next = accumulate(state, x);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex, x);
                return;
            }

            state = next;
            await downstream.SubmitAsync(state, this);
        });
    }
}
