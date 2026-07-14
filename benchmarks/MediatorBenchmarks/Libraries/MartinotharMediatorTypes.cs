using Mediator;

namespace MediatorBenchmarks.Libraries.MartinotharLib;

// --- Scenario 1: Send, no behaviors ---
public sealed record Ping(string Message) : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public ValueTask<string> Handle(Ping request, CancellationToken cancellationToken)
        => new(request.Message);
}

// --- Scenario 2: Publish to 3 handlers ---
public sealed record Pinged(string Message) : INotification;

public sealed class PingedHandler1 : INotificationHandler<Pinged>
{
    public ValueTask Handle(Pinged notification, CancellationToken cancellationToken)
    {
        Counters.Handler1++;
        return default;
    }
}

public sealed class PingedHandler2 : INotificationHandler<Pinged>
{
    public ValueTask Handle(Pinged notification, CancellationToken cancellationToken)
    {
        Counters.Handler2++;
        return default;
    }
}

public sealed class PingedHandler3 : INotificationHandler<Pinged>
{
    public ValueTask Handle(Pinged notification, CancellationToken cancellationToken)
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
public sealed record PingWithBehaviors(string Message) : IRequest<string>;

public sealed class PingWithBehaviorsHandler : IRequestHandler<PingWithBehaviors, string>
{
    public ValueTask<string> Handle(PingWithBehaviors request, CancellationToken cancellationToken)
        => new(request.Message);
}

// martinothamar Mediator 3.0 IPipelineBehavior: Handle(message, next, ct); next(message, ct).
// Behaviors are NOT auto-discovered; registered manually. Closed generics for parity with other libs.
public sealed class Behavior1 : IPipelineBehavior<PingWithBehaviors, string>
{
    public ValueTask<string> Handle(PingWithBehaviors message, MessageHandlerDelegate<PingWithBehaviors, string> next, CancellationToken cancellationToken)
        => next(message, cancellationToken);
}

public sealed class Behavior2 : IPipelineBehavior<PingWithBehaviors, string>
{
    public ValueTask<string> Handle(PingWithBehaviors message, MessageHandlerDelegate<PingWithBehaviors, string> next, CancellationToken cancellationToken)
        => next(message, cancellationToken);
}
