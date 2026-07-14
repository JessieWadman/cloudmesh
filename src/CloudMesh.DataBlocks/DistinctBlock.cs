namespace CloudMesh.DataBlocks;

/// <summary>
/// A pipeline stage block that forwards only items it has not seen before, dropping any later duplicate. Backing
/// block for <c>Distinct</c>.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// The set of seen items is per-instance state and grows unbounded for the lifetime of the pipeline, so
/// <c>Distinct</c> is best on streams with a bounded key space. Because a block handles one message at a time, the
/// set needs no locking.
/// </remarks>
public sealed class DistinctBlock<T> : DataBlock
{
    private readonly HashSet<T> seen;

    /// <summary>Creates a distinct block.</summary>
    /// <param name="downstream">The block that receives the first occurrence of each item.</param>
    /// <param name="comparer">The equality comparer used to detect duplicates, or <see langword="null"/> for the default.</param>
    public DistinctBlock(ICanSubmit downstream, IEqualityComparer<T>? comparer = null)
    {
        seen = new HashSet<T>(comparer);
        ReceiveAsync<T>(async x =>
        {
            if (!seen.Add(x))
                return;

            await downstream.SubmitAsync(x, this);
        });
    }
}
