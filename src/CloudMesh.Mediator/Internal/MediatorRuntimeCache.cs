using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace CloudMesh.Mediator.Internal;

/// <summary>
/// A cached, resolved dispatch plan for a send (request, response) pair. One dictionary lookup replaces the
/// separate handler-resolve + behavior-presence lookup on the hot path.
/// </summary>
internal readonly struct SendPlan
{
    /// <summary>Whether any pipeline behavior exists for this (request, response) pair (runtime-detected).</summary>
    public readonly bool HasBehaviors;

    /// <summary>
    /// The resolved handler instance, cached ONLY when the container's handler lifetime is
    /// <see cref="ServiceLifetime.Singleton"/> AND there are no behaviors. Null otherwise (transient/scoped
    /// handlers, or behavior pipelines, are resolved per call). When non-null it is an
    /// <c>IRequestHandler&lt;TRequest,TResponse&gt;</c> for this pair.
    /// </summary>
    public readonly object? SingletonHandler;

    public SendPlan(bool hasBehaviors, object? singletonHandler)
    {
        HasBehaviors = hasBehaviors;
        SingletonHandler = singletonHandler;
    }
}

/// <summary>
/// Per-container runtime cache used by <see cref="Mediator"/>. Registered as a singleton so its
/// caches are shared across all resolutions from one container, yet isolated between containers
/// (tests build many providers). Holds the behavior-presence flag caches and the dynamic-dispatch
/// wrapper caches keyed by runtime request/notification type.
/// </summary>
internal sealed class MediatorRuntimeCache
{
    public MediatorRuntimeCache()
    {
    }

    public MediatorRuntimeCache(ServiceLifetime handlerLifetime)
    {
        HandlerLifetime = handlerLifetime;
    }

    /// <summary>
    /// The lifetime handlers were registered with (from <see cref="MediatorOptions.HandlerLifetime"/>). The send
    /// hot path caches a resolved handler INSTANCE only when this is <see cref="ServiceLifetime.Singleton"/>, so
    /// the default (Transient) behavior is unchanged. Defaults to Transient when unknown.
    /// </summary>
    public ServiceLifetime HandlerLifetime { get; } = ServiceLifetime.Transient;

    /// <summary>
    /// Consolidated send hot-path cache: one lookup yields both the behavior flag and, for the singleton +
    /// no-behavior case, the resolved handler instance. This replaces a separate handler-resolve + behavior-flag
    /// lookup on the send path. A <see cref="ValueTuple{T1,T2}"/> of two <see cref="Type"/>s is
    /// <see cref="IEquatable{T}"/>, so the key does not box on <c>TryGetValue</c>.
    /// </summary>
    public ConcurrentDictionary<(Type Request, Type Response), SendPlan> SendPlans { get; } = new();

    /// <summary>
    /// Stream analogue of <see cref="SendPlans"/> (the cached handler, when present, is an
    /// <c>IStreamRequestHandler&lt;TRequest,TResponse&gt;</c>). Kept separate from <see cref="SendPlans"/> so a
    /// request type that is both a send- and a stream-request can never collide on the shared key.
    /// </summary>
    public ConcurrentDictionary<(Type Request, Type Response), SendPlan> StreamPlans { get; } = new();

    /// <summary>Caches whether any notification handlers exist for a notification type.</summary>
    public ConcurrentDictionary<Type, bool> NotificationHandlerPresence { get; } = new();

    /// <summary>Wrappers for the object-typed <c>SendAsync(IRequest&lt;TResponse&gt;)</c> path, keyed by runtime request type.</summary>
    public ConcurrentDictionary<Type, object> RequestWrappers { get; } = new();

    /// <summary>Wrappers for the object-typed <c>StreamAsync(IStreamRequest&lt;TResponse&gt;)</c> path, keyed by runtime request type.</summary>
    public ConcurrentDictionary<Type, object> StreamWrappers { get; } = new();

    /// <summary>Wrappers for the object-typed <c>PublishAsync(object)</c> path, keyed by runtime notification type.</summary>
    public ConcurrentDictionary<Type, NotificationWrapper> NotificationWrappers { get; } = new();
}
