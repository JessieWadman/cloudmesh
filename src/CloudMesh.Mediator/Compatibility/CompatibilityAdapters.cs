namespace CloudMesh.Mediator.Compatibility;

/// <summary>
/// Adapts a MediatR-shaped <see cref="IRequestHandler{TRequest,TResponse}"/> (method <c>Handle</c>, returns
/// <see cref="Task{TResponse}"/>) onto the native <see cref="CloudMesh.Mediator.IRequestHandler{TRequest,TResponse}"/>.
/// Resolved from DI, which supplies the underlying compat handler.
/// </summary>
public sealed class CompatRequestHandlerAdapter<TRequest, TResponse>
    : CloudMesh.Mediator.IRequestHandler<TRequest, TResponse>
    where TRequest : CloudMesh.Mediator.IRequest<TResponse>
{
    private readonly IRequestHandler<TRequest, TResponse> inner;

    public CompatRequestHandlerAdapter(IRequestHandler<TRequest, TResponse> inner) => this.inner = inner;

    public ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
        => new(inner.Handle(request, cancellationToken));
}

/// <summary>Adapts a MediatR-shaped <see cref="INotificationHandler{TNotification}"/> onto the native one.</summary>
public sealed class CompatNotificationHandlerAdapter<TNotification>
    : CloudMesh.Mediator.INotificationHandler<TNotification>
    where TNotification : CloudMesh.Mediator.INotification
{
    private readonly INotificationHandler<TNotification> inner;

    public CompatNotificationHandlerAdapter(INotificationHandler<TNotification> inner) => this.inner = inner;

    public ValueTask HandleAsync(TNotification notification, CancellationToken cancellationToken)
        => new(inner.Handle(notification, cancellationToken));
}
