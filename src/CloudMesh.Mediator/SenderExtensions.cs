namespace CloudMesh.Mediator;

/// <summary>
/// Hand-written ergonomic entry points for <see cref="ISender"/>. These are the boxing FALLBACK: they apply to
/// any <see cref="IRequest{TResponse}"/>/<see cref="IStreamRequest{TResponse}"/> and route to the runtime-typed
/// dispatch. For a concrete request type the source generator also emits an exact-typed
/// <c>SendAsync(this ISender, in ConcreteRequest, ...)</c>/<c>StreamAsync(...)</c> overload; that one is more
/// specific (no interface conversion) and wins overload resolution, giving the box-free path automatically —
/// while these keep <c>sender.SendAsync(request)</c> compiling even without the generator.
/// </summary>
/// <remarks>
/// These forward to <see cref="ISender.InternalSendDynamicAsync{TResponse}"/>/<see cref="ISender.InternalStreamDynamicAsync{TResponse}"/>,
/// whose concrete implementation on <see cref="Mediator"/> carries the AOT/trim (<c>RequiresDynamicCode</c>)
/// annotations. Calling the interface member here does not surface those warnings — the box-free generic
/// primitive or a generated overload should be used in AOT/trim scenarios.
/// </remarks>
public static class SenderExtensions
{
    /// <summary>
    /// Sends a request, inferring <typeparamref name="TResponse"/> from the request. Boxing fallback used when no
    /// source-generated box-free overload exists for the concrete request type.
    /// </summary>
    public static ValueTask<TResponse> SendAsync<TResponse>(
        this ISender sender, IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => sender.InternalSendDynamicAsync(request, cancellationToken);

    /// <summary>
    /// Streams a request's responses, inferring <typeparamref name="TResponse"/>. Boxing fallback; the generated
    /// exact-typed overload wins when present.
    /// </summary>
    public static IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        this ISender sender, IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        => sender.InternalStreamDynamicAsync(request, cancellationToken);
}
