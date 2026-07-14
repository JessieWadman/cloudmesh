namespace CloudMesh.Mediator;

/// <summary>
/// Strategy for invoking a notification's handlers. Swap the registered implementation to change fan-out semantics
/// (sequential vs. parallel, error aggregation, etc.) without touching call sites.
/// </summary>
public interface INotificationPublisher
{
    ValueTask PublishAsync<TNotification>(
        IReadOnlyList<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification;
}

/// <summary>
/// Awaits each handler in turn, in registration order. A throwing handler stops the remaining handlers.
/// This is the default strategy (predictable ordering, no concurrency surprises).
/// </summary>
public sealed class SequentialNotificationPublisher : INotificationPublisher
{
    public ValueTask PublishAsync<TNotification>(
        IReadOnlyList<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        var count = handlers.Count;
        if (count == 0)
            return default;
        if (count == 1)
            return handlers[0].HandleAsync(notification, cancellationToken);

        return AwaitAll(handlers, notification, cancellationToken);

        static async ValueTask AwaitAll(
            IReadOnlyList<INotificationHandler<TNotification>> handlers,
            TNotification notification,
            CancellationToken cancellationToken)
        {
            for (var i = 0; i < handlers.Count; i++)
                await handlers[i].HandleAsync(notification, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Invokes all handlers, then awaits their completion together. Every handler is started even if an earlier
/// one throws synchronously (its fault is captured rather than orphaning the others). Awaiting the returned
/// task surfaces the first failure; the complete set of failures is available on the underlying
/// <see cref="Task.Exception"/> (standard <see cref="Task.WhenAll(Task[])"/> semantics).
/// </summary>
public sealed class ParallelNotificationPublisher : INotificationPublisher
{
    public ValueTask PublishAsync<TNotification>(
        IReadOnlyList<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        var count = handlers.Count;
        if (count == 0)
            return default;
        if (count == 1)
            return handlers[0].HandleAsync(notification, cancellationToken);

        var tasks = new Task[count];
        for (var i = 0; i < count; i++)
        {
            try
            {
                tasks[i] = handlers[i].HandleAsync(notification, cancellationToken).AsTask();
            }
            catch (Exception ex)
            {
                // A handler that threw synchronously must not orphan the handlers already started nor lose
                // its own fault: capture it as a faulted task so every handler runs and every failure lands
                // in Task.WhenAll.
                tasks[i] = Task.FromException(ex);
            }
        }

        return new ValueTask(Task.WhenAll(tasks));
    }
}
