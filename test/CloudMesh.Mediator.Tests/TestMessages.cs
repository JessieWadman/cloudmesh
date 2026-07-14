using System.Runtime.CompilerServices;
using CloudMesh.Mediator.Distributed;

namespace CloudMesh.Mediator.Tests;

/// <summary>Shared, thread-safe sink so handlers/behaviors can record what ran and in what order.</summary>
public sealed class Recorder
{
    private readonly List<string> events = new();
    public int Counter;

    public void Add(string message)
    {
        lock (events) events.Add(message);
    }

    public IReadOnlyList<string> Events
    {
        get { lock (events) return events.ToArray(); }
    }
}

// ---- Basic request/response ------------------------------------------------

public sealed record Ping(string Message) : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public ValueTask<string> HandleAsync(Ping request, CancellationToken cancellationToken)
        => new("Pong: " + request.Message);
}

public sealed record Add(int A, int B) : IRequest<int>;

public sealed class AddHandler : IRequestHandler<Add, int>
{
    public ValueTask<int> HandleAsync(Add request, CancellationToken cancellationToken)
        => new(request.A + request.B);
}

// Value-type request — box-free fast path.
public readonly record struct StructAdd(int A, int B) : IRequest<int>;

public sealed class StructAddHandler : IRequestHandler<StructAdd, int>
{
    public ValueTask<int> HandleAsync(StructAdd request, CancellationToken cancellationToken)
        => new(request.A + request.B);
}

// Value-type request used by the allocation test; handler is registered as a singleton there.
public readonly record struct FastPing(int Value) : IRequest<int>;

public sealed class FastPingHandler : IRequestHandler<FastPing, int>
{
    public ValueTask<int> HandleAsync(FastPing request, CancellationToken cancellationToken)
        => new(request.Value);
}

// ---- Void command (IRequest / Unit) ---------------------------------------

public sealed record DoWork(string Label) : IRequest;

public sealed class DoWorkHandler : IRequestHandler<DoWork>
{
    private readonly Recorder recorder;
    public DoWorkHandler(Recorder recorder) => this.recorder = recorder;

    public ValueTask HandleAsync(DoWork request, CancellationToken cancellationToken)
    {
        recorder.Add("work:" + request.Label);
        return default;
    }
}

// ---- Request with no handler (diagnostic path) ----------------------------

public sealed record Orphan(string Message) : IRequest<string>;

// ---- Cancellation ----------------------------------------------------------

public sealed record CancelSensitive(int Value) : IRequest<int>;

public sealed class CancelSensitiveHandler : IRequestHandler<CancelSensitive, int>
{
    public ValueTask<int> HandleAsync(CancelSensitive request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<int>(request.Value);
    }
}

// ---- Pipeline behaviors (isolated to the Tracked request) -----------------

public sealed record Tracked(string Message) : IRequest<string>;

public sealed class TrackedHandler : IRequestHandler<Tracked, string>
{
    private readonly Recorder recorder;
    public TrackedHandler(Recorder recorder) => this.recorder = recorder;

    public ValueTask<string> HandleAsync(Tracked request, CancellationToken cancellationToken)
    {
        recorder.Add("handler");
        return new ValueTask<string>("handled:" + request.Message);
    }
}

public sealed class FirstBehavior : IPipelineBehavior<Tracked, string>
{
    private readonly Recorder recorder;
    public FirstBehavior(Recorder recorder) => this.recorder = recorder;

    public async ValueTask<string> HandleAsync(Tracked request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
    {
        recorder.Add("first:before");
        var result = await next();
        recorder.Add("first:after");
        return result;
    }
}

public sealed class SecondBehavior : IPipelineBehavior<Tracked, string>
{
    private readonly Recorder recorder;
    public SecondBehavior(Recorder recorder) => this.recorder = recorder;

    public async ValueTask<string> HandleAsync(Tracked request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
    {
        recorder.Add("second:before");
        var result = await next();
        recorder.Add("second:after");
        return result;
    }
}

// ---- Short-circuiting behavior --------------------------------------------

public sealed record Guarded(bool Allow) : IRequest<string>;

public sealed class GuardedHandler : IRequestHandler<Guarded, string>
{
    public ValueTask<string> HandleAsync(Guarded request, CancellationToken cancellationToken)
        => new("handled");
}

public sealed class GuardBehavior : IPipelineBehavior<Guarded, string>
{
    public ValueTask<string> HandleAsync(Guarded request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        => request.Allow ? next() : new ValueTask<string>("blocked");
}

// ---- Streaming -------------------------------------------------------------

public sealed record CountUp(int Count) : IStreamRequest<int>;

public sealed class CountUpHandler : IStreamRequestHandler<CountUp, int>
{
    public async IAsyncEnumerable<int> HandleAsync(CountUp request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
            await Task.Yield();
        }
    }
}

// Stream request with a doubling behavior applied.
public sealed record CountUpDoubled(int Count) : IStreamRequest<int>;

public sealed class CountUpDoubledHandler : IStreamRequestHandler<CountUpDoubled, int>
{
    public async IAsyncEnumerable<int> HandleAsync(CountUpDoubled request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            yield return i;
            await Task.Yield();
        }
    }
}

public sealed class DoublingStreamBehavior : IStreamPipelineBehavior<CountUpDoubled, int>
{
    public async IAsyncEnumerable<int> HandleAsync(CountUpDoubled request, StreamHandlerDelegate<int> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var value in next().WithCancellation(cancellationToken))
            yield return value * 2;
    }
}

