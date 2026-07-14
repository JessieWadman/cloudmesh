# CloudMesh.Mediator

A fast, allocation-conscious **mediator** for in-process CQRS on .NET — send requests, stream responses, and
publish notifications, with a pipeline for cross-cutting behaviors.

It's a drop-in-minded alternative to MediatR (which moved to commercial licensing), built around a **Roslyn
source generator** so dispatch is **box-free** and registration is **reflection-free** (AOT/trim-friendly). The
generator ships inside this package as an analyzer, so the box-free overloads and compile-time diagnostics work
automatically once you reference it.

- **Targets:** .NET 8, 9, 10 — **License:** MIT
- `ValueTask`-based throughout; zero allocation on the box-free send path and on notification fan-out.
- MediatR-compatible shims so most existing code ports by swapping `using` directives.

---

## Table of contents

- [Install](#install)
- [Quick start](#quick-start)
- [Core concepts](#core-concepts)
  - [Requests and responses](#requests-and-responses)
  - [Commands (no response)](#commands-no-response)
  - [Streaming requests](#streaming-requests)
  - [Notifications](#notifications)
  - [Pipeline behaviors](#pipeline-behaviors)
- [Registration](#registration)
- [The box-free fast path](#the-box-free-fast-path)
- [Compile-time diagnostics](#compile-time-diagnostics)
- [Distributed notifications](#distributed-notifications)
- [Migrating from MediatR](#migrating-from-mediatr)
- [FAQ](#faq)

---

## Install

```bash
dotnet add package CloudMesh.Mediator
```

That's all you need — the source generator is bundled as an analyzer.

## Quick start

**1. Define a request and its handler.**

```csharp
using CloudMesh.Mediator;

// A `readonly record struct` request gets the zero-allocation path (see "The box-free fast path").
public readonly record struct GetUser(int Id) : IRequest<User>;

public sealed class GetUserHandler : IRequestHandler<GetUser, User>
{
    private readonly IUserStore _store;
    public GetUserHandler(IUserStore store) => _store = store;

    public async ValueTask<User> HandleAsync(GetUser request, CancellationToken ct)
        => await _store.FindAsync(request.Id, ct);
}
```

**2. Register the mediator** (scans the assembly for handlers and behaviors):

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddCloudMeshMediator(o => o.RegisterServicesFromAssemblyContaining<GetUser>());
```

**3. Send.** Inject `IMediator` (or the narrower `ISender` / `IPublisher`):

```csharp
public sealed class UsersController(IMediator mediator)
{
    public async Task<User> Get(int id) => await mediator.SendAsync(new GetUser(id));
}
```

With the generator referenced (it is, by default), `mediator.SendAsync(new GetUser(id))` binds a generated,
box-free overload automatically — no boxing, no reflection.

---

## Core concepts

### Requests and responses

A **request** implements `IRequest<TResponse>` and is handled by **exactly one** `IRequestHandler<TRequest, TResponse>`:

```csharp
public sealed record CreateOrder(int CustomerId, string Sku) : IRequest<int>;   // returns the new order id

public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, int>
{
    public ValueTask<int> HandleAsync(CreateOrder request, CancellationToken ct)
        => /* ... */ new ValueTask<int>(orderId);
}

int orderId = await mediator.SendAsync(new CreateOrder(customerId: 7, sku: "ABC"));
```

Requests can be classes, records, or structs. Structs (especially `readonly record struct`) are recommended for
hot paths — see [the box-free fast path](#the-box-free-fast-path).

### Commands (no response)

For a command that returns nothing, implement the marker `IRequest` (equivalent to `IRequest<NoResponse>`) and
the single-parameter `IRequestHandler<TRequest>`:

```csharp
public sealed record DeactivateUser(int Id) : IRequest;

public sealed class DeactivateUserHandler : IRequestHandler<DeactivateUser>
{
    public ValueTask HandleAsync(DeactivateUser request, CancellationToken ct)
    {
        // ...
        return default;   // completed ValueTask
    }
}

await mediator.SendAsync(new DeactivateUser(42));
```

`NoResponse` is the "unit" type used internally so commands don't have to special-case `void`.

### Streaming requests

A **stream request** implements `IStreamRequest<TResponse>` and produces an `IAsyncEnumerable<TResponse>`:

```csharp
using System.Runtime.CompilerServices;

public sealed record TailLog(string Path) : IStreamRequest<string>;

public sealed class TailLogHandler : IStreamRequestHandler<TailLog, string>
{
    public async IAsyncEnumerable<string> HandleAsync(
        TailLog request, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var line in ReadLinesAsync(request.Path, ct))
            yield return line;
    }
}

await foreach (var line in mediator.StreamAsync(new TailLog("app.log"), ct))
    Console.WriteLine(line);
```

### Notifications

A **notification** implements `INotification` and is delivered to **zero or more** `INotificationHandler<TNotification>`:

```csharp
public sealed record OrderPlaced(int OrderId) : INotification;

public sealed class SendConfirmationEmail : INotificationHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced n, CancellationToken ct) => /* ... */ default;
}

public sealed class UpdateInventory : INotificationHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced n, CancellationToken ct) => /* ... */ default;
}

await mediator.PublishAsync(new OrderPlaced(orderId));   // both handlers run
```

Fan-out strategy is pluggable via `MediatorOptions.NotificationPublisherType`:

| Publisher | Behavior |
|---|---|
| `SequentialNotificationPublisher` *(default)* | Awaits each handler in registration order; a throwing handler stops the rest. |
| `ParallelNotificationPublisher` | Starts all handlers, awaits them together (`Task.WhenAll`), aggregating failures. |

Publishing a notification with no handlers is a no-op. If you only know the notification's type at runtime, use
`PublishAsync(object notification)` — it dispatches on the runtime type.

### Pipeline behaviors

Behaviors wrap request handling for cross-cutting concerns (validation, logging, retries, transactions). They run
in registration order, each calling `next()` to invoke the rest of the pipeline — and may short-circuit by
**not** calling `next`:

```csharp
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger _log;
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> log) => _log = log;

    public async ValueTask<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        _log.LogInformation("Handling {Request}", typeof(TRequest).Name);
        var response = await next();
        _log.LogInformation("Handled {Request}", typeof(TRequest).Name);
        return response;
    }
}
```

Both closed (`IPipelineBehavior<GetUser, User>`) and **open-generic** (`LoggingBehavior<TRequest, TResponse>`)
behaviors are discovered and registered automatically. There's an `IStreamPipelineBehavior<,>` for streams too.

> Behaviors are the one place that allocates: composing the `next` chain requires a closure per behavior. If you
> want the leanest possible hot path, keep behavior-free requests behavior-free.

---

## Registration

Two ways to register — pick one:

**1. Assembly scanning** (familiar, MediatR-like). Uses reflection at startup:

```csharp
services.AddCloudMeshMediator(o =>
{
    o.RegisterServicesFromAssemblyContaining<GetUser>();      // scan this assembly
    // o.RegisterServicesFromAssembly(someOtherAssembly);     // and/or others

    o.HandlerLifetime = ServiceLifetime.Transient;            // default; see note below
    o.NotificationPublisherType = typeof(SequentialNotificationPublisher);   // default
});
```

**2. Source-generated registration** (reflection-free, AOT/trim-clean). The generator emits a per-assembly method
named `AddCloudMeshMediatorGenerated<YourAssemblyName>()`:

```csharp
// e.g. in an assembly named "MyApp":
services.AddCloudMeshMediatorGeneratedMyApp();
```

The name is suffixed per assembly on purpose: each assembly registers its **own** handlers, and an app composes
them — `services.AddCloudMeshMediatorGeneratedContracts().AddCloudMeshMediatorGeneratedMyApp()`.

**Handler lifetime.** `HandlerLifetime` defaults to `Transient` (MediatR-compatible). Setting it to `Singleton`
enables a hot-path cache that resolves each handler once and calls it directly, shaving the per-call DI lookup.
When you use `Singleton`, register your handlers uniformly as singletons (see the XML docs on `HandlerLifetime`).

---

## The box-free fast path

The whole point of the library. `ISender` exposes only a generic, box-free primitive:

```csharp
ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, ...) where TRequest : IRequest<TResponse>;
```

You rarely call that directly. Instead, the source generator emits an **exact-typed** overload per request type:

```csharp
// generated:
public static ValueTask<int> SendAsync(this ISender sender, in CreateOrder request, CancellationToken ct = default);
```

Because that overload is more specific than the interface-typed one, `mediator.SendAsync(new CreateOrder(...))`
binds to it — so a `struct` request is **never boxed**, and dispatch avoids reflection. On this path a send
allocates **nothing** and completes in the low-nanosecond range. (A reflection-based mediator boxes the request,
resolves via a service-locator, and allocates on every send and publish.)

Guidelines for the fastest path:

- Prefer `readonly record struct` requests.
- Reference the package (the analyzer is included) so the generated overloads exist.
- Consider `HandlerLifetime = Singleton` for the hottest handlers.

If a box-free overload can't be found for a concrete request (e.g. the request type lives in an assembly that
didn't run the generator), the call falls back to a boxing path — and diagnostic **CMED200** points it out.

---

## Compile-time diagnostics

The generator/analyzer surface problems and suggestions at build time. All default to `Info` (except duplicate
handlers), so they never break a `TreatWarningsAsErrors` build; elevate them per-project via `.editorconfig`.

| ID | Default | Meaning |
|---|---|---|
| `CMED001` | Info | Request type has no handler in this compilation. |
| `CMED002` | **Error** | More than one handler for the same `(request, response)` pair. |
| `CMED003` | Info | Notification type has no handlers (publishing is a no-op). |
| `CMED100` | Info | Handler uses the MediatR-compat shape; the native shape is faster. |
| `CMED200` | Info | A send/stream call boxes a value-type request; a box-free overload is available. |

```ini
# .editorconfig — opt into stricter enforcement
dotnet_diagnostic.CMED100.severity = warning
dotnet_diagnostic.CMED200.severity = warning
```

Full reference: [docs/diagnostics.md](https://github.com/JessieWadman/cloudmesh/blob/main/docs/diagnostics.md).

---

## Distributed notifications

For out-of-process notification delivery, mark a notification with `[DistributedNotification]` and publish it
through `IDistributedPublisher`:

```csharp
using CloudMesh.Mediator.Distributed;

[DistributedNotification("orders.created")]
public sealed record OrderCreated(long OrderId) : INotification;
```

The core provides the seam — `INotificationTransport` (moves bytes), `INotificationSerializer` (bytes ⇆ message),
and `IDistributedPublisher` (subject + serialize + hand off). **Network transports (e.g. NATS/JetStream) plug in
as separate packages**; the core ships no transport, so remote delivery stays explicit and opt-in rather than
hidden behind the in-process `IPublisher`.

---

## Migrating from MediatR

Two paths. Both start by swapping the `using`:

```diff
- using MediatR;
+ using CloudMesh.Mediator;
```

### Path A — minimal (keep MediatR shapes)

Add `using CloudMesh.Mediator.Compatibility;`. Your MediatR-shaped handlers (`Handle` returning `Task`) and call
sites (`Send`/`Publish`/`CreateStream`) keep working via shims — the generator adapts them automatically:

```csharp
using CloudMesh.Mediator;
using CloudMesh.Mediator.Compatibility;

public sealed record Ping(string Msg) : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>   // Compatibility.IRequestHandler
{
    public Task<string> Handle(Ping request, CancellationToken ct) => Task.FromResult("Pong");
}

string pong = await mediator.Send(new Ping("hi"));   // Compatibility.Send extension → Task
```

`CMED100` will nudge each shim toward the native shape when you're ready.

### Path B — native (recommended)

Rename `Handle` → `HandleAsync`, `Task` → `ValueTask`, and drop the compat `using`. This removes the adapter and
a per-call `Task` allocation.

### API mapping

| MediatR | CloudMesh.Mediator (native) | Compat shim |
|---|---|---|
| `using MediatR;` | `using CloudMesh.Mediator;` | + `using CloudMesh.Mediator.Compatibility;` |
| `IRequest<T>`, `INotification`, `IStreamRequest<T>` | same | same |
| `IRequestHandler<T,R>.Handle(...) : Task<R>` | `.HandleAsync(...) : ValueTask<R>` | `Compatibility.IRequestHandler<T,R>` keeps `Handle`/`Task` |
| `INotificationHandler<T>.Handle(...) : Task` | `.HandleAsync(...) : ValueTask` | `Compatibility.INotificationHandler<T>` |
| `IPipelineBehavior<T,R>.Handle(req, next, ct)` | `.HandleAsync(req, next, ct)` (`ValueTask`, `next()` is parameterless) | — |
| `Unit` | `NoResponse` | `Compatibility.Unit` (implicitly converts to `NoResponse`) |
| `mediator.Send(x)` | `mediator.SendAsync(x)` | `mediator.Send(x)` → `Task` |
| `mediator.Publish(x)` | `mediator.PublishAsync(x)` | `mediator.Publish(x)` → `Task` |
| `mediator.CreateStream(x)` | `mediator.StreamAsync(x)` | `mediator.CreateStream(x)` |
| `services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(a))` | `services.AddCloudMeshMediator(o => o.RegisterServicesFromAssemblyContaining<T>())` | same |

> **Tip:** don't declare CloudMesh types in the `MediatR` namespace to avoid changing usings — if you reference
> both packages during an incremental migration, that would collide (`CS0433`). The `using` swap keeps both
> packages usable side by side while you port.

---

## FAQ

**Do I have to use the source generator?**
No — `AddCloudMeshMediator(...)` works standalone via reflection. But the generator (bundled by default) is what
gives you box-free ergonomic sends, reflection-free registration, and the compile-time diagnostics.

**Why is my request boxed even though I used the generator?**
The generated box-free overload is emitted in the assembly that **declares** the request type. If you send a
request whose type lives in an assembly that didn't run the generator, the call falls back to a boxing path —
`CMED200` flags exactly those sites.

**Sequential vs parallel notifications?**
Sequential (default) is predictable and cheap; a throwing handler stops the rest. Parallel runs all handlers and
aggregates failures. They have different failure semantics — choose deliberately.

**Is it AOT/trim-safe?**
The generated registration and dispatch are reflection-free and AOT/trim-clean. The reflection-based
`AddCloudMeshMediator(...)` and the runtime-typed dispatch overloads are annotated with
`[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`; use the generated registration for AOT.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
