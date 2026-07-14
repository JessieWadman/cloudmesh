namespace CloudMesh.DataBlocks.Streams.FluentApi;

/// <summary>
/// A built, running pipeline. Feed it via <see cref="PushAsync"/> (for a manual-push source) and dispose it to
/// drain and flush every stage. Observe failures via <see cref="Completion"/> or a registered <c>OnError</c> handler.
/// </summary>
/// <typeparam name="TOriginalInput">The type accepted by <see cref="PushAsync"/>.</typeparam>
public interface IPipeline<TOriginalInput> : IAsyncDisposable
{
    /// <summary>
    /// Submits one input item to the head of the pipeline, awaiting mailbox capacity if the pipeline is applying
    /// backpressure. For a <c>From</c>/channel source the source pumps itself, so calling this is unnecessary.
    /// </summary>
    /// <param name="input">The item to push into the pipeline.</param>
    /// <param name="ct">A cancellation token (currently advisory).</param>
    ValueTask PushAsync(TOriginalInput input, CancellationToken ct = default);

    /// <summary>
    /// Completes when the pipeline has drained normally (a manual-push pipeline on <see cref="System.IAsyncDisposable.DisposeAsync"/>;
    /// a <c>From</c>/channel-source pipeline once the source is exhausted and every stage has drained). It
    /// <b>faults</b> with the first unhandled stage failure (a <see cref="PipelineException"/>) when no <c>OnError</c>
    /// handler was registered, so a consumer can <c>await pipeline.Completion</c> to observe failures.
    /// <see cref="System.IAsyncDisposable.DisposeAsync"/> itself never throws pipeline faults; it always drains cleanly.
    /// </summary>
    Task Completion { get; }
}
