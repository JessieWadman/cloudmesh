namespace CloudMesh.Mediator.Compatibility;

/// <summary>
/// Method-name parity with MediatR's consumer API (<c>Send</c>/<c>Publish</c>/<c>CreateStream</c>, returning
/// <see cref="Task"/>). Lets code that calls into MediatR compile against CloudMesh.Mediator unchanged.
/// </summary>
public static class MediatRCompatibilityExtensions
{
    public static Task<TResponse> Send<TResponse>(this ISender sender, IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => sender.SendAsync(request, cancellationToken).AsTask();

    public static Task Send(this ISender sender, IRequest<NoResponse> request, CancellationToken cancellationToken = default)
        => sender.SendAsync(request, cancellationToken).AsTask();

    public static Task Publish<TNotification>(this IPublisher publisher, TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
        => publisher.PublishAsync(notification, cancellationToken).AsTask();

    public static Task Publish(this IPublisher publisher, object notification, CancellationToken cancellationToken = default)
        => publisher.PublishAsync(notification, cancellationToken).AsTask();

    public static IAsyncEnumerable<TResponse> CreateStream<TResponse>(this ISender sender, IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        => sender.StreamAsync(request, cancellationToken);
}
