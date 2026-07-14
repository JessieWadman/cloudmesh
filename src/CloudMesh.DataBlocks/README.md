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

## When to reach for it

- Building background workers or streaming pipelines that need **high throughput in-process**.
- You want ordered, single-threaded-per-block processing without manual locking.
- You need batching (`BufferBlock`), time-windowed aggregation (`AggregationDataBlock`), or load distribution
  (`RoundRobinDataBlock` / `SpillOverDataBlock`) as ready-made building blocks.

If you need *cross-process* or *cross-machine* messaging, this isn't that — it's deliberately in-process only.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
