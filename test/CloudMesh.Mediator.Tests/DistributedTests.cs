using System.Buffers;
using System.Buffers.Binary;
using CloudMesh.Mediator.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudMesh.Mediator.Tests;

public class DistributedTests
{
    [Fact]
    public async Task Distributed_publish_serializes_and_routes_to_transport()
    {
        var transport = new FakeTransport();
        await using var provider = BuildDistributed(transport);
        var publisher = provider.GetRequiredService<IDistributedPublisher>();

        await publisher.PublishAsync(new OrderCreated(42));

        Assert.Equal("orders.created", transport.LastSubject);
        Assert.Equal("orders.created", transport.LastMetadata.Subject);

        var roundTripped = new FakeSerializer().Deserialize<OrderCreated>(transport.LastPayload);
        Assert.Equal(42, roundTripped.OrderId);
    }

    [Fact]
    public async Task Distributed_publish_without_subject_attribute_throws()
    {
        var transport = new FakeTransport();
        await using var provider = BuildDistributed(transport);
        var publisher = provider.GetRequiredService<IDistributedPublisher>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await publisher.PublishAsync(new SomethingHappened("no-subject")));
    }

    private static ServiceProvider BuildDistributed(FakeTransport transport)
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationTransport>(transport);
        services.AddSingleton<INotificationSerializer, FakeSerializer>();
        services.AddSingleton<IDistributedPublisher, DistributedPublisher>();
        return services.BuildServiceProvider();
    }

    private sealed class FakeTransport : INotificationTransport
    {
        public string? LastSubject { get; private set; }
        public byte[] LastPayload { get; private set; } = Array.Empty<byte>();
        public NotificationMetadata LastMetadata { get; private set; }

        public ValueTask PublishAsync(string subject, ReadOnlyMemory<byte> payload, NotificationMetadata metadata, CancellationToken cancellationToken)
        {
            LastSubject = subject;
            LastPayload = payload.ToArray();
            LastMetadata = metadata;
            return default;
        }
    }

    private sealed class FakeSerializer : INotificationSerializer
    {
        public string ContentType => "application/x-fake";

        public void Serialize<TNotification>(in TNotification notification, IBufferWriter<byte> writer)
            where TNotification : INotification
        {
            if (notification is OrderCreated order)
            {
                var span = writer.GetSpan(8);
                BinaryPrimitives.WriteInt64LittleEndian(span, order.OrderId);
                writer.Advance(8);
                return;
            }
            throw new NotSupportedException($"FakeSerializer cannot serialize {typeof(TNotification)}");
        }

        public TNotification Deserialize<TNotification>(ReadOnlySpan<byte> payload)
            where TNotification : INotification
        {
            if (typeof(TNotification) == typeof(OrderCreated))
            {
                var id = BinaryPrimitives.ReadInt64LittleEndian(payload);
                return (TNotification)(object)new OrderCreated(id);
            }
            throw new NotSupportedException($"FakeSerializer cannot deserialize {typeof(TNotification)}");
        }
    }
}
