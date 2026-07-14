using System.Buffers;

namespace CloudMesh.Mediator.Distributed;

/// <summary>
/// Marks a notification as eligible for out-of-process delivery and declares the transport subject/topic it maps to.
/// Discovered at compile time by the source generator (later) and at runtime by the distributed publisher.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class DistributedNotificationAttribute : Attribute
{
    public DistributedNotificationAttribute(string subject) => Subject = subject;

    /// <summary>The transport subject/topic/stream this notification is published to (e.g. <c>"orders.created"</c>).</summary>
    public string Subject { get; }
}

/// <summary>Envelope metadata carried alongside a distributed notification's payload.</summary>
public readonly struct NotificationMetadata
{
    /// <summary>The transport subject/topic the notification is published to.</summary>
    public string Subject { get; init; }

    /// <summary>Optional stable message id, used by transports that support de-duplication.</summary>
    public string? MessageId { get; init; }

    /// <summary>Optional content type of the payload (e.g. <c>"application/json"</c>).</summary>
    public string? ContentType { get; init; }
}

/// <summary>
/// The seam a network transport package (NATS, Redis, ...) implements. The core produces subject + bytes;
/// the transport just moves them. No transport ships in this package.
/// </summary>
public interface INotificationTransport
{
    ValueTask PublishAsync(string subject, ReadOnlyMemory<byte> payload, NotificationMetadata metadata, CancellationToken cancellationToken);
}

/// <summary>Serializes/deserializes distributed notifications to/from bytes. Pluggable (MemoryPack, STJ, ...).</summary>
public interface INotificationSerializer
{
    void Serialize<TNotification>(in TNotification notification, IBufferWriter<byte> writer)
        where TNotification : INotification;

    TNotification Deserialize<TNotification>(ReadOnlySpan<byte> payload)
        where TNotification : INotification;

    /// <summary>Content type produced by <see cref="Serialize{TNotification}"/> (for envelope metadata).</summary>
    string ContentType { get; }
}

/// <summary>
/// Publishes a notification to other processes via the registered <see cref="INotificationTransport"/>.
/// Distinct from in-process <see cref="IPublisher"/>: remote delivery has different latency/failure semantics
/// and is kept visible rather than hidden behind the local publish.
/// </summary>
public interface IDistributedPublisher
{
    ValueTask PublishAsync<TNotification>(in TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
