using System.Threading.Channels;
using CloudMesh.DataBlocks.Streams;
using CloudMesh.DataBlocks.Streams.FluentApi;

namespace CloudMesh.DataBlocks.Tests;

public class PipelineTests
{
    // A thread-safe collecting sink for deterministic assertions.
    private sealed class Collector<T>
    {
        private readonly List<T> items = new();
        private readonly object gate = new();

        public void Add(T item)
        {
            lock (gate)
                items.Add(item);
        }

        public List<T> Snapshot()
        {
            lock (gate)
                return items.ToList();
        }
    }

    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(100);

    // ---- Core operators --------------------------------------------------------------------------------------

    [Fact]
    public async Task Map_TransformsEachItem()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.OnManualPush<int>()
                         .Map(x => x * 2)
                         .To(sink.Add)
                         .Build())
        {
            for (var i = 1; i <= 5; i++)
                await pipeline.PushAsync(i);
        }

        Assert.Equal(new[] { 2, 4, 6, 8, 10 }, sink.Snapshot().OrderBy(x => x));
    }

    [Fact]
    public async Task MapAsync_TransformsEachItem()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.OnManualPush<int>()
                         .MapAsync(async (x, ct) => { await Task.Delay(1, ct); return x + 100; })
                         .To(sink.Add)
                         .Build())
        {
            for (var i = 0; i < 5; i++)
                await pipeline.PushAsync(i);
        }

        Assert.Equal(new[] { 100, 101, 102, 103, 104 }, sink.Snapshot().OrderBy(x => x));
    }

    [Fact]
    public async Task Where_FiltersItems()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.OnManualPush<int>()
                         .Where(x => x % 2 == 0)
                         .To(sink.Add)
                         .Build())
        {
            for (var i = 0; i < 10; i++)
                await pipeline.PushAsync(i);
        }

        Assert.Equal(new[] { 0, 2, 4, 6, 8 }, sink.Snapshot().OrderBy(x => x));
    }

    [Fact]
    public async Task Tap_RunsSideEffectAndPassesThrough()
    {
        var tapped = new Collector<int>();
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.OnManualPush<int>()
                         .Tap(tapped.Add)
                         .To(sink.Add)
                         .Build())
        {
            for (var i = 1; i <= 3; i++)
                await pipeline.PushAsync(i);
        }

        Assert.Equal(new[] { 1, 2, 3 }, tapped.Snapshot().OrderBy(x => x));
        Assert.Equal(new[] { 1, 2, 3 }, sink.Snapshot().OrderBy(x => x));
    }

    [Fact]
    public async Task Buffer_Then_Reduce_CollapsesBatches()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.OnManualPush<int>()
                         .Buffer(maxItems: 3, maxWaitTime: TimeSpan.FromSeconds(10))
                         .Reduce(batch => batch.Sum())
                         .To(sink.Add)
                         .Build())
        {
            for (var i = 1; i <= 6; i++)
                await pipeline.PushAsync(i);
        }

        // Two full batches (1+2+3) and (4+5+6), regardless of order.
        Assert.Equal(new[] { 6, 15 }, sink.Snapshot().OrderBy(x => x));
    }

    [Fact]
    public async Task Aggregate_EmitsWindowedFold()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.OnManualPush<int>()
                         .Aggregate(seed: 0, (sum, n) => sum + n, window: Window)
                         .To(sink.Add)
                         .Build())
        {
            for (var i = 1; i <= 10; i++)
                await pipeline.PushAsync(i);

            // Give the window time to flush at least once before draining.
            await Task.Delay(300);
        }

        // Sum of all items (across however many windows) must equal 55.
        Assert.Equal(55, sink.Snapshot().Sum());
    }

    // ---- New Rx-style operators ------------------------------------------------------------------------------

    [Fact]
    public async Task Skip_DropsLeadingItems()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.From(RangeAsync(0, 5))
                         .Skip(2)
                         .To(sink.Add)
                         .Build())
        {
            await pipeline.Completion;
        }

        Assert.Equal(new[] { 2, 3, 4 }, sink.Snapshot());
    }

    [Fact]
    public async Task Take_ForwardsLeadingItems()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.From(RangeAsync(0, 10))
                         .Take(3)
                         .To(sink.Add)
                         .Build())
        {
            await pipeline.Completion;
        }

        Assert.Equal(new[] { 0, 1, 2 }, sink.Snapshot());
    }

    [Fact]
    public async Task Distinct_RemovesDuplicates()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.From(FromArrayAsync(new[] { 1, 1, 2, 3, 2, 3, 4 }))
                         .Distinct()
                         .To(sink.Add)
                         .Build())
        {
            await pipeline.Completion;
        }

        Assert.Equal(new[] { 1, 2, 3, 4 }, sink.Snapshot());
    }

    [Fact]
    public async Task DistinctUntilChanged_CollapsesConsecutiveDuplicates()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.From(FromArrayAsync(new[] { 1, 1, 2, 2, 2, 3, 1, 1 }))
                         .DistinctUntilChanged()
                         .To(sink.Add)
                         .Build())
        {
            await pipeline.Completion;
        }

        Assert.Equal(new[] { 1, 2, 3, 1 }, sink.Snapshot());
    }

    [Fact]
    public async Task SelectMany_FlattensSequences()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.From(FromArrayAsync(new[] { 1, 2, 3 }))
                         .SelectMany(n => Enumerable.Range(1, n))
                         .To(sink.Add)
                         .Build())
        {
            await pipeline.Completion;
        }

        Assert.Equal(new[] { 1, 1, 2, 1, 2, 3 }, sink.Snapshot());
    }

    [Fact]
    public async Task Scan_EmitsRunningFold()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.From(FromArrayAsync(new[] { 1, 2, 3, 4 }))
                         .Scan(0, (acc, n) => acc + n)
                         .To(sink.Add)
                         .Build())
        {
            await pipeline.Completion;
        }

        Assert.Equal(new[] { 1, 3, 6, 10 }, sink.Snapshot());
    }

    // ---- Fan-out / drain -------------------------------------------------------------------------------------

    [Fact]
    public async Task FanOut_DeliversAllItems()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.OnManualPush<int>()
                         .MapAsync(async (x, ct) => { await Task.Delay(Random.Shared.Next(1, 5), ct); return x; },
                             degreeOfParallelism: 4)
                         .To(sink.Add)
                         .Build())
        {
            for (var i = 0; i < 100; i++)
                await pipeline.PushAsync(i);
        }

        // Order is not guaranteed after fan-out, but every item must arrive exactly once.
        Assert.Equal(Enumerable.Range(0, 100), sink.Snapshot().OrderBy(x => x));
    }

    [Fact]
    public async Task DrainOnDispose_FlushesBufferedItems()
    {
        var sink = new Collector<int>();
        var pipeline = Pipeline.OnManualPush<int>()
            .Buffer(maxItems: 1000, maxWaitTime: TimeSpan.FromSeconds(60))
            .Reduce(batch => batch.Length)
            .To(sink.Add)
            .Build();

        for (var i = 0; i < 7; i++)
            await pipeline.PushAsync(i);

        // Nothing has flushed yet (batch not full, window not elapsed). Disposing must flush the partial batch.
        await pipeline.DisposeAsync();

        Assert.Equal(new[] { 7 }, sink.Snapshot());
    }

    // ---- Sources & sinks -------------------------------------------------------------------------------------

    [Fact]
    public async Task OnManualPush_PushesToSink()
    {
        var sink = new Collector<string>();
        await using (var pipeline = Pipeline.OnManualPush<string>()
                         .Map(s => s.ToUpperInvariant())
                         .To(sink.Add)
                         .Build())
        {
            await pipeline.PushAsync("a");
            await pipeline.PushAsync("b");
        }

        Assert.Equal(new[] { "A", "B" }, sink.Snapshot().OrderBy(x => x));
    }

    [Fact]
    public async Task From_AsyncEnumerable_Source()
    {
        var sink = new Collector<int>();
        await using (var pipeline = Pipeline.From(RangeAsync(0, 5))
                         .Map(x => x * 10)
                         .To(sink.Add)
                         .Build())
        {
            await pipeline.Completion;
        }

        Assert.Equal(new[] { 0, 10, 20, 30, 40 }, sink.Snapshot());
    }

    [Fact]
    public async Task From_ChannelReader_Source()
    {
        var channel = Channel.CreateUnbounded<int>();
        var sink = new Collector<int>();

        await using var pipeline = Pipeline.From(channel.Reader)
            .Map(x => x + 1)
            .To(sink.Add)
            .Build();

        for (var i = 0; i < 5; i++)
            await channel.Writer.WriteAsync(i);
        channel.Writer.Complete();

        await pipeline.Completion;

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, sink.Snapshot());
    }

    [Fact]
    public async Task To_ChannelWriter_Sink_And_ReadBack()
    {
        var outChannel = Channel.CreateUnbounded<int>();

        await using var pipeline = Pipeline.From(RangeAsync(0, 5))
            .Map(x => x * 2)
            .To(outChannel.Writer)
            .Build();

        // Read the sink channel out to completion; the sink completes the writer on drain.
        var received = new List<int>();
        await foreach (var item in outChannel.Reader.ReadAllAsync())
            received.Add(item);

        Assert.Equal(new[] { 0, 2, 4, 6, 8 }, received);
        await pipeline.Completion;
    }

    // ---- Guard clauses ---------------------------------------------------------------------------------------

    [Fact]
    public void GuardClauses_Throw()
    {
        var stage = Pipeline.OnManualPush<int>();

        Assert.Throws<ArgumentNullException>(() => stage.Map<int>(null!));
        Assert.Throws<ArgumentNullException>(() => stage.MapAsync<int>(null!));
        Assert.Throws<ArgumentNullException>(() => stage.Where(null!));
        Assert.Throws<ArgumentNullException>(() => stage.Tap(null!));
        Assert.Throws<ArgumentNullException>(() => stage.SelectMany<int>(null!));
        Assert.Throws<ArgumentNullException>(() => stage.Scan<int>(0, null!));
        Assert.Throws<ArgumentNullException>(() => stage.OnError(null!));
        Assert.Throws<ArgumentNullException>(() => stage.Distinct(null!));
        Assert.Throws<ArgumentNullException>(() => stage.DistinctUntilChanged(null!));

        Assert.Throws<ArgumentOutOfRangeException>(() => stage.MapAsync((x, _) => new ValueTask<int>(x), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stage.Skip(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stage.Take(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stage.Buffer(0, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => stage.Buffer(1, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => stage.Aggregate(0, (a, b) => a + b, TimeSpan.Zero));

        Assert.Throws<ArgumentNullException>(() => stage.To((ICanSubmit)null!));
        Assert.Throws<ArgumentNullException>(() => stage.To((Action<int>)null!));
        Assert.Throws<ArgumentNullException>(() => stage.To((Func<int, CancellationToken, ValueTask>)null!));
        Assert.Throws<ArgumentNullException>(() => stage.To((ChannelWriter<int>)null!));

        Assert.Throws<ArgumentNullException>(() => Pipeline.From((IAsyncEnumerable<int>)null!));
        Assert.Throws<ArgumentNullException>(() => Pipeline.From((ChannelReader<int>)null!));
    }

    // ---- Error model -----------------------------------------------------------------------------------------

    [Fact]
    public async Task OnError_CatchesThrowAndContinues()
    {
        var sink = new Collector<int>();
        var errors = new Collector<(Exception, object?)>();

        await using (var pipeline = Pipeline.OnManualPush<int>()
                         .Map(x =>
                         {
                             if (x == 3)
                                 throw new InvalidOperationException("boom");
                             return x;
                         })
                         .OnError((ex, item) => errors.Add((ex, item)))
                         .To(sink.Add)
                         .Build())
        {
            for (var i = 1; i <= 5; i++)
                await pipeline.PushAsync(i);
        }

        // The offending item (3) is dropped; everything else is delivered.
        Assert.Equal(new[] { 1, 2, 4, 5 }, sink.Snapshot().OrderBy(x => x));

        var captured = errors.Snapshot();
        Assert.Single(captured);
        Assert.IsType<PipelineException>(captured[0].Item1);
        Assert.Equal(3, captured[0].Item2);
    }

    [Fact]
    public async Task WithoutOnError_CompletionFaults()
    {
        var sink = new Collector<int>();
        var pipeline = Pipeline.OnManualPush<int>()
            .Map(x =>
            {
                if (x == 2)
                    throw new InvalidOperationException("kaboom");
                return x;
            })
            .To(sink.Add)
            .Build();

        for (var i = 1; i <= 4; i++)
            await pipeline.PushAsync(i);

        // Dispose drains cleanly and does NOT throw the fault...
        await pipeline.DisposeAsync();

        // ...but the fault is observable via Completion.
        var ex = await Assert.ThrowsAsync<PipelineException>(async () => await pipeline.Completion);
        Assert.Equal(2, ex.Item);

        // Subsequent items still flowed through despite the fault.
        Assert.Equal(new[] { 1, 3, 4 }, sink.Snapshot().OrderBy(x => x));
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------

    private static async IAsyncEnumerable<int> RangeAsync(int start, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Yield();
            yield return start + i;
        }
    }

    private static async IAsyncEnumerable<int> FromArrayAsync(int[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
