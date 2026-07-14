namespace CloudMesh.Mediator;

/// <summary>
/// Handles a <typeparamref name="TRequest"/> and produces a <typeparamref name="TResponse"/>.
/// Exactly one handler is expected per request type.
/// </summary>
/// <remarks>
/// The request is passed by value (not <c>in</c>) so implementations may be <c>async</c>;
/// C# forbids <c>in</c> parameters on async methods. The zero-allocation win of value-type
/// requests is preserved regardless — it comes from avoiding the heap, not from avoiding a stack copy.
/// </remarks>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Handles a command (<see cref="IRequest"/>) that returns nothing.
/// Implementers implement only the <see cref="ValueTask"/>-returning method; the
/// <see cref="NoResponse"/>-returning base is bridged automatically.
/// </summary>
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, NoResponse>
    where TRequest : IRequest<NoResponse>
{
    new ValueTask HandleAsync(TRequest request, CancellationToken cancellationToken);

    async ValueTask<NoResponse> IRequestHandler<TRequest, NoResponse>.HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        await HandleAsync(request, cancellationToken).ConfigureAwait(false);
        return NoResponse.Value;
    }
}

/// <summary>
/// Produces an asynchronous stream of <typeparamref name="TResponse"/> for a <typeparamref name="TRequest"/>.
/// </summary>
public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Handles a published <typeparamref name="TNotification"/>. Any number of handlers may exist for a notification.
/// </summary>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    ValueTask HandleAsync(TNotification notification, CancellationToken cancellationToken);
}
