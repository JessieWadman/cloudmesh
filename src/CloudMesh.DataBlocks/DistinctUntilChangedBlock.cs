namespace CloudMesh.DataBlocks;

/// <summary>
/// A pipeline stage block that drops <b>consecutive</b> duplicates, forwarding an item only when it differs from
/// the item immediately before it. Backing block for <c>DistinctUntilChanged</c>.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// Only the previous item is retained (constant memory), unlike <see cref="DistinctBlock{T}"/> which remembers
/// every item. Because a block handles one message at a time, the state needs no locking. After a
/// <c>MapAsync(degreeOfParallelism &gt; 1)</c> fan-out, ordering is not preserved, so "consecutive" is defined by
/// arrival order at this block.
/// </remarks>
public sealed class DistinctUntilChangedBlock<T> : DataBlock
{
    private readonly IEqualityComparer<T> comparer;
    private bool hasPrevious;
    private T? previous;

    /// <summary>Creates a distinct-until-changed block.</summary>
    /// <param name="downstream">The block that receives each item that differs from its predecessor.</param>
    /// <param name="comparer">The equality comparer used to compare adjacent items, or <see langword="null"/> for the default.</param>
    public DistinctUntilChangedBlock(ICanSubmit downstream, IEqualityComparer<T>? comparer = null)
    {
        this.comparer = comparer ?? EqualityComparer<T>.Default;
        ReceiveAsync<T>(async x =>
        {
            if (hasPrevious && this.comparer.Equals(previous!, x))
                return;

            hasPrevious = true;
            previous = x;
            await downstream.SubmitAsync(x, this);
        });
    }
}
