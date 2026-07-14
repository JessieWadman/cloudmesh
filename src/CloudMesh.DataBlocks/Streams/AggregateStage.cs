namespace CloudMesh.DataBlocks.Streams;

internal sealed class AggregateStage<T, TAccumulate> : AggregationDataBlock<T>
{
    private readonly ICanSubmit downstream;
    private readonly TAccumulate seed;
    private readonly Func<TAccumulate, T, TAccumulate> accumulate;
    private TAccumulate state;
    private bool any;

    public AggregateStage(TAccumulate seed, Func<TAccumulate, T, TAccumulate> accumulate, TimeSpan window, ICanSubmit downstream)
        : base(flushFrequency: window, bufferSize: 1_000_000)
    {
        this.downstream = downstream;
        this.seed = seed;
        this.accumulate = accumulate;
        state = seed;
    }

    protected override bool ReceiveOne(T item)
    {
        state = accumulate(state, item);
        any = true;
        return true;
    }

    protected override async ValueTask FlushAsync()
    {
        if (!any) return;
        await downstream.SubmitAsync(state, this);
        state = seed;
        any = false;
    }
}