# CloudMesh.DataBlocks

A lightweight, in-process **actor / pipeline** library built on `System.Threading.Channels`. Think of it as an
actor framework that went on a diet: message-driven components you wire into high-throughput processing pipelines
— fan-out, fan-in, batching, aggregation, round-robin — without pulling in a full actor runtime.

- **Targets:** .NET 8, 9, 10 — **License:** MIT

```bash
dotnet add package CloudMesh.DataBlocks
```

## The model

A **`DataBlock`** is a component with a mailbox. It processes one message at a time (so handler state needs no
locking), receiving typed messages via `ReceiveAsync<T>(...)` and being fed via `SubmitAsync(...)`. Blocks are
composed into pipelines where each stage hands work to the next.

```csharp
using CloudMesh.DataBlocks;

public sealed class OrderProcessor : DataBlock
{
    public OrderProcessor()
    {
        ReceiveAsync<Order>(async order =>
        {
            await ProcessAsync(order);
        });
    }
}

var processor = /* create/host the block */;
await processor.SubmitAsync(new Order(...), sender: null);   // one message at a time, in order
```

## The blocks

| Block | Role |
|---|---|
| `DataBlock` | Base consumer — decouples receiving from consuming. |
| `AggregationDataBlock<T>` | **Fan-in:** compute state over time and emit it periodically (sum/avg/frequency every N seconds). |
| `BufferBlock<T>` | **Fan-in:** batch up to N items or T milliseconds, whichever first, then consume the batch. |
| `BufferRouter<T>` | **Fan-in:** a `BufferBlock<T>` that forwards each batch to another block. |
| `RoundRobinDataBlock` | **Fan-out:** distribute messages fairly across child blocks, one at a time. |
| `SpillOverDataBlock` | **Fan-out:** fill each child to capacity before advancing to the next. |
| `DataBlockScheduler` | Schedule delayed/cancelable message delivery — timeouts, wait patterns. |
| `CaptureBlock` | Collect received messages into a list — mostly for unit-testing pipelines. |
| `BackpressureMonitor` | A hook to detect backpressure buildup in a pipeline. |

## Fluent pipeline (Data Streams)

On top of the blocks sits a composable, Rx/LINQ-style **pipeline builder**. You pick a *source*, chain *operators*,
choose a *sink* with `To(...)`, then `Build()` a running `IPipeline<T>`. Each operator is just a `DataBlock` that
awaits its downstream submit, so **backpressure propagates upstream** through the whole chain, and disposing the
pipeline **drains and flushes every stage in order** (buffered/aggregated items included).

```csharp
using CloudMesh.DataBlocks;

await using var pipeline = Pipeline.OnManualPush<string>()
    .Map(s => s.Trim().ToLowerInvariant())                       // transform
    .Where(s => s.Length > 0)                                    // filter
    .MapAsync(async (s, ct) => await EnrichAsync(s, ct),
              degreeOfParallelism: 4)                            // fan-out + implicit fan-in (order NOT preserved)
    .Distinct()                                                  // Rx-style operator
    .Buffer(maxItems: 100, maxWaitTime: TimeSpan.FromSeconds(1)) // batch into T[]
    .Reduce(batch => batch.Length)                               // collapse a batch to one value
    .OnError((ex, item) => log.LogWarning(ex, "dropped {Item}", item)) // resilient error handler
    .To(Console.WriteLine)                                       // sink
    .Build();

foreach (var word in words)
    await pipeline.PushAsync(word);
// leaving the scope disposes → drains every stage
```

### Sources

| Source | Pumps |
|---|---|
| `Pipeline.OnManualPush<T>()` | You feed it: `await pipeline.PushAsync(item)`. |
| `Pipeline.From(IAsyncEnumerable<T>)` | Self-pumps the sequence once built. |
| `Pipeline.From(ChannelReader<T>)` | Self-pumps a `System.Threading.Channels` reader until it completes. |

### Operators

| Operator | What it does |
|---|---|
| `Map` / `MapAsync(dop)` | Transform each item. `dop > 1` fans out across N workers (order **not** preserved). |
| `Where` | Filter. |
| `Tap` | Run a side effect, pass the item through unchanged. |
| `Skip(n)` / `Take(n)` | Drop / forward the first *n* items (arrival order). |
| `Distinct()` / `Distinct(comparer)` | Forward only items not seen before. |
| `DistinctUntilChanged()` / `…(comparer)` | Drop consecutive duplicates. |
| `SelectMany(selector)` | Flatten: emit 0..n downstream items per input. |
| `Scan(seed, acc)` | Running fold — emit the accumulator after **every** item. |
| `Buffer(count, time)` | Batch into `T[]` by size or time window (fan-in). |
| `Reduce` / `ReduceAsync` | Collapse a batch (`T[]`) to a single value. |
| `Aggregate(seed, acc, window)` | Time-windowed fold — emit one value **per window** (fan-in). |

> The stateful operators (`Skip`, `Take`, `DistinctUntilChanged`, `Scan`) observe items in **arrival order**. After a
> `MapAsync(dop > 1)` fan-out that is *not* the source order, so place them before the fan-out if you need source order.

### Sinks

`To(Action<T>)`, `To(Func<T, CancellationToken, ValueTask>)`, `To(ICanSubmit)` (an existing block; not owned/disposed),
or `To(ChannelWriter<T>)` (writes each item and completes the writer when the pipeline drains, so a downstream
`reader.ReadAllAsync()` loop terminates).

### Error model

Two ways to observe failures in user code (a selector/predicate/action/fold):

- **`OnError(Action<Exception, object?>)`** — a *resilient* handler. When any stage throws, the offending item is
  **dropped**, the handler is invoked with a `PipelineException` and that item, and the pipeline **keeps running**.
  `Completion` still finishes successfully.
- **`IPipeline<T>.Completion`** — a `Task` that completes when the pipeline drains normally, or **faults** with the
  first `PipelineException` when **no** `OnError` handler was registered. `await pipeline.Completion` to observe it.

```csharp
await using var pipeline = Pipeline.From(source).Map(Risky).To(Sink).Build();
try { await pipeline.Completion; }          // faults on the first stage error (no OnError registered)
catch (PipelineException ex) { /* ex.Item is the offending value */ }
```

`DisposeAsync` always drains cleanly and never re-throws pipeline faults — errors are observed via `OnError` or
`Completion`. For a `From`/channel source, `Completion` also completes on its own once the source is exhausted and
the stages have drained, so you can `await pipeline.Completion` without disposing.

## Samples

Runnable examples live in [`samples/DataBlocks`](https://github.com/JessieWadman/cloudmesh/tree/main/samples/DataBlocks):
`ProducerConsumerSample`, `RoundRobinSample`, `AggregationSample`, `CaptureBlockSample`, `TimeoutSample`,
`PrioritizedConsumer`, **`PipelineSample`** — a composite order-processing pipeline that wires **fan-out**,
**content routing**, **buffering**, and **fan-in aggregation** together to show how the blocks compose — and
**`DataStreamsSample`**, an end-to-end tour of the fluent pipeline API (operators, channel source/sink, error model).

## When to reach for it

- Building background workers or streaming pipelines that need **high throughput in-process**.
- You want ordered, single-threaded-per-block processing without manual locking.
- You need batching (`BufferBlock`), time-windowed aggregation (`AggregationDataBlock`), or load distribution
  (`RoundRobinDataBlock` / `SpillOverDataBlock`) as ready-made building blocks.

If you need *cross-process* or *cross-machine* messaging, this isn't that — it's deliberately in-process only.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
