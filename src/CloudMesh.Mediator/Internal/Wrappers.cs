namespace CloudMesh.Mediator.Internal;

/// <summary>
/// Non-generic-over-request wrapper for the dynamic <c>SendAsync(IRequest&lt;TResponse&gt;)</c> path.
/// One instance is created per runtime request type and cached; it casts to the concrete request
/// type and calls the box-free generic fast path.
/// </summary>
internal abstract class RequestWrapper<TResponse>
{
    public abstract ValueTask<TResponse> HandleAsync(Mediator mediator, IRequest<TResponse> request, CancellationToken cancellationToken);
}

internal sealed class RequestWrapper<TRequest, TResponse> : RequestWrapper<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override ValueTask<TResponse> HandleAsync(Mediator mediator, IRequest<TResponse> request, CancellationToken cancellationToken)
        => mediator.SendAsync<TRequest, TResponse>((TRequest)request, cancellationToken);
}

/// <summary>Wrapper for the dynamic <c>StreamAsync(IStreamRequest&lt;TResponse&gt;)</c> path.</summary>
internal abstract class StreamWrapper<TResponse>
{
    public abstract IAsyncEnumerable<TResponse> HandleAsync(Mediator mediator, IStreamRequest<TResponse> request, CancellationToken cancellationToken);
}

internal sealed class StreamWrapper<TRequest, TResponse> : StreamWrapper<TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    public override IAsyncEnumerable<TResponse> HandleAsync(Mediator mediator, IStreamRequest<TResponse> request, CancellationToken cancellationToken)
        => mediator.StreamAsync<TRequest, TResponse>((TRequest)request, cancellationToken);
}

/// <summary>Wrapper for the dynamic <c>PublishAsync(object)</c> path, keyed by runtime notification type.</summary>
internal abstract class NotificationWrapper
{
    public abstract ValueTask HandleAsync(Mediator mediator, object notification, CancellationToken cancellationToken);
}

internal sealed class NotificationWrapper<TNotification> : NotificationWrapper
    where TNotification : INotification
{
    public override ValueTask HandleAsync(Mediator mediator, object notification, CancellationToken cancellationToken)
        => mediator.PublishAsync((TNotification)notification, cancellationToken);
}
