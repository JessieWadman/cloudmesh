namespace CloudMesh.DataBlocks.Streams.FluentApi;

/// <summary>
/// A pipeline stage: transform, filter, batch, aggregate, or apply an Rx-style operator, then terminate via one of
/// the <see cref="IPipelineTargets{TOriginalInput, TCurrent}"/> <c>To</c> overloads.
/// </summary>
/// <typeparam name="TOriginalInput">The pipeline's original input type, preserved end-to-end so the built pipeline
/// exposes <c>PushAsync(TOriginalInput)</c> regardless of the current stage's type.</typeparam>
/// <typeparam name="TCurrent">The item type flowing out of this stage.</typeparam>
/// <remarks>
/// Every stage awaits its downstream submit, so backpressure propagates upstream through the whole chain. The
/// stateful operators (<see cref="Skip(int)"/>, <see cref="Take(int)"/>,
/// <see cref="DistinctUntilChanged()"/>, <see cref="Scan{TAccumulate}(TAccumulate, System.Func{TAccumulate, TCurrent, TAccumulate})"/>)
/// keep per-instance state and observe items in <b>arrival order</b>. Placing them after a
/// <see cref="MapAsync{R}(System.Func{TCurrent, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask{R}}, int)"/>
/// with <c>degreeOfParallelism &gt; 1</c> means arrival order is not the source order (the fan-out does not preserve
/// ordering), so those operators act on whatever order items happen to arrive.
/// </remarks>
public interface IPipelineStage<TOriginalInput, TCurrent> : IPipelineTargets<TOriginalInput, TCurrent>
{
    /// <summary>Projects each item to a new value using a synchronous selector.</summary>
    /// <typeparam name="R">The result item type.</typeparam>
    /// <param name="func">The selector applied to each item.</param>
    /// <returns>The next stage carrying <typeparamref name="R"/>.</returns>
    IPipelineStage<TOriginalInput, R> Map<R>(Func<TCurrent, R> func);

    /// <summary>
    /// Projects each item to a new value using an async selector, optionally fanning out across
    /// <paramref name="degreeOfParallelism"/> parallel workers.
    /// </summary>
    /// <typeparam name="R">The result item type.</typeparam>
    /// <param name="func">The async selector applied to each item.</param>
    /// <param name="degreeOfParallelism">The number of parallel workers. <c>1</c> (default) preserves order; a value
    /// greater than <c>1</c> fans out to that many workers behind a round-robin and fans back in to a single
    /// downstream — higher throughput, but <b>ordering is not preserved</b>.</param>
    /// <returns>The next stage carrying <typeparamref name="R"/>.</returns>
    IPipelineStage<TOriginalInput, R> MapAsync<R>(
        Func<TCurrent, CancellationToken, ValueTask<R>> func, int degreeOfParallelism = 1);

    /// <summary>Forwards only the items for which <paramref name="predicate"/> returns <see langword="true"/>.</summary>
    /// <param name="predicate">The predicate deciding whether to keep each item.</param>
    /// <returns>The same stage type, carrying only the kept items.</returns>
    IPipelineStage<TOriginalInput, TCurrent> Where(Func<TCurrent, bool> predicate);

    /// <summary>Runs a side-effecting action on each item and forwards the item unchanged.</summary>
    /// <param name="action">The side effect to run on each item.</param>
    /// <returns>The same stage type, passing items through.</returns>
    IPipelineStage<TOriginalInput, TCurrent> Tap(Action<TCurrent> action);

    /// <summary>
    /// Batches items into arrays, flushing a batch when it reaches <paramref name="maxItems"/> or after
    /// <paramref name="maxWaitTime"/> elapses since the batch's first item — whichever comes first — and on drain.
    /// </summary>
    /// <param name="maxItems">The maximum batch size (must be positive).</param>
    /// <param name="maxWaitTime">The maximum time to hold a partial batch (must be positive).</param>
    /// <returns>An array stage carrying <typeparamref name="TCurrent"/><c>[]</c> batches.</returns>
    IArrayPipelineStage<TOriginalInput, TCurrent, TCurrent[]> Buffer(int maxItems, TimeSpan maxWaitTime);

