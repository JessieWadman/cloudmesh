using System.ComponentModel;

namespace CloudMesh.Mediator;

/// <summary>Sends requests (single-response) and streams (multi-response) to their handlers.</summary>
/// <remarks>
/// The interface exposes only the box-free generic primitives (two type parameters). Those can't be inferred
/// from a single request argument (<c>TResponse</c> lives only in the constraint), so they never shadow the
/// ergonomic extension overloads. The ergonomic, single-argument entry points are extension methods:
/// the source generator emits an exact-typed <c>SendAsync(in ConcreteRequest)</c> per request type (box-free,
/// and more specific than any interface conversion, so it wins overload resolution), and
/// <see cref="SenderExtensions"/> provides a hand-written boxing fallback for request types the generator
/// didn't see. The runtime-typed dispatch used by that fallback is <see cref="InternalSendDynamicAsync{TResponse}"/>.
/// </remarks>
public interface ISender
{
    /// <summary>
    /// Sends a request to its handler with no boxing. The source generator emits per-request ergonomic overloads
    /// that call this, so callers get both inference and the box-free path.
    /// </summary>
    ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;

    /// <summary>
    /// Sends a request whose static type is only <see cref="IRequest{TResponse}"/> (boxing a value-type request once)
    /// and dispatches on its runtime type. This is the fallback used by the ergonomic <c>SendAsync</c> extension when
    /// no source-generated box-free overload exists for the concrete request type.
    /// </summary>
    /// <remarks>
    /// Do not call these directly.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    ValueTask<TResponse> InternalSendDynamicAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>Streams a request's responses with no boxing.</summary>
    IAsyncEnumerable<TResponse> StreamAsync<TRequest, TResponse>(in TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IStreamRequest<TResponse>;

    /// <summary>
    /// Streams a request whose static type is only <see cref="IStreamRequest{TResponse}"/> (boxing once) and dispatches
    /// on its runtime type. Fallback used by the ergonomic <c>StreamAsync</c> extension.
    /// </summary>
    /// <remarks>
    /// Do not call these directly.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    IAsyncEnumerable<TResponse> InternalStreamDynamicAsync<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);
}

/// <summary>Publishes notifications to all registered handlers (in-process).</summary>
public interface IPublisher
{
    /// <summary>Publishes a notification to all handlers. Box-free for value-type notifications.</summary>
    ValueTask PublishAsync<TNotification>(in TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Publishes a notification whose concrete type is known only at runtime.
    /// Dispatch uses the runtime type of <paramref name="notification"/>.
    /// </summary>
    ValueTask PublishAsync(object notification, CancellationToken cancellationToken = default);
}

/// <summary>The mediator: sends requests/streams and publishes notifications in-process.</summary>
public interface IMediator : ISender, IPublisher
{
}
