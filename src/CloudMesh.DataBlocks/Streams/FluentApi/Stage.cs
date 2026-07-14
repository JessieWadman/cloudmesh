using System.Threading.Channels;

namespace CloudMesh.DataBlocks.Streams.FluentApi;

internal class Stage<TOriginalInput, TCurrent> : IPipelineStage<TOriginalInput, TCurrent>
{
    protected readonly PipelineDef Def;
    internal Stage(PipelineDef def) => Def = def;

    // Routes a caught user-code exception (with its item) to the shared error sink assigned at Build() time.
    private void Report(Exception ex, object? item) => Def.ErrorSink?.Report(ex, item);

    public IPipelineStage<TOriginalInput, R> Map<R>(Func<TCurrent, R> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return MapAsync<R>((x, _) => new ValueTask<R>(func(x)));
    }

    public IPipelineStage<TOriginalInput, R> MapAsync<R>(
        Func<TCurrent, CancellationToken, ValueTask<R>> func, int degreeOfParallelism = 1)
    {
        ArgumentNullException.ThrowIfNull(func);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(degreeOfParallelism);

        Def.Stages.Add(downstream =>
        {
            if (degreeOfParallelism <= 1)
                return new TransformBlock<TCurrent, R>(func, downstream, Report);

            // Fan-out: N identical workers behind a round-robin, all fanning back in to the same downstream.
            // The worker must be an inline lambda literal — AddTargets takes an Expression<Func<T>>, not a delegate.
            var roundRobin = new RoundRobinDataBlock();
            roundRobin.AddTargets(() => new TransformBlock<TCurrent, R>(func, downstream, Report), degreeOfParallelism);
            return roundRobin;
        });
        return new Stage<TOriginalInput, R>(Def);
    }

    public IPipelineStage<TOriginalInput, TCurrent> Where(Func<TCurrent, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Def.Stages.Add(downstream => new FilterBlock<TCurrent>(predicate, downstream, Report));
        return new Stage<TOriginalInput, TCurrent>(Def);
    }

    public IPipelineStage<TOriginalInput, TCurrent> Tap(Action<TCurrent> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Def.Stages.Add(downstream => new TapBlock<TCurrent>(action, downstream, Report));
        return new Stage<TOriginalInput, TCurrent>(Def);
    }

    public IArrayPipelineStage<TOriginalInput, TCurrent, TCurrent[]> Buffer(int maxItems, TimeSpan maxWaitTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItems);
        if (maxWaitTime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxWaitTime), maxWaitTime, "The buffer window must be positive.");
        Def.Stages.Add(downstream => new BufferStage<TCurrent>(maxItems, maxWaitTime, downstream));
        return new ArrayStage<TOriginalInput, TCurrent>(Def);
    }

    public IPipelineStage<TOriginalInput, TAccumulate> Aggregate<TAccumulate>(
        TAccumulate seed, Func<TAccumulate, TCurrent, TAccumulate> accumulate, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(accumulate);
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window), window, "The aggregation window must be positive.");
        Def.Stages.Add(downstream => new AggregateStage<TCurrent, TAccumulate>(seed, accumulate, window, downstream));
        return new Stage<TOriginalInput, TAccumulate>(Def);
    }

    public IPipelineStage<TOriginalInput, TCurrent> Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Def.Stages.Add(downstream => new SkipBlock<TCurrent>(count, downstream));
        return new Stage<TOriginalInput, TCurrent>(Def);
    }

    public IPipelineStage<TOriginalInput, TCurrent> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Def.Stages.Add(downstream => new TakeBlock<TCurrent>(count, downstream));
        return new Stage<TOriginalInput, TCurrent>(Def);
    }

    public IPipelineStage<TOriginalInput, TCurrent> Distinct()
    {
        Def.Stages.Add(downstream => new DistinctBlock<TCurrent>(downstream));
        return new Stage<TOriginalInput, TCurrent>(Def);
    }

    public IPipelineStage<TOriginalInput, TCurrent> Distinct(IEqualityComparer<TCurrent> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        Def.Stages.Add(downstream => new DistinctBlock<TCurrent>(downstream, comparer));
        return new Stage<TOriginalInput, TCurrent>(Def);
    }

    public IPipelineStage<TOriginalInput, TCurrent> DistinctUntilChanged()
    {
        Def.Stages.Add(downstream => new DistinctUntilChangedBlock<TCurrent>(downstream));
        return new Stage<TOriginalInput, TCurrent>(Def);
    }

    public IPipelineStage<TOriginalInput, TCurrent> DistinctUntilChanged(IEqualityComparer<TCurrent> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        Def.Stages.Add(downstream => new DistinctUntilChangedBlock<TCurrent>(downstream, comparer));
        return new Stage<TOriginalInput, TCurrent>(Def);
    }

    public IPipelineStage<TOriginalInput, R> SelectMany<R>(Func<TCurrent, IEnumerable<R>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Def.Stages.Add(downstream => new SelectManyBlock<TCurrent, R>(selector, downstream, Report));
        return new Stage<TOriginalInput, R>(Def);
    }

    public IPipelineStage<TOriginalInput, TAccumulate> Scan<TAccumulate>(
        TAccumulate seed, Func<TAccumulate, TCurrent, TAccumulate> accumulate)
    {
        ArgumentNullException.ThrowIfNull(accumulate);
        Def.Stages.Add(downstream => new ScanBlock<TCurrent, TAccumulate>(seed, accumulate, downstream, Report));
        return new Stage<TOriginalInput, TAccumulate>(Def);
    }

    public IPipelineStage<TOriginalInput, TCurrent> OnError(Action<Exception, object?> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Def.ErrorHandler = handler;
        return new Stage<TOriginalInput, TCurrent>(Def);
    }

    public IPipelineFinal<TOriginalInput> To(Action<TCurrent> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return To((x, _) => { action(x); return ValueTask.CompletedTask; });
    }

    public IPipelineFinal<TOriginalInput> To(Func<TCurrent, CancellationToken, ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Def.SinkFactory = () => new SinkBlock<TCurrent>(x => action(x, CancellationToken.None), Def.ErrorSink!.Report);
        Def.OwnsSink = true;
        return new Final<TOriginalInput>(Def);
    }

    public IPipelineFinal<TOriginalInput> To(ICanSubmit target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Def.Sink = target;
        Def.OwnsSink = false;
        return new Final<TOriginalInput>(Def);
    }

    public IPipelineFinal<TOriginalInput> To(ChannelWriter<TCurrent> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Def.SinkFactory = () => new ChannelSinkBlock<TCurrent>(writer);
        Def.OwnsSink = true;
        return new Final<TOriginalInput>(Def);
    }
}
