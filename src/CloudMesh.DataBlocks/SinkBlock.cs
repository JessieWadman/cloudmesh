namespace CloudMesh.DataBlocks;

/// <summary>
/// A terminal pipeline stage block that runs a consuming action on each item. Backing block for the delegate
/// <c>To(...)</c> sinks.
/// </summary>
/// <typeparam name="T">The item type consumed.</typeparam>
/// <remarks>
/// If the action throws, the exception is caught and reported to the pipeline's error sink (the item is dropped and
/// processing continues), so one bad item does not tear down the pipeline.
/// </remarks>
public sealed class SinkBlock<T> : DataBlock
{
    /// <summary>Creates a sink block.</summary>
    /// <param name="action">The consuming action run on each item.</param>
    /// <param name="onError">Optional sink invoked with the exception and offending item if the action throws.</param>
    public SinkBlock(Func<T, ValueTask> action, Action<Exception, object?>? onError = null)
        => ReceiveAsync<T>(async x =>
        {
            try
            {
                await action(x);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex, x);
            }
        });
}
