namespace CloudMesh.DataBlocks.Streams.FluentApi;

/// <summary>A stage whose messages are batches (arrays); adds <c>Reduce</c> to collapse each batch to one value.</summary>
/// <typeparam name="TOriginalInput">The pipeline's original input type.</typeparam>
/// <typeparam name="TCurrent">The element type within each batch.</typeparam>
/// <typeparam name="TArray">The batch type flowing through the stage (an <see cref="System.Collections.Generic.IEnumerable{T}"/> of <typeparamref name="TCurrent"/>).</typeparam>
public interface IArrayPipelineStage<TOriginalInput, TCurrent, TArray> : IPipelineStage<TOriginalInput, TArray>
    where TArray : IEnumerable<TCurrent>
{
    /// <summary>Collapses each batch to a single value with a synchronous reducer.</summary>
    /// <typeparam name="R">The reduced result type.</typeparam>
    /// <param name="reducer">The reducer applied to each batch.</param>
    /// <returns>The next stage carrying <typeparamref name="R"/>.</returns>
    IPipelineStage<TOriginalInput, R> Reduce<R>(Func<TCurrent[], R> reducer);

    /// <summary>Collapses each batch to a single value with an async reducer.</summary>
    /// <typeparam name="R">The reduced result type.</typeparam>
    /// <param name="reducer">The async reducer applied to each batch.</param>
    /// <returns>The next stage carrying <typeparamref name="R"/>.</returns>
    IPipelineStage<TOriginalInput, R> ReduceAsync<R>(Func<TCurrent[], CancellationToken, ValueTask<R>> reducer);
}
