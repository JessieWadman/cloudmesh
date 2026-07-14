using System.Buffers;
using System.Collections.Concurrent;

namespace CloudMesh.Mediator.Distributed;

/// <summary>
/// Default <see cref="IDistributedPublisher"/>: resolves the notification's subject from
/// <see cref="DistributedNotificationAttribute"/>, serializes it, and hands the bytes to the transport.
/// Registered only when a transport package is present.
/// </summary>
public sealed class DistributedPublisher : IDistributedPublisher
{
    private readonly INotificationTransport transport;
    private readonly INotificationSerializer serializer;

    private static readonly ConcurrentDictionary<Type, string> SubjectCache = new();

    public DistributedPublisher(INotificationTransport transport, INotificationSerializer serializer)
    {
        this.transport = transport;
        this.serializer = serializer;
    }

    public ValueTask PublishAsync<TNotification>(in TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        var subject = SubjectCache.GetOrAdd(typeof(TNotification), static type =>
        {
            var attribute = (DistributedNotificationAttribute?)Attribute.GetCustomAttribute(type, typeof(DistributedNotificationAttribute));
            if (attribute is null)
                throw new InvalidOperationException(
                    $"Notification type '{type}' is not marked with [DistributedNotification]. " +
                    "Apply the attribute with a subject to publish it via the distributed transport.");
            return attribute.Subject;
        });

        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(in notification, writer);

        var metadata = new NotificationMetadata
        {
            Subject = subject,
            ContentType = serializer.ContentType,
        };

        return transport.PublishAsync(subject, writer.WrittenMemory, metadata, cancellationToken);
    }
}
