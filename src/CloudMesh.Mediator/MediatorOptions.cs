using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace CloudMesh.Mediator;

/// <summary>Configures mediator registration: which assemblies to scan, handler lifetime, and fan-out strategy.</summary>
public sealed class MediatorOptions
{
    internal List<Assembly> AssembliesToScan { get; } = new();

    /// <summary>
    /// Lifetime used for discovered handlers and behaviors. Defaults to <see cref="ServiceLifetime.Transient"/> (MediatR-compatible).
    /// </summary>
    /// <remarks>
    /// This is treated as the <b>uniform</b> lifetime for handlers. When set to <see cref="ServiceLifetime.Singleton"/>,
    /// the send hot path caches the resolved handler instance per request type to skip per-call resolution. That fast
    /// path assumes every request handler really is a singleton; if you set this to Singleton but then manually register
    /// a specific handler with a shorter lifetime, that handler may be cached and reused incorrectly. Keep handler
    /// lifetimes uniform, or leave this at the default Transient.
    /// </remarks>
    public ServiceLifetime HandlerLifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>Notification fan-out strategy. Defaults to <see cref="SequentialNotificationPublisher"/>.</summary>
    public Type NotificationPublisherType { get; set; } = typeof(SequentialNotificationPublisher);

    /// <summary>Registers all handlers/behaviors found in the assembly containing <typeparamref name="T"/>.</summary>
    public MediatorOptions RegisterServicesFromAssemblyContaining<T>()
        => RegisterServicesFromAssembly(typeof(T).Assembly);

    /// <summary>Registers all handlers/behaviors found in the given assembly.</summary>
    public MediatorOptions RegisterServicesFromAssembly(Assembly assembly)
    {
        if (!AssembliesToScan.Contains(assembly))
            AssembliesToScan.Add(assembly);
        return this;
    }
}
