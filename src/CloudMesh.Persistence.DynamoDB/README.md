# CloudMesh.Persistence.DynamoDB

A **lightweight repository pattern for AWS DynamoDB** — a small, fluent API over the AWS SDK for key lookups,
conditional writes, partial (patch) updates, secondary-index queries and scans, and atomic transactions. Ships a
drop-in **in-memory implementation** so you can unit-test persistence code with no AWS dependency.

- **Targets:** .NET 8, 9, 10 — **License:** MIT

---

## Table of contents

- [Install](#install)
- [Register](#register)
- [Define an entity](#define-an-entity)
- [Read and write](#read-and-write)
- [Fluent queries and secondary indexes](#fluent-queries-and-secondary-indexes)
- [Partial (patch) updates](#partial-patch-updates)
- [Transactions](#transactions)
- [Unit testing with the in-memory repository](#unit-testing-with-the-in-memory-repository)
- [Gotchas](#gotchas)

---

## Install

```bash
dotnet add package CloudMesh.Persistence.DynamoDB
```

## Register

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddDynamoDBPersistence(); // registers IAmazonDynamoDB + IRepositoryFactory (scoped)
```

Set the environment variable `USE_LOCAL_DYNAMODB=1` to point the client at a local DynamoDB
(`http://localhost:8000`) for development.

Then inject `IRepositoryFactory` and get a repository per table:

```csharp
public sealed class OrderService(IRepositoryFactory factory)
{
    private IRepository<Order> Orders => factory.For<Order>("orders");
}
```

## Define an entity

Entities are mapped with the standard AWS `[DynamoDB*]` attributes:

```csharp
using Amazon.DynamoDBv2.DataModel;

public sealed class Order
{
    [DynamoDBHashKey]  public string CustomerId { get; set; } = "";
    [DynamoDBRangeKey] public string OrderId { get; set; } = "";
    public decimal Total { get; set; }
    public string Status { get; set; } = "pending";

    [DynamoDBGlobalSecondaryIndexHashKey("status-index")]
    public string StatusIndexKey => Status;
}
```

## Read and write

```csharp
var repo = factory.For<Order>("orders");

// Create only if absent (returns false if it already existed).
bool created = await repo.CreateAsync(order, ct);

// Upsert.
await repo.SaveAsync(order, ct);

// Point reads (hash, or hash + range).
Order? one = await repo.GetById("cust-1", "order-9", ct);
Order[] many = await repo.GetByIds(ct, "cust-1", "cust-2");

// Delete by key or by entity.
await repo.DeleteAsync("cust-1", "order-9", ct);
```

## Fluent queries and secondary indexes

`Query()` builds a key-condition query; `ToAsyncEnumerable` pages transparently.

```csharp
// Query the base table by partition key + sort-key condition.
Order[] recent = await repo.Query()
    .WithHashKey("cust-1")
    .WithSortKey(QueryOperator.BeginsWith, "2026-")
    .Reverse()                       // newest first
    .ToArrayAsync(ct);

// Query a global secondary index.
await foreach (var o in repo.Query()
    .UseIndex("status-index")
    .WithHashKey("pending")
    .ToAsyncEnumerable(ct))
{
    // ...
}
```

Use `Scan()` for non-key filters (reads the whole table — prefer `Query` when you have a key):

```csharp
Order[] big = await repo.Scan()
    .Where(o => o.Total, ScanOperator.GreaterThan, 1000m)
    .ToArrayAsync(ct);
```

## Partial (patch) updates

Patch a single item in place — no read-modify-write — with optional conditional (`If`) checks for optimistic
concurrency. A failed condition returns `false`/`null` instead of throwing:

```csharp
bool ok = await repo.Patch("cust-1", "order-9")
    .Set(o => o.Status, "shipped")
    .Increment(o => o.Total, 5m)
    .If(o => o.Status, PatchCondition.Equals, "pending") // only if still pending
    .ExecuteAsync(ct);

// Or get the updated item back:
Order? updated = await repo.Patch("cust-1", "order-9")
    .Set(o => o.Status, "cancelled")
    .ExecuteAndGetAsync(ct);
```

## Transactions

`IRepositoryFactory.Transaction()` writes multiple items across tables atomically. `ExecuteAsync` returns `false`
if any conditional check failed (nothing was written):

```csharp
bool committed = await factory.Transaction()
    .Save("orders", order)
    .Patch<Inventory>("inventory", sku)
        .Decrement(i => i.OnHand, 1)
        .If(i => i.OnHand, PatchCondition.GreaterThanOrEqual, 1)
        .Build()
    .ExecuteAsync(ct);
```

## Unit testing with the in-memory repository

`InMemoryRepositoryFactory` implements the same `IRepositoryFactory` contract entirely in memory — keys,
conditional patches, transactions, queries and scans — so tests run with no AWS, network or emulator:

```csharp
using CloudMesh.Persistence.DynamoDB.Mock;

[Fact]
public async Task Ships_only_pending_orders()
{
    IRepositoryFactory factory = new InMemoryRepositoryFactory();
    var repo = factory.For<Order>("orders");
    await repo.SaveAsync(new Order { CustomerId = "c1", OrderId = "o1", Status = "pending" }, default);

    var sut = new OrderService(factory);
    await sut.ShipAsync("c1", "o1");

    var stored = await repo.GetById("c1", "o1", default);
    Assert.Equal("shipped", stored!.Status);
}
```

Inject `IRepositoryFactory` throughout your code and swap in `InMemoryRepositoryFactory` in tests and
`DynamoDBRepositoryFactory` (via `AddDynamoDBPersistence`) in production.

## Gotchas

- Keys are passed as `DynamoDBValue`, which has implicit conversions from the common scalar types — pass plain
  literals (`repo.GetById("c1", ct)`).
- `Scan()` reads the entire table; use `Query()` with a key condition whenever possible.
- Consistent reads are not valid on global secondary indexes (the builders throw if you combine them).
- Conditional (`If`) failures on `Patch`/`Transaction` are surfaced as `false`/`null`, not exceptions — check the
  result.
- Batch writes (`BatchWrite()`) are chunked into DynamoDB's 25-item limit automatically, but are **not**
  transactional — use `Transaction()` when atomicity matters.
- The in-memory implementation aims to mirror real behavior for tests, but is not a byte-for-byte DynamoDB
  emulator (a few filter paths on the in-memory query builder are simplified).

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
