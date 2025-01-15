# CloudMesh
Reusable dotnet building blocks for cloud solutions.

## Core
Some common utilities and optimization helpers.

| Class             | Description                                                                         |         
|-------------------|-------------------------------------------------------------------------------------|
| Throttler         | Easy implementation of throttling within your code.                                 |
| AsyncLazy         | Same as standard Lazy<T> but async initialization.                                  |
| AsyncLock         | Async/awaitable lock, same as Semaphore, but for use in async code.                 |        
| FastDecimalParser | Very fast decimal parsing of strings. Much faster than the built-in decimal.Parse() |


## Data Blocks
Worker and producer/consumer patterns implemented using the highly performant channels from System.Threading.Channels.
It can be thought of as an actor framework that went on a diet to become a light-weight library, with in-process only patterns.
Allows for easy, high-throughput, in-process processing using fan-out, fan-in, aggregation, buffering, round-robin and similar pattern.
Great for building processing pipelines and background workers.

| Class                   | Description                                                                                                                                                                                                                  |
|-------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| DataBlock               | Generic consumer. Decouples receiving and consuming messages.                                                                                                                                                                |
| AggregationDataBlock<T> | Fan-in pattern: Consumes messages to calculate state over time and periodically emit the state. Example: Calculating the sum, average or frequency of values every 5 seconds. Great for aggregating metrics.                 |
| BufferBlock<T>          | Fan-in pattern: Buffer up to X items, or Y milliseconds, whichever happen first, and then consume the batch of items received within the window.                                                                             |
| BufferRouter<T>         | Fan-in pattern: Extends BufferBlock<T> to automatically forward the batch to another DataBlock.                                                                                                                              |
| RoundRobinDataBlock     | Fan-out pattern: Uses the round-robin algorithm. Distributes load fairly across child actors. For each message received, advance to next child and then wrap-around when we hit the number of children.                      |
| SpillOverDataBlock      | Fan-out pattern: Uses saturate-before-advance logic. Saturates each child completely to capacity before switching to the next. Great for when you want to collect batches of data and then push complete batches downstream. |
| DataBlockScheduler      | Allows scheduling of message delivery to data blocks, with support for cancellation. Great for controlling timeouts and wait patterns.                                                                                       |
| CaptureBlock            | Collect all messages received and allows them to be listed. Mostly for unit testing code that uses DataBlocks.                                                                                                               |
| BackpressureMonitor     | Hook where you can identify backpressure buildup in pipelines.                                                                                                                                                               |


## Guid64
A roughly time-sortable 64 bit guid implementation based on Twitter's Snowflake algorithm.
Great for using as client-generated primary keys in databases, due to all index operations in the database being
much faster for 64 bit int, compared to 128 bit byte arrays (Guid).

## MurmurHash
32, 64 and 128 bit murmur hash implementations copied from Akka.NET project. Used by many of the other libraries in here,
but without the need to put a dependency on a massive framework such as Akka.NET. All credits for the code and implementation go to
them.

## Network Mutexes
Normally, mutexes are only within a single machine. The network mutexes allows for acquiring arbitrary, exclusive locks
across an entire cluster or network. It does so (behind the scenes) by "borrowing" the locking mechanisms of database
engine. Either row locks, or soft locks, depending on the capabilities of the engine. If you for example have the need
to use cross-machine mutexes, and also happen to have a Postgres database in your project, the Postgres database
actually has very competent exclusive lock mechanisms. This project merely packages that locking mechanisms in a
convenient way as a mutex. Same goes for DynamoDb. While DynamoDb does not support row locking the way that Postgres
does, it does support optimistic locking by means of update conditions, which is what the DynamoDb implementation does.

## Persistence DynamoDB
Provides an easy-to-use repository pattern for DynamoDB, with fluent api to select secondary indexes, performing scans
or partial update. Also includes an in-memory implementation that works the same way as the real one, for use by your
unit test projects.

## Temporal
Library to work with temporal data. That is; records with change history of values that have taken effect as well as pending, future changes.
Each property of a temporal class can have multiple values, each with an effective date.
Given a point-in-time, the temporal object will produce a snapshot of what the object would look like.
The effective dates can be in the past (historical) or in the future (pending).
Setting a new, future value for a property will clear any other future, pending changes for that property from that date onwards.

Typical scenarios are HR, where you record that a person will in 3 months change job title, manager and department.
3 months from now, unless the person changes their mind, the values will "take effect".