    /// <summary>
    /// Folds items into a running accumulator over a fixed time window and emits one accumulated value per window
    /// (fan-in). Distinct from <see cref="Scan{TAccumulate}(TAccumulate, Func{TAccumulate, TCurrent, TAccumulate})"/>,
    /// which emits after every item.
    /// </summary>
    /// <typeparam name="TAccumulate">The accumulator/result type.</typeparam>
    /// <param name="seed">The initial accumulator value for each window.</param>
    /// <param name="accumulate">Combines the accumulator with each item.</param>
    /// <param name="window">The flush window (must be positive).</param>
    /// <returns>The next stage carrying <typeparamref name="TAccumulate"/>.</returns>
    IPipelineStage<TOriginalInput, TAccumulate> Aggregate<TAccumulate>(
        TAccumulate seed, Func<TAccumulate, TCurrent, TAccumulate> accumulate, TimeSpan window);

    /// <summary>Drops the first <paramref name="count"/> items (in arrival order) and forwards the rest.</summary>
    /// <param name="count">The number of leading items to drop (must be non-negative).</param>
    /// <returns>The same stage type, with the leading items removed.</returns>
    IPipelineStage<TOriginalInput, TCurrent> Skip(int count);

    /// <summary>Forwards the first <paramref name="count"/> items (in arrival order) and drops the rest.</summary>
    /// <param name="count">The number of leading items to forward (must be non-negative).</param>
    /// <returns>The same stage type, truncated to the leading items.</returns>
    IPipelineStage<TOriginalInput, TCurrent> Take(int count);

    /// <summary>Forwards only items not previously seen, using the default equality comparer.</summary>
    /// <returns>The same stage type, with duplicates removed.</returns>
    /// <remarks>Remembers every distinct item for the pipeline's lifetime; prefer a bounded key space.</remarks>
    IPipelineStage<TOriginalInput, TCurrent> Distinct();

    /// <summary>Forwards only items not previously seen, using the supplied equality comparer.</summary>
    /// <param name="comparer">The comparer used to detect duplicates.</param>
    /// <returns>The same stage type, with duplicates removed.</returns>
    /// <remarks>Remembers every distinct item for the pipeline's lifetime; prefer a bounded key space.</remarks>
    IPipelineStage<TOriginalInput, TCurrent> Distinct(IEqualityComparer<TCurrent> comparer);

    /// <summary>Drops <b>consecutive</b> duplicates, forwarding an item only when it differs from the one before it.</summary>
    /// <returns>The same stage type, with consecutive duplicates collapsed.</returns>
    IPipelineStage<TOriginalInput, TCurrent> DistinctUntilChanged();

    /// <summary>Drops <b>consecutive</b> duplicates using the supplied comparer.</summary>
    /// <param name="comparer">The comparer used to compare adjacent items.</param>
    /// <returns>The same stage type, with consecutive duplicates collapsed.</returns>
    IPipelineStage<TOriginalInput, TCurrent> DistinctUntilChanged(IEqualityComparer<TCurrent> comparer);

    /// <summary>Flattens each item into zero or more downstream items via <paramref name="selector"/>.</summary>
    /// <typeparam name="R">The flattened element type.</typeparam>
    /// <param name="selector">Projects each item to a sequence of downstream elements.</param>
    /// <returns>The next stage carrying <typeparamref name="R"/>.</returns>
    IPipelineStage<TOriginalInput, R> SelectMany<R>(Func<TCurrent, IEnumerable<R>> selector);

    /// <summary>
    /// Maintains a running accumulator and emits it after every item (a running fold). Contrast with
    /// <see cref="Aggregate{TAccumulate}(TAccumulate, Func{TAccumulate, TCurrent, TAccumulate}, TimeSpan)"/>, which
    /// emits once per time window.
    /// </summary>
    /// <typeparam name="TAccumulate">The accumulator type emitted downstream.</typeparam>
    /// <param name="seed">The initial accumulator value.</param>
    /// <param name="accumulate">Combines the accumulator with each item to produce the next accumulator.</param>
    /// <returns>The next stage carrying <typeparamref name="TAccumulate"/>.</returns>
    IPipelineStage<TOriginalInput, TAccumulate> Scan<TAccumulate>(
        TAccumulate seed, Func<TAccumulate, TCurrent, TAccumulate> accumulate);

    /// <summary>
    /// Registers a resilient per-item error handler. When any stage's user code (a selector/predicate/action/fold)
    /// throws, the offending item is dropped, the handler is invoked with the (wrapped) exception and that item, and
    /// the pipeline keeps processing subsequent items. Without a handler, the first such failure instead faults
    /// <see cref="IPipeline{TOriginalInput}.Completion"/>.
    /// </summary>
    /// <param name="handler">Receives the <see cref="PipelineException"/> and the item that caused it.</param>
    /// <returns>The same stage type. A single handler applies to the whole pipeline; the last registration wins.</returns>
    IPipelineStage<TOriginalInput, TCurrent> OnError(Action<Exception, object?> handler);
}
