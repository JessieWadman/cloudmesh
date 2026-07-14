namespace CloudMesh.DataBlocks.Streams;

/// <summary>
/// The shared error observation point for a running pipeline. Every stage block routes a caught user-code
/// exception here (via <see cref="Report"/>); the sink decides what happens next based on whether a resilient
/// <c>OnError</c> handler was registered:
/// <list type="bullet">
/// <item><description>
/// If an <c>OnError</c> handler was supplied, it is invoked with the (wrapped) exception and the offending item;
/// the item is dropped and the pipeline keeps running. Completion still finishes successfully.
/// </description></item>
/// <item><description>
/// If no handler was supplied, the <b>first</b> fault is captured and used to fault
/// <see cref="Completion"/> when the pipeline drains; the item is dropped and processing continues so the
/// pipeline can still be disposed cleanly.
/// </description></item>
/// </list>
/// </summary>
internal sealed class PipelineErrorSink
{
    private readonly Action<Exception, object?>? errorHandler;
    private readonly TaskCompletionSource completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object gate = new();
    private Exception? firstFault;

    public PipelineErrorSink(Action<Exception, object?>? errorHandler)
        => this.errorHandler = errorHandler;

    /// <summary>
    /// A task that completes successfully when the pipeline drains normally, or faults with the first unhandled
    /// stage failure when no <c>OnError</c> handler was registered.
    /// </summary>
    public Task Completion => completion.Task;

    /// <summary>
    /// Reports a user-code failure from a stage. Wraps it in a <see cref="PipelineException"/> (preserving the
    /// offending item), then either forwards it to the resilient handler or records it as the first fault.
    /// </summary>
    /// <param name="error">The original exception thrown by the user delegate.</param>
    /// <param name="item">The item being processed when the failure occurred.</param>
    public void Report(Exception error, object? item)
    {
        var wrapped = error as PipelineException ?? new PipelineException(item, error);

        if (errorHandler is not null)
        {
            // Resilient path: never let the user's own handler bring down the stage or the sink.
            try
            {
                errorHandler(wrapped, item);
            }
            catch
            {
            }
            return;
        }

        // No handler: capture only the first fault so a consumer can observe it via Completion.
        lock (gate)
        {
            firstFault ??= wrapped;
        }
    }

    /// <summary>
    /// Marks the pipeline as drained. Faults <see cref="Completion"/> with the first recorded fault, or completes
    /// it successfully if there were none. Idempotent.
    /// </summary>
    public void Complete()
    {
        Exception? fault;
        lock (gate)
        {
            fault = firstFault;
        }

        if (fault is not null)
            completion.TrySetException(fault);
        else
            completion.TrySetResult();
    }
}
