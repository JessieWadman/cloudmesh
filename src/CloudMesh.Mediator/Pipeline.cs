namespace CloudMesh.Mediator;

/// <summary>Invokes the next behavior in the pipeline, or the handler itself if this is the last behavior.</summary>
public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Wraps request handling with cross-cutting behavior (validation, logging, retries, transactions...).
/// Behaviors execute in registration order, each calling <c>next</c> to invoke the remainder of the pipeline.
/// A behavior may short-circuit by returning without calling <c>next</c>.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

/// <summary>Invokes the next stream behavior, or the stream handler if this is the last behavior.</summary>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<TResponse>();

/// <summary>Wraps stream handling with cross-cutting behavior.</summary>
public interface IStreamPipelineBehavior<in TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> HandleAsync(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
