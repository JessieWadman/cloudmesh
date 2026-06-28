# CloudMesh

Reusable .NET building blocks for cloud solutions — a collection of small, focused, high-performance
libraries you can pull in à la carte.

**Targets:** .NET 8, 9 and 10 &nbsp;•&nbsp; **License:** MIT &nbsp;•&nbsp; Each component ships as its own NuGet package, so you only take what you need.

```bash
dotnet add package CloudMesh.Core
dotnet add package CloudMesh.DataBlocks
dotnet add package CloudMesh.Variant
# ...etc
```

## Packages at a glance

| Package | What it gives you |
|---|---|
| **CloudMesh.Core** | Common utilities and allocation-conscious helpers (locks, throttling, fast parsing, buffered IO). |
| **CloudMesh.DataBlocks** | A lightweight, in-process actor/pipeline library built on `System.Threading.Channels`. |
| **CloudMesh.Variant** | A boxing-free discriminated union (`Value`) for storing arbitrary value types without heap allocation. |
| **CloudMesh.Uuid** | A very fast UUID v7 (RFC 9562) generator. |
| **CloudMesh.Guid64** | A roughly time-sortable 64-bit id (Snowflake-style) for use as database primary keys. |
| **CloudMesh.DotNotation** | Read/write nested object values by string path (`"address.city"`), compiled and cached. |
| **CloudMesh.MurmurHash** | 32-, 64- and 128-bit MurmurHash implementations with no heavy dependencies. |
| **CloudMesh.Temporal** | Effective-dated ("temporal") records with historical and pending future values. |
| **CloudMesh.NetworkMutex.\*** | Cluster-wide exclusive locks borrowed from a database engine (Postgres / DynamoDB). |
| **CloudMesh.Persistence.DynamoDB** | A fluent repository pattern for DynamoDB, with an in-memory implementation for tests. |
| **CloudMesh.SystemClock** | ⚠️ Deprecated — use the BCL `TimeProvider` instead. |

---

## Core

Common utilities and optimization helpers.

| Class | Description |
|---|---|
| `Throttler` | Easy implementation of throttling within your code. |
| `AsyncLazy<T>` | Same as the standard `Lazy<T>` but with async initialization. |
| `AsyncLock` | An async/awaitable lock — like a semaphore, but for use in async code. |
| `AsyncReaderWriterLock` | An async/awaitable reader-writer lock. |
| `FastDecimalParser` | Very fast decimal parsing of strings — much faster than the built-in `decimal.Parse()`. |
| `BufferedStreamLineReader` | Statically-allocated, buffered line reader for reading one line of text at a time from a stream with little to no GC — designed for streaming millions of rows. |
| `MemoryHelper` | Helpers for growing/aligning pooled `Memory<T>` buffers. |
| `Timestamp` | A monotonic, allocation-free relative timestamp (based on `Environment.TickCount64`) for intervals and timeouts that is immune to wall-clock/NTP/DST jumps. |
| `ConcurrentList`, `PriorityQueue` | Small concurrent/utility collections. |

## Data Blocks

Worker and producer/consumer patterns implemented using the highly performant channels from `System.Threading.Channels`.
It can be thought of as an actor framework that went on a diet to become a light-weight library, with in-process-only patterns.
Allows for easy, high-throughput, in-process processing using fan-out, fan-in, aggregation, buffering, round-robin and similar patterns.
Great for building processing pipelines and background workers.

| Class | Description |
|---|---|
| `DataBlock` | Generic consumer. Decouples receiving and consuming messages. |
| `AggregationDataBlock<T>` | Fan-in: consume messages to calculate state over time and periodically emit it. Example: sum, average or frequency every 5 seconds. Great for aggregating metrics. |
| `BufferBlock<T>` | Fan-in: buffer up to X items, or Y milliseconds, whichever happens first, then consume the batch received within the window. |
| `BufferRouter<T>` | Fan-in: extends `BufferBlock<T>` to automatically forward the batch to another `DataBlock`. |
| `RoundRobinDataBlock` | Fan-out: round-robin. Distributes load fairly across child actors — for each message, advance to the next child and wrap around. |
| `SpillOverDataBlock` | Fan-out: saturate-before-advance. Fills each child to capacity before switching to the next. Great for collecting full batches before pushing downstream. |
| `DataBlockScheduler` | Schedule message delivery to data blocks, with cancellation. Great for timeouts and wait patterns. |
| `CaptureBlock` | Collect all received messages and list them. Mostly for unit-testing code that uses DataBlocks. |
| `BackpressureMonitor` | A hook to identify backpressure buildup in pipelines. |

