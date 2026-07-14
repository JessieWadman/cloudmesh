namespace CloudMesh.DataBlocks;

/// <summary>
/// A pipeline stage block that forwards the first <c>count</c> items it sees and drops every item thereafter.
/// Backing block for <c>Take</c>.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// The remaining count is per-instance state; because a block handles one message at a time it needs no locking.
/// After a <c>MapAsync(degreeOfParallelism &gt; 1)</c> fan-out, ordering is not preserved, so "first N" is defined
/// by arrival order at this block, not the original source order.
/// </remarks>
public sealed class TakeBlock<T> : DataBlock
{
    private int remaining;

    /// <summary>Creates a take block.</summary>
    /// <param name="count">The number of leading items to forward.</param>
    /// <param name="downstream">The block that receives the forwarded items.</param>
    public TakeBlock(int count, ICanSubmit downstream)
    {
        remaining = count;
        ReceiveAsync<T>(async x =>
        {
            if (remaining <= 0)
                return;

            remaining--;
            await downstream.SubmitAsync(x, this);
        });
    }
}
