namespace CloudMesh.DataBlocks;

/// <summary>
/// A pipeline stage block that flattens each incoming item into zero or more downstream items by projecting it to
/// a sequence and forwarding every element. Backing block for <c>SelectMany</c>.
/// </summary>
/// <typeparam name="TIn">The incoming item type.</typeparam>
/// <typeparam name="TOut">The flattened element type.</typeparam>
/// <remarks>
/// Each element is submitted individually, so backpressure applies per element. If the selector throws (either
/// building the sequence or while it is being enumerated), the exception is caught and reported to the pipeline's
/// error sink; the whole input item is dropped, though elements already forwarded before the failure remain.
/// </remarks>
public sealed class SelectManyBlock<TIn, TOut> : DataBlock
{
    /// <summary>Creates a select-many (flatten) block.</summary>
    /// <param name="selector">Projects each incoming item to a sequence of downstream elements.</param>
    /// <param name="downstream">The block that receives each flattened element.</param>
    /// <param name="onError">Optional sink invoked with the exception and offending item if the selector throws.</param>
    public SelectManyBlock(Func<TIn, IEnumerable<TOut>> selector, ICanSubmit downstream,
        Action<Exception, object?>? onError = null)
        => ReceiveAsync<TIn>(async x =>
        {
            IEnumerator<TOut> enumerator;
            try
            {
                enumerator = selector(x).GetEnumerator();
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex, x);
                return;
            }

            try
            {
                while (true)
                {
                    TOut element;
                    try
                    {
                        if (!enumerator.MoveNext())
                            break;
                        element = enumerator.Current;
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke(ex, x);
                        return;
                    }

                    await downstream.SubmitAsync(element, this);
                }
            }
            finally
            {
                enumerator.Dispose();
            }
        });
}
