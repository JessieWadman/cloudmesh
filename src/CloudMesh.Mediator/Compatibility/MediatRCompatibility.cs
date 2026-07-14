namespace CloudMesh.Mediator.Compatibility;

/// <summary>
/// MediatR-shaped request handler (method named <c>Handle</c>, returns <see cref="Task{TResponse}"/>).
/// Implementations are discovered during assembly scanning and adapted onto the native
/// <see cref="CloudMesh.Mediator.IRequestHandler{TRequest,TResponse}"/>, so a MediatR handler ports by
/// changing only its <c>using</c>.
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>MediatR-shaped notification handler (method named <c>Handle</c>, returns <see cref="Task"/>).</summary>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}
