using MediatR;

namespace MediatorBenchmarks.Libraries.MediatRLib;

// --- Scenario 1: Send, no behaviors ---
public sealed record Ping(string Message) : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken)
        => Task.FromResult(request.Message);
}

// --- Scenario 2: Publish to 3 handlers ---
public sealed record Pinged(string Message) : INotification;

public sealed class PingedHandler1 : INotificationHandler<Pinged>
{
    public Task Handle(Pinged notification, CancellationToken cancellationToken)
    {
        Counters.Handler1++;
        return Task.CompletedTask;
    }
}

public sealed class PingedHandler2 : INotificationHandler<Pinged>
{
    public Task Handle(Pinged notification, CancellationToken cancellationToken)
    {
        Counters.Handler2++;
        return Task.CompletedTask;
    }
}

public sealed class PingedHandler3 : INotificationHandler<Pinged>
{
    public Task Handle(Pinged notification, CancellationToken cancellationToken)
    {
        Counters.Handler3++;
        return Task.CompletedTask;
    }
}

internal static class Counters
{
    public static long Handler1;
    public static long Handler2;
    public static long Handler3;
}

// --- Scenario 3: Send with 2 pipeline behaviors ---
public sealed record PingWithBehaviors(string Message) : IRequest<string>;

public sealed class PingWithBehaviorsHandler : IRequestHandler<PingWithBehaviors, string>
{
    public Task<string> Handle(PingWithBehaviors request, CancellationToken cancellationToken)
        => Task.FromResult(request.Message);
}

// MediatR 12.x IPipelineBehavior signature: Handle(request, next, ct).
public sealed class Behavior1 : IPipelineBehavior<PingWithBehaviors, string>
{
    public Task<string> Handle(PingWithBehaviors request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        => next(cancellationToken);
}

public sealed class Behavior2 : IPipelineBehavior<PingWithBehaviors, string>
{
    public Task<string> Handle(PingWithBehaviors request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        => next(cancellationToken);
}
