namespace CloudMesh.DataBlocks;

/// <summary>
/// A pipeline stage block that drops the first <c>count</c> items it sees and forwards every item thereafter.
/// Backing block for <c>Skip</c>.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// The count is per-instance state; because a block handles one message at a time it needs no locking. After a
/// <c>MapAsync(degreeOfParallelism &gt; 1)</c> fan-out, ordering is not preserved, so "first N" is defined by
/// arrival order at this block, not the original source order.
/// </remarks>
public sealed class SkipBlock<T> : DataBlock
{
    private int remaining;

    /// <summary>Creates a skip block.</summary>
    /// <param name="count">The number of leading items to drop.</param>
    /// <param name="downstream">The block that receives the forwarded items.</param>
    public SkipBlock(int count, ICanSubmit downstream)
    {
        remaining = count;
        ReceiveAsync<T>(async x =>
        {
            if (remaining > 0)
            {
                remaining--;
                return;
            }

            await downstream.SubmitAsync(x, this);
        });
    }
}
