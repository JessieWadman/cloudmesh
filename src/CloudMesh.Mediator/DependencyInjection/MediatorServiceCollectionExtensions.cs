using System.Diagnostics.CodeAnalysis;
using CloudMesh.Mediator.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace CloudMesh.Mediator;

/// <summary>DI registration for the mediator.</summary>
public static class MediatorServiceCollectionExtensions
{
    private const string ScanMessage =
        "Assembly scanning discovers handlers/behaviors via reflection and registers reflection-built adapters. " +
        "For AOT/trim scenarios use the source generator, which registers handlers without reflection.";

    /// <summary>
    /// Registers only the mediator infrastructure — <see cref="IMediator"/>/<see cref="ISender"/>/<see cref="IPublisher"/>,
    /// the per-container runtime cache and the notification publisher chosen in <paramref name="options"/> — WITHOUT any
    /// assembly scanning or reflection. Both the reflection-based <see cref="AddCloudMeshMediator"/> and the
    /// source-generated <c>AddCloudMeshMediatorGenerated</c> call this so infra registration lives in exactly one place.
    /// Handlers/behaviors are registered separately by the caller.
    /// </summary>
    public static IServiceCollection AddCloudMeshMediatorCore(this IServiceCollection services, MediatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // Notification fan-out strategy (singleton).
        services.AddSingleton(typeof(INotificationPublisher), options.NotificationPublisherType);

        // Per-container runtime cache (singleton) shared by the transient mediator instances. The handler
        // lifetime is captured so the send hot path may cache a resolved handler instance ONLY for singletons.
        var handlerLifetime = options.HandlerLifetime;
        services.AddSingleton(_ => new MediatorRuntimeCache(handlerLifetime));

        // The mediator itself; transient so the resolving scope's provider is injected and scoped handlers resolve.
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="IMediator"/>/<see cref="ISender"/>/<see cref="IPublisher"/> and scans the configured
    /// assemblies for handlers and pipeline behaviors.
    /// </summary>
    /// <remarks>
    /// The mediator is registered as transient with the resolving scope's <see cref="IServiceProvider"/> injected,
    /// so scoped handlers require resolving the mediator from a scope (not from the root provider) to observe the
    /// intended per-scope handler instances.
    /// </remarks>
    [RequiresDynamicCode(ScanMessage)]
    [RequiresUnreferencedCode(ScanMessage)]
    public static IServiceCollection AddCloudMeshMediator(this IServiceCollection services, Action<MediatorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MediatorOptions();
        configure(options);

        // Shared infrastructure registration (mediator, cache, notification publisher).
        services.AddCloudMeshMediatorCore(options);

        var lifetime = options.HandlerLifetime;

        // Track the closed request/stream handler service types already registered so a second handler for the
        // same request produces a clear error instead of silent last-wins. Maps service type -> implementing type.
        var requestHandlers = new Dictionary<Type, Type>();
        var streamHandlers = new Dictionary<Type, Type>();

        foreach (var assembly in options.AssembliesToScan)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract)
                    continue;

                // Open-generic pipeline behaviors (e.g. LoggingBehavior<TReq,TResp> : IPipelineBehavior<TReq,TResp>)
                // are registered as open-generic descriptors; MS DI closes them per request type at resolution.
                // Any other open-generic type cannot be registered.
                if (type.ContainsGenericParameters)
                {
                    RegisterOpenGenericBehaviors(services, type, lifetime);
                    continue;
                }

                foreach (var iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType)
                        continue;

                    var definition = iface.GetGenericTypeDefinition();

                    if (definition == typeof(IRequestHandler<,>))
                    {
                        EnsureSingleRequestHandler(requestHandlers, iface, type, "IRequestHandler");
                        services.Add(new ServiceDescriptor(iface, type, lifetime));
                    }
                    else if (definition == typeof(IStreamRequestHandler<,>))
                    {
                        EnsureSingleRequestHandler(streamHandlers, iface, type, "IStreamRequestHandler");
                        services.Add(new ServiceDescriptor(iface, type, lifetime));
                    }
                    else if (definition == typeof(INotificationHandler<>))
                    {
                        services.Add(new ServiceDescriptor(iface, type, lifetime));
                    }
                    else if (definition == typeof(IPipelineBehavior<,>))
                    {
                        services.Add(new ServiceDescriptor(iface, type, lifetime));
                    }
                    else if (definition == typeof(IStreamPipelineBehavior<,>))
                    {
                        services.Add(new ServiceDescriptor(iface, type, lifetime));
                    }
                    else if (definition == typeof(Compatibility.IRequestHandler<,>))
                    {
                        // Register the concrete compat handler under its own compat interface.
                        services.Add(new ServiceDescriptor(iface, type, lifetime));

                        // And register an adapter as the native IRequestHandler<TReq,TResp>.
                        var args = iface.GetGenericArguments();
                        var nativeService = typeof(IRequestHandler<,>).MakeGenericType(args);
                        var adapter = typeof(Compatibility.CompatRequestHandlerAdapter<,>).MakeGenericType(args);
                        EnsureSingleRequestHandler(requestHandlers, nativeService, type, "IRequestHandler");
                        services.Add(new ServiceDescriptor(nativeService, adapter, lifetime));
                    }
                    else if (definition == typeof(Compatibility.INotificationHandler<>))
                    {
                        services.Add(new ServiceDescriptor(iface, type, lifetime));

                        var args = iface.GetGenericArguments();
                        var nativeService = typeof(INotificationHandler<>).MakeGenericType(args);
                        var adapter = typeof(Compatibility.CompatNotificationHandlerAdapter<>).MakeGenericType(args);
                        services.Add(new ServiceDescriptor(nativeService, adapter, lifetime));
                    }
                }
            }
        }

        return services;
    }

    private static void RegisterOpenGenericBehaviors(IServiceCollection services, Type type, ServiceLifetime lifetime)
    {
        // The type must be an open generic whose behavior interface is closed over the type's OWN type parameters,
        // e.g. class LoggingBehavior<TReq,TResp> : IPipelineBehavior<TReq,TResp>. Register typeof(behavior<,>)
        // against typeof(IPipelineBehavior<,>).
        var ownParameters = type.GetGenericArguments();

        foreach (var iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType)
                continue;

            var definition = iface.GetGenericTypeDefinition();
            if (definition != typeof(IPipelineBehavior<,>) && definition != typeof(IStreamPipelineBehavior<,>))
                continue;

            // Interface args must be exactly the class's own type parameters (in some order).
            var ifaceArgs = iface.GetGenericArguments();
            var allOwn = ifaceArgs.Length == ownParameters.Length;
            if (allOwn)
            {
                foreach (var arg in ifaceArgs)
                {
                    if (!arg.IsGenericParameter || arg.DeclaringType != type)
                    {
                        allOwn = false;
                        break;
                    }
                }
            }
            if (!allOwn)
                continue;

            // Register the OPEN definition: typeof(IPipelineBehavior<,>) -> typeof(Behavior<,>).
            services.Add(new ServiceDescriptor(definition, type.GetGenericTypeDefinition(), lifetime));
        }
    }

    private static void EnsureSingleRequestHandler(Dictionary<Type, Type> seen, Type serviceType, Type implementationType, string kind)
    {
        if (seen.TryGetValue(serviceType, out var existing))
        {
            var requestType = serviceType.GetGenericArguments()[0];
            throw new InvalidOperationException(
                $"Multiple {kind} handlers registered for request type '{requestType}': " +
                $"'{existing}' and '{implementationType}'. Exactly one handler is allowed per request type.");
        }
        seen[serviceType] = implementationType;
    }
}
