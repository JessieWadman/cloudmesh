# CloudMesh
Reusable dotnet building blocks for cloud solutions.

## Core
Some common utilities and optimization helpers.

| Class             | Description                                                                      |         
|-------------------|----------------------------------------------------------------------------------|
| Throttler         | Easy implementation of throttling within your code.                              |
| AsyncLazy         | Same as standard Lazy<T> but async initialization.                               |
| AsyncLock         | Async/awaitable lock, same as Semaphore, but for use in async code.              |        
| FastDecimalParser | Very fast decimal parsing of strings. Must faster than build-int decimal.Parse() |
 
 
## Data Blocks
Worker and producer/consumer patterns implemented using the highly performance channels from System.Threading.Channels.
Allows for easy, high-throughput, in-process processing using fan-out, fan-in, aggregation, buffering, round-robin and similar pattern.
Great for building processing pipelines and background workers.

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
Provides an easy to use repository pattern for DynamoDB, with fluent api to select secondary indexes, performing scans
or partial update. Also includes an in-memory implementation that works the same way as the real one, for use by your 
unit test projects.