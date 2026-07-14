namespace CloudMesh.DataBlocks;

/// <summary>
/// A pipeline stage block that forwards only the items for which a predicate returns <see langword="true"/>.
/// Backing block for <c>Where</c>.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// The block awaits the downstream submit, so backpressure propagates upstream. If the predicate throws, the
/// exception is caught and reported to the pipeline's error sink (the item is dropped and processing continues).
/// </remarks>
public sealed class FilterBlock<T> : DataBlock
{
    /// <summary>Creates a filter block.</summary>
    /// <param name="predicate">The predicate deciding whether to forward each item.</param>
    /// <param name="downstream">The block that receives forwarded items.</param>
    /// <param name="onError">Optional sink invoked with the exception and offending item if the predicate throws.</param>
    public FilterBlock(Func<T, bool> predicate, ICanSubmit downstream, Action<Exception, object?>? onError = null)
        => ReceiveAsync<T>(async x =>
        {
            bool keep;
            try
            {
                keep = predicate(x);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex, x);
                return;
            }

            if (keep)
                await downstream.SubmitAsync(x, this);
        });
}
