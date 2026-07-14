namespace CloudMesh.Mediator;

/// <summary>
/// Thrown when a request/stream is sent but no handler is registered for it. In a later stage the source
/// generator upgrades this to a compile-time error; until then it is a clear runtime diagnostic.
/// </summary>
public sealed class HandlerNotFoundException : InvalidOperationException
{
    public HandlerNotFoundException(Type requestType)
        : base($"No handler registered for request type '{requestType}'. " +
               $"Ensure a handler implementing the corresponding IRequestHandler<>/IStreamRequestHandler<> is registered.")
    {
        RequestType = requestType;
    }

    public Type RequestType { get; }
}
