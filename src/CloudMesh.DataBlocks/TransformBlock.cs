namespace CloudMesh.DataBlocks;

/// <summary>
/// A pipeline stage block that applies an async selector to each incoming <typeparamref name="TIn"/> and submits
/// the resulting <typeparamref name="TOut"/> to its downstream. Backing block for <c>Map</c>/<c>MapAsync</c>.
/// </summary>
/// <typeparam name="TIn">The incoming item type.</typeparam>
/// <typeparam name="TOut">The transformed item type.</typeparam>
/// <remarks>
/// The block awaits <see cref="ICanSubmit.SubmitAsync{T}"/> downstream, so backpressure propagates upstream. If
/// the selector throws, the exception is caught and reported to the pipeline's error sink (the item is dropped and
/// processing continues); the downstream submit is deliberately kept outside that guard, so a downstream failure is
/// not attributed to this stage's user code.
/// </remarks>
public sealed class TransformBlock<TIn, TOut> : DataBlock
{
    /// <summary>Creates a transform block.</summary>
    /// <param name="selector">The async selector applied to each item.</param>
    /// <param name="downstream">The block that receives each transformed item.</param>
    /// <param name="onError">Optional sink invoked with the exception and offending item if the selector throws.</param>
    public TransformBlock(Func<TIn, CancellationToken, ValueTask<TOut>> selector, ICanSubmit downstream,
        Action<Exception, object?>? onError = null)
        => ReceiveAsync<TIn>(async x =>
        {
            TOut result;
            try
            {
                result = await selector(x, CancellationToken.None);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex, x);
                return;
            }

            await downstream.SubmitAsync(result, this);
        });
}