## Variant

A boxing-free discriminated union, `Value`, for stashing arbitrary value types in arrays or passing them
as arguments without allocating. Storing a value type as `object` boxes it (a heap allocation); in
high-throughput code — parsing records, then rearranging and bulk-loading them — that can mean millions of
allocations and heavy GC pressure just to box primitives.

`Value` avoids boxing wherever possible:

```csharp
Value[] row = new Value[3];
row[0] = 42;             // int    — no boxing
row[1] = Guid.NewGuid(); // 16-byte struct, stored inline — no boxing
row[2] = default;        // null   — no boxing

int   n  = row[0].As<int>();
Guid? id = row[1].As<Guid?>();   // nullable round-trip — still no boxing
```

It handles the built-in primitives, `DateTime`/`DateTimeOffset` (packed), enums, `ArraySegment<byte>`/`<char>`,
and any user `struct` that fits inline (up to the union size) — including reading a stored value back as its
`Nullable<T>` form. Larger structs fall back to a single boxed reference.

## Uuid

A very fast UUID v7 (RFC 9562) generator — time-ordered, so it sorts well as a database key. It uses .NET's
`xoshiro256` random source and, for a fixed timestamp, generates a v7 UUID in single-digit nanoseconds —
roughly 5× faster than the BCL's `Guid.CreateVersion7()` in the included benchmarks.

## Guid64

A roughly time-sortable 64-bit guid implementation based on Twitter's Snowflake algorithm.
Great for client-generated primary keys, because database index operations are much faster on a 64-bit
integer than on a 128-bit `Guid`.

## DotNotation

Read and write values on nested object graphs using string paths, e.g. `GetValue(order, "customer.address.city")`
and `SetValue(order, "customer.address.city", "Stockholm")`. Paths are compiled to expressions and cached, and
collections are supported. Handy for mapping, templating and config-driven access.

## MurmurHash

32-, 64- and 128-bit murmur hash implementations adapted from the Akka.NET project. Used by many of the other
libraries here, but without taking a dependency on a massive framework. All credit for the original code goes
to the Akka.NET authors.

## Network Mutexes

Normally, mutexes are scoped to a single machine. Network mutexes let you acquire arbitrary, exclusive locks
across an entire cluster or network. Behind the scenes they "borrow" the locking mechanisms of a database
engine — either row locks or soft locks, depending on the engine's capabilities. If you need cross-machine
mutexes and already have a Postgres database, its exclusive lock mechanisms are very capable, and this library
simply packages them as a convenient mutex. DynamoDB doesn't support row locking the way Postgres does, but it
supports optimistic locking via update conditions, which is what the DynamoDB implementation uses.

## Persistence DynamoDB

An easy-to-use repository pattern for DynamoDB, with a fluent API to select secondary indexes and perform scans
or partial updates. Also includes an in-memory implementation that behaves like the real one, for use in your
unit-test projects.

## Temporal

A library for working with temporal data — records with a change history of values that have taken effect, as
well as pending future changes. Each property of a temporal class can have multiple values, each with an
effective date. Given a point in time, the temporal object produces a snapshot of what it would look like.
Effective dates can be in the past (historical) or the future (pending). Setting a new future value for a
property clears any other pending changes for that property from that date onwards.

Typical scenarios include HR — e.g. recording that a person will change job title, manager and department in
three months. Three months from now, unless changed, the new values "take effect".

---

## Building

```bash
dotnet build CloudMesh.sln
dotnet test
```

Package versions are managed centrally (`Directory.Packages.props`), and all projects multi-target
`net8.0;net9.0;net10.0`.

## License

MIT © Jessie Wadman. See [LICENSE](LICENSE).
