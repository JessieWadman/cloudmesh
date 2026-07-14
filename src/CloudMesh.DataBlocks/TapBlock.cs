namespace CloudMesh.DataBlocks;

/// <summary>
/// A pass-through pipeline stage block that runs a side-effecting action on each item and then forwards the
/// (unchanged) item downstream. Backing block for <c>Tap</c>.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// The block awaits the downstream submit, so backpressure propagates upstream. If the action throws, the
/// exception is caught and reported to the pipeline's error sink (the item is dropped and processing continues).
/// </remarks>
public sealed class TapBlock<T> : DataBlock
{
    /// <summary>Creates a tap block.</summary>
    /// <param name="action">The side-effecting action run on each item before it is forwarded.</param>
    /// <param name="downstream">The block that receives each forwarded item.</param>
    /// <param name="onError">Optional sink invoked with the exception and offending item if the action throws.</param>
    public TapBlock(Action<T> action, ICanSubmit downstream, Action<Exception, object?>? onError = null)
        => ReceiveAsync<T>(async x =>
        {
            try
            {
                action(x);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex, x);
                return;
            }

            await downstream.SubmitAsync(x, this);
        });
}