// ---- Open-generic pipeline behavior (isolated via a marker constraint) -----
// The behavior is constrained to IOpenGenericMarker so DI only closes it for marker requests, keeping it from
// affecting the ordering/short-circuit tests above. MS DI validates generic constraints when closing open generics.

public interface IOpenGenericMarker { }

public sealed record OpenGenTarget(int Value) : IRequest<int>, IOpenGenericMarker;

public sealed class OpenGenTargetHandler : IRequestHandler<OpenGenTarget, int>
{
    public ValueTask<int> HandleAsync(OpenGenTarget request, CancellationToken cancellationToken)
        => new(request.Value);
}

public sealed class OpenGenericCountingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IOpenGenericMarker
{
    private readonly Recorder recorder;
    public OpenGenericCountingBehavior(Recorder recorder) => this.recorder = recorder;

    public ValueTask<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        recorder.Add("open-generic:before");
        return next();
    }
}

// ---- Handler instance identity (for the singleton hot-path cache tests) ----
// The handler returns its OWN per-instance id so tests can assert whether the same instance served two calls
// (singleton cached), or a different instance per scope (scoped), or per call (transient).

public readonly record struct WhoAmI(int Ignored) : IRequest<Guid>;

public sealed class WhoAmIHandler : IRequestHandler<WhoAmI, Guid>
{
    private readonly Guid instanceId = Guid.NewGuid();
    public ValueTask<Guid> HandleAsync(WhoAmI request, CancellationToken cancellationToken)
        => new(instanceId);
}

// A request with NO behavior discovered by scanning; a behavior is added MANUALLY at runtime in one test to
// prove runtime-registered behaviors still run even under HandlerLifetime = Singleton.
public readonly record struct LateBehaviorRequest(string Message) : IRequest<string>;

public sealed class LateBehaviorRequestHandler : IRequestHandler<LateBehaviorRequest, string>
{
    public ValueTask<string> HandleAsync(LateBehaviorRequest request, CancellationToken cancellationToken)
        => new("handled:" + request.Message);
}

// Registered manually (not by scanning) in the runtime-behavior test.
public sealed class LateBehavior : IPipelineBehavior<LateBehaviorRequest, string>
{
    private readonly Recorder recorder;
    public LateBehavior(Recorder recorder) => this.recorder = recorder;
    public async ValueTask<string> HandleAsync(LateBehaviorRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
    {
        recorder.Add("late-behavior");
        return await next();
    }
}

// ---- Multi-response request (one request type, two response types) ---------
// Legal: the runtime dispatches on the (TRequest,TResponse) pair and DI keys on the closed handler interface,
// so a request implementing IRequest<int> AND IRequest<string> may have a distinct handler for each response.

public sealed record MultiResp(int Value) : IRequest<int>, IRequest<string>;

public sealed class MultiRespIntHandler : IRequestHandler<MultiResp, int>
{
    public ValueTask<int> HandleAsync(MultiResp request, CancellationToken cancellationToken)
        => new(request.Value);
}

public sealed class MultiRespStringHandler : IRequestHandler<MultiResp, string>
{
    public ValueTask<string> HandleAsync(MultiResp request, CancellationToken cancellationToken)
        => new("s:" + request.Value);
}

// ---- Notifications ---------------------------------------------------------

public sealed record SomethingHappened(string What) : INotification;

public sealed class NotificationHandlerA : INotificationHandler<SomethingHappened>
{
    private readonly Recorder recorder;
    public NotificationHandlerA(Recorder recorder) => this.recorder = recorder;

    public ValueTask HandleAsync(SomethingHappened notification, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref recorder.Counter);
        recorder.Add("A:" + notification.What);
        return default;
    }
}

public sealed class NotificationHandlerB : INotificationHandler<SomethingHappened>
{
    private readonly Recorder recorder;
    public NotificationHandlerB(Recorder recorder) => this.recorder = recorder;

    public ValueTask HandleAsync(SomethingHappened notification, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref recorder.Counter);
        recorder.Add("B:" + notification.What);
        return default;
    }
}

// Notification with no handlers registered.
public sealed record Unheard(string What) : INotification;

// ---- Distributed seam ------------------------------------------------------

[DistributedNotification("orders.created")]
public sealed record OrderCreated(long OrderId) : INotification;
