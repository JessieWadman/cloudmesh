using System.Threading.Channels;
using CloudMesh.DataBlocks;

// ===============================================================================================================
// A FLUENT "DATA STREAMS" API OVER CLOUDMESH DATABLOCKS
//
// This turns the low-level DataBlocks primitives (DataBlock, RoundRobinDataBlock, BufferBlock, AggregationDataBlock)
// into a composable, Rx/LINQ-style pipeline builder. Each fluent operator appends a *stage* — a factory that, given
// its downstream, builds one block. Build() wires the chain back-to-front (sink first) and returns an IPipeline.
//
//   Pipeline.OnManualPush<T>()   ── a source you PushAsync into
//   Pipeline.From(asyncEnum)     ── a source that pumps an IAsyncEnumerable
//        .Map / .MapAsync(dop)   ── transform (dop > 1 fans out across N workers, fanning back in to one downstream)
//        .Where                  ── filter
//        .Tap                    ── side effect (pass-through)
//        .Buffer(count, time)    ── batch into TCurrent[]  (enters an "array stage")
//        .Reduce / .ReduceAsync  ── collapse a batch (TCurrent[]) to a single value
//        .Aggregate(seed, acc)   ── windowed fold into one value per window
//        .To(...)                ── sink
//        .Build()                ── materialize; PushAsync feeds the head; DisposeAsync drains every stage in order
//
// Generic threading: TOriginalInput is preserved end-to-end so Build() can type PushAsync(TOriginalInput); TCurrent
// is the type flowing out of the current stage.
// ===============================================================================================================

// ---- Demo 1: manual push — normalize → filter → enrich (4-way fan-out) → buffer → reduce → sink ---------------
Console.WriteLine("=== Demo 1: OnManualPush ===\n");

await using (var pipeline = Pipeline.OnManualPush<string>()
    .Map(s => s.Trim().ToLowerInvariant())                                            // transform
    .Where(s => s.Length > 0)                                                         // filter
    .MapAsync(async (s, ct) => { await Task.Delay(Random.Shared.Next(5, 20), ct); return $"[{s}]"; },
              degreeOfParallelism: 4)                                                  // FAN-OUT + implicit fan-in
    .Tap(s => Console.WriteLine($"  enriched {s}"))                                    // side effect
    .Buffer(maxItems: 3, maxWaitTime: TimeSpan.FromMilliseconds(250))                  // BATCH → string[]
    .Reduce(batch => $"batch({batch.Length}): {string.Join(" ", batch)}")             // string[] → string
    .To(Console.WriteLine)                                                            // sink
    .Build())
{
    foreach (var word in new[] { "  Hello ", "WORLD", "", "  ", "Foo", "Bar", "Baz", "Qux", "Quux" })
        await pipeline.PushAsync(word);

    await Task.Delay(400);          // let the time-based buffer flush; leaving the scope disposes → drains the rest
}

// ---- Demo 2: enumerable source → windowed aggregation ---------------------------------------------------------
Console.WriteLine("\n=== Demo 2: From(IAsyncEnumerable) → windowed sum ===\n");

await using (var agg = Pipeline.From(NumbersAsync())
    .Aggregate(seed: 0, (sum, n) => sum + n, window: TimeSpan.FromMilliseconds(100))   // FAN-IN over a time window
    .To(sum => Console.WriteLine($"  window sum: {sum}"))
    .Build())
{
    // A From-source pipeline pumps itself; disposing (scope exit) awaits the source, then drains the blocks.
}

// ---- Demo 3: Rx-style operators + resilient error handling + channel sink -------------------------------------
Console.WriteLine("\n=== Demo 3: SelectMany → Distinct → OnError → channel sink ===\n");

var outChannel = Channel.CreateUnbounded<int>();

await using (var rx = Pipeline.From(NumbersAsync())
    .SelectMany(n => new[] { n, n % 3 })                                             // flatten: emit 0..n downstream
    .Distinct()                                                                      // drop repeats seen before
    .Map(n => n == 7 ? throw new InvalidOperationException("unlucky 7") : n * n)      // one item throws
    .OnError((ex, item) => Console.WriteLine($"  dropped {item}: {ex.Message}"))     // resilient: log + continue
    .To(outChannel.Writer)                                                           // channel sink (auto-completes)
    .Build())
{
    // Read the sink channel out concurrently; the sink completes the writer when the pipeline drains.
    await foreach (var squared in outChannel.Reader.ReadAllAsync())
        Console.WriteLine($"  squared: {squared}");

    await rx.Completion;   // no fault: the throwing item was handled by OnError
}

Console.WriteLine("\nDone.");

static async IAsyncEnumerable<int> NumbersAsync()
{
    for (var i = 1; i <= 20; i++) { await Task.Delay(20); yield return i; }
}