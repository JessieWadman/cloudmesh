using System.Diagnostics.CodeAnalysis;
using CloudMesh.Mediator.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace CloudMesh.Mediator;

/// <summary>
/// Default in-process mediator. Resolves handlers and behaviors from an <see cref="IServiceProvider"/>.
/// Fast path (no behaviors) dispatches straight to the handler with no per-call allocation.
/// </summary>
public sealed class Mediator : IMediator
{
    private const string DynamicDispatchMessage =
        "The runtime-typed dispatch overloads use reflection (MakeGenericType/Activator) to build per-type wrappers. " +
        "Use the generic SendAsync/StreamAsync/PublishAsync overloads, or the source generator, for AOT/trim safety.";

    private readonly IServiceProvider services;
    private readonly INotificationPublisher notificationPublisher;
    private readonly MediatorRuntimeCache cache;

    internal Mediator(IServiceProvider services, INotificationPublisher notificationPublisher, MediatorRuntimeCache cache)
    {
        this.services = services;
        this.notificationPublisher = notificationPublisher;
        this.cache = cache;
    }

    public Mediator(IServiceProvider services, INotificationPublisher notificationPublisher)
        : this(services, notificationPublisher, services.GetService<MediatorRuntimeCache>() ?? new MediatorRuntimeCache())
    {
    }

    // ---- Requests ----------------------------------------------------------

    public ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // One lookup on the consolidated send hot-path cache yields the behavior flag and, for the singleton +
        // no-behavior case, the already-resolved handler instance (skipping GetService entirely).
        if (!cache.SendPlans.TryGetValue((typeof(TRequest), typeof(TResponse)), out var plan))
            return SendUncached<TRequest, TResponse>(request, cancellationToken);
        
        // Fastest path: singleton handler, no behaviors — call directly, no GetService, no behavior work.
        if (plan.SingletonHandler is IRequestHandler<TRequest, TResponse> cached)
            return cached.HandleAsync(request, cancellationToken);

        if (!plan.HasBehaviors)
        {
            // Transient/scoped handler with no behaviors: resolve per call (correct for those lifetimes), call directly.
            var h = services.GetService<IRequestHandler<TRequest, TResponse>>();
            if (h is null)
                return SendMiss<TRequest, TResponse>(request, cancellationToken);
            return h.HandleAsync(request, cancellationToken);
        }

        // Behaviors present: resolve handler + behaviors and run the pipeline.
        var behaviorHandler = services.GetService<IRequestHandler<TRequest, TResponse>>();
        if (behaviorHandler is null)
            return SendMiss<TRequest, TResponse>(request, cancellationToken);
        var behaviors = ResolveList(services.GetServices<IPipelineBehavior<TRequest, TResponse>>());
        return InvokeWithBehaviors(behaviorHandler, behaviors, request, cancellationToken);

    }

    /// <summary>
    /// First-call (cache miss) path: resolve the handler, detect behaviors at runtime (which correctly sees
    /// runtime-/cross-assembly-/open-generic-registered behaviors), build and store the <see cref="SendPlan"/>,
    /// then dispatch. A handler instance is cached ONLY when the container's handler lifetime is Singleton and
    /// there are no behaviors — so the default (Transient) behavior is unchanged and no captive dependency occurs.
    /// </summary>
    private ValueTask<TResponse> SendUncached<TRequest, TResponse>(in TRequest request, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var handler = services.GetService<IRequestHandler<TRequest, TResponse>>();
        if (handler is null)
            return SendMiss<TRequest, TResponse>(request, cancellationToken);

        var hasBehaviors = HasBehaviors<IPipelineBehavior<TRequest, TResponse>>();

        var cacheSingleton = !hasBehaviors && cache.HandlerLifetime == ServiceLifetime.Singleton;
        cache.SendPlans[(typeof(TRequest), typeof(TResponse))] =
            new SendPlan(hasBehaviors, cacheSingleton ? handler : null);

        if (!hasBehaviors)
            return handler.HandleAsync(request, cancellationToken);

        var behaviors = ResolveList(services.GetServices<IPipelineBehavior<TRequest, TResponse>>());
        return InvokeWithBehaviors(handler, behaviors, request, cancellationToken);
    }

    /// <summary>Handles a null handler resolution: fall back to runtime-typed dispatch for interface/abstract static types, else throw.</summary>
    private ValueTask<TResponse> SendMiss<TRequest, TResponse>(in TRequest request, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        // If the static type argument is an interface/abstract base (e.g. the caller used a variable typed as
        // IRequest<T>), fall back to runtime-typed dispatch on the concrete instance.
        if (typeof(TRequest).IsInterface || typeof(TRequest).IsAbstract)
            return InternalSendDynamicAsync<TResponse>(request, cancellationToken);
        throw new HandlerNotFoundException(typeof(TRequest));
    }

    private bool HasBehaviors<TBehavior>()
    {
        foreach (var _ in services.GetServices<TBehavior>())
            return true;
        return false;
    }

    private static IReadOnlyList<T> ResolveList<T>(IEnumerable<T> services)
        => services as IReadOnlyList<T> ?? services.ToArray();

    private static ValueTask<TResponse> InvokeWithBehaviors<TRequest, TResponse>(
        IRequestHandler<TRequest, TResponse> handler,
        IReadOnlyList<IPipelineBehavior<TRequest, TResponse>> behaviors,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        // TODO(source-gen): unroll to avoid per-behavior closures
        RequestHandlerDelegate<TResponse> next = () => handler.HandleAsync(request, cancellationToken);
        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var current = next;
            next = () => behavior.HandleAsync(request, current, cancellationToken);
        }
        return next();
    }

    [RequiresDynamicCode(DynamicDispatchMessage)]
    [RequiresUnreferencedCode(DynamicDispatchMessage)]
    public ValueTask<TResponse> InternalSendDynamicAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestType = request.GetType();
        var wrapper = (RequestWrapper<TResponse>)cache.RequestWrappers.GetOrAdd(requestType, static rt => CreateRequestWrapper<TResponse>(rt));
        return wrapper.HandleAsync(this, request, cancellationToken);
    }

    [RequiresDynamicCode(DynamicDispatchMessage)]
    [RequiresUnreferencedCode(DynamicDispatchMessage)]
    private static object CreateRequestWrapper<TResponse>(Type requestType)
    {
        var wrapperType = typeof(RequestWrapper<,>).MakeGenericType(requestType, typeof(TResponse));
        return Activator.CreateInstance(wrapperType)!;
    }

    // ---- Streams -----------------------------------------------------------

    public IAsyncEnumerable<TResponse> StreamAsync<TRequest, TResponse>(in TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IStreamRequest<TResponse>
    {
        // Same consolidated hot-path cache as SendAsync; a stream handler is instance-cached only for singletons.
        if (cache.StreamPlans.TryGetValue((typeof(TRequest), typeof(TResponse)), out var plan))
        {
            if (plan.SingletonHandler is IStreamRequestHandler<TRequest, TResponse> cached)
                return cached.HandleAsync(request, cancellationToken);

            var h = services.GetService<IStreamRequestHandler<TRequest, TResponse>>()
                    ?? throw new HandlerNotFoundException(typeof(TRequest));

            if (!plan.HasBehaviors)
                return h.HandleAsync(request, cancellationToken);

            return InvokeStreamWithBehaviors(h, request, cancellationToken);
        }

        return StreamUncached<TRequest, TResponse>(request, cancellationToken);
    }

    private IAsyncEnumerable<TResponse> StreamUncached<TRequest, TResponse>(in TRequest request, CancellationToken cancellationToken)
        where TRequest : IStreamRequest<TResponse>
    {
        var handler = services.GetService<IStreamRequestHandler<TRequest, TResponse>>()
                      ?? throw new HandlerNotFoundException(typeof(TRequest));

        var hasBehaviors = HasBehaviors<IStreamPipelineBehavior<TRequest, TResponse>>();

        var cacheSingleton = !hasBehaviors && cache.HandlerLifetime == ServiceLifetime.Singleton;
        cache.StreamPlans[(typeof(TRequest), typeof(TResponse))] =
            new SendPlan(hasBehaviors, cacheSingleton ? handler : null);

        if (!hasBehaviors)
            return handler.HandleAsync(request, cancellationToken);

        return InvokeStreamWithBehaviors(handler, request, cancellationToken);
    }

    private IAsyncEnumerable<TResponse> InvokeStreamWithBehaviors<TRequest, TResponse>(
        IStreamRequestHandler<TRequest, TResponse> handler, TRequest request, CancellationToken cancellationToken)
        where TRequest : IStreamRequest<TResponse>
    {
        var behaviors = ResolveList(services.GetServices<IStreamPipelineBehavior<TRequest, TResponse>>());
        var local = request;
        // TODO(source-gen): unroll to avoid per-behavior closures
        StreamHandlerDelegate<TResponse> next = () => handler.HandleAsync(local, cancellationToken);
        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var current = next;
            next = () => behavior.HandleAsync(local, current, cancellationToken);
        }
        return next();
    }

    [RequiresDynamicCode(DynamicDispatchMessage)]
    [RequiresUnreferencedCode(DynamicDispatchMessage)]
    public IAsyncEnumerable<TResponse> InternalStreamDynamicAsync<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestType = request.GetType();
        var wrapper = (StreamWrapper<TResponse>)cache.StreamWrappers.GetOrAdd(requestType, static rt => CreateStreamWrapper<TResponse>(rt));
        return wrapper.HandleAsync(this, request, cancellationToken);
    }

    [RequiresDynamicCode(DynamicDispatchMessage)]
    [RequiresUnreferencedCode(DynamicDispatchMessage)]
    private static object CreateStreamWrapper<TResponse>(Type requestType)
    {
        var wrapperType = typeof(StreamWrapper<,>).MakeGenericType(requestType, typeof(TResponse));
        return Activator.CreateInstance(wrapperType)!;
    }

    // ---- Notifications -----------------------------------------------------

    public ValueTask PublishAsync<TNotification>(in TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (!cache.NotificationHandlerPresence.TryGetValue(typeof(TNotification), out var hasHandlers))
        {
            hasHandlers = HasNotificationHandlers<TNotification>();
            cache.NotificationHandlerPresence[typeof(TNotification)] = hasHandlers;
        }

        if (!hasHandlers)
        {
            // No handlers found for the static type. If that static type is an interface/abstract base
            // (e.g. the caller published through an INotification-typed variable), the concrete instance
            // may still have handlers — route to the runtime-typed path.
            if (typeof(TNotification).IsInterface || typeof(TNotification).IsAbstract)
                return PublishAsync((object)notification!, cancellationToken);

            // Concrete type with no handlers: a legitimate no-op.
            return default;
        }

        var handlers = ResolveList(services.GetServices<INotificationHandler<TNotification>>());
        return notificationPublisher.PublishAsync(handlers, notification, cancellationToken);
    }

    private bool HasNotificationHandlers<TNotification>()
        where TNotification : INotification
    {
        foreach (var _ in services.GetServices<INotificationHandler<TNotification>>())
            return true;
        return false;
    }

    [RequiresDynamicCode(DynamicDispatchMessage)]
    [RequiresUnreferencedCode(DynamicDispatchMessage)]
    public ValueTask PublishAsync(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var notificationType = notification.GetType();
        var wrapper = cache.NotificationWrappers.GetOrAdd(notificationType, static nt => CreateNotificationWrapper(nt));
        return wrapper.HandleAsync(this, notification, cancellationToken);
    }

    [RequiresDynamicCode(DynamicDispatchMessage)]
    [RequiresUnreferencedCode(DynamicDispatchMessage)]
    private static NotificationWrapper CreateNotificationWrapper(Type notificationType)
    {
        var wrapperType = typeof(NotificationWrapper<>).MakeGenericType(notificationType);
        return (NotificationWrapper)Activator.CreateInstance(wrapperType)!;
    }
}
