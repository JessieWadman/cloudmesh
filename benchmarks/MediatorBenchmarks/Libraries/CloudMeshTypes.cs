using CloudMesh.Mediator;

namespace MediatorBenchmarks.Libraries.CloudMeshLib;

// --- Scenario 1: Send, no behaviors ---
public readonly record struct Ping(string Message) : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public ValueTask<string> HandleAsync(Ping request, CancellationToken cancellationToken)
        => new(request.Message);
}

// --- Scenario 2: Publish to 3 handlers ---
public sealed record Pinged(string Message) : INotification;

public sealed class PingedHandler1 : INotificationHandler<Pinged>
{
    public ValueTask HandleAsync(Pinged notification, CancellationToken cancellationToken)
    {
        Counters.Handler1++;
        return default;
    }
}

public sealed class PingedHandler2 : INotificationHandler<Pinged>
{
    public ValueTask HandleAsync(Pinged notification, CancellationToken cancellationToken)
    {
        Counters.Handler2++;
        return default;
    }
}

public sealed class PingedHandler3 : INotificationHandler<Pinged>
{
    public ValueTask HandleAsync(Pinged notification, CancellationToken cancellationToken)
    {
        Counters.Handler3++;
        return default;
    }
}

internal static class Counters
{
    public static long Handler1;
    public static long Handler2;
    public static long Handler3;
}

// --- Scenario 3: Send with 2 pipeline behaviors ---
public readonly record struct PingWithBehaviors(string Message) : IRequest<string>;

public sealed class PingWithBehaviorsHandler : IRequestHandler<PingWithBehaviors, string>
{
    public ValueTask<string> HandleAsync(PingWithBehaviors request, CancellationToken cancellationToken)
        => new(request.Message);
}

public sealed class Behavior1 : IPipelineBehavior<PingWithBehaviors, string>
{
    public ValueTask<string> HandleAsync(PingWithBehaviors request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        => next();
}

public sealed class Behavior2 : IPipelineBehavior<PingWithBehaviors, string>
{
    public ValueTask<string> HandleAsync(PingWithBehaviors request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        => next();
}
