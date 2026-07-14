namespace CloudMesh.Mediator;

/// <summary>
/// Marks a request that, when sent, produces a single <typeparamref name="TResponse"/>.
/// A request is handled by exactly one <see cref="IRequestHandler{TRequest,TResponse}"/>.
/// </summary>
public interface IRequest<out TResponse>
{
}

/// <summary>
/// Marks a request that produces no meaningful response (a command). Equivalent to <see cref="IRequest{NoResponse}"/>.
/// </summary>
public interface IRequest : IRequest<NoResponse>
{
}

/// <summary>
/// Marks a request that, when streamed, produces an asynchronous sequence of <typeparamref name="TResponse"/>.
/// </summary>
public interface IStreamRequest<out TResponse>
{
}

/// <summary>
/// Marks a notification that may be handled by zero or more <see cref="INotificationHandler{TNotification}"/> instances.
/// </summary>
public interface INotification
{
}

/// <summary>
/// The NoResponse type: a type with a single value, used as the response of a request that returns nothing.
/// Avoids special-casing <c>void</c> across the generic dispatch surface.
/// </summary>
public readonly struct NoResponse : IEquatable<NoResponse>, IComparable<NoResponse>
{
    /// <summary>The single value of <see cref="NoResponse"/>.</summary>
    public static readonly NoResponse Value = default;

    /// <summary>A completed <see cref="ValueTask{NoResponse}"/> carrying <see cref="Value"/>.</summary>
    public static ValueTask<NoResponse> ValueTask => new(Value);

    public bool Equals(NoResponse other) => true;
    public override bool Equals(object? obj) => obj is NoResponse;
    public override int GetHashCode() => 0;
    public int CompareTo(NoResponse other) => 0;
    public static bool operator ==(NoResponse left, NoResponse right) => true;
    public static bool operator !=(NoResponse left, NoResponse right) => false;
    public override string ToString() => "()";
}
