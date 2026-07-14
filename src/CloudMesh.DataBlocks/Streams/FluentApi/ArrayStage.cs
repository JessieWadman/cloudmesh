namespace CloudMesh.DataBlocks.Streams.FluentApi;

// A stage whose current message is a batch (TElement[]). Reduce/ReduceAsync are just Map/MapAsync over the array.
internal sealed class ArrayStage<TOriginalInput, TElement>
    : Stage<TOriginalInput, TElement[]>, IArrayPipelineStage<TOriginalInput, TElement, TElement[]>
{
    internal ArrayStage(PipelineDef def) : base(def) { }

    public IPipelineStage<TOriginalInput, R> Reduce<R>(Func<TElement[], R> reducer) => Map(reducer);

    public IPipelineStage<TOriginalInput, R> ReduceAsync<R>(Func<TElement[], CancellationToken, ValueTask<R>> reducer)
        => MapAsync(reducer);
}