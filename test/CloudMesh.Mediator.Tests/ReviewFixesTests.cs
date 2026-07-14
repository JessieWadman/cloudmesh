using CloudMesh.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudMesh.Mediator.Tests;

// ---- Extra notification used only by the parallel-throw test (P5). ----------
// Safe to add to the scanned assembly: it is only ever published by the test below, and notification
// handlers are legitimately many, so registering these does not affect the existing suite.

public sealed record ExplodingNotification(string What) : INotification;

public sealed class ThrowsSynchronouslyHandler : INotificationHandler<ExplodingNotification>
{
    public ValueTask HandleAsync(ExplodingNotification notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("boom (synchronous)");
}

public sealed class RecordsThatItRanHandler : INotificationHandler<ExplodingNotification>
{
    private readonly Recorder recorder;
    public RecordsThatItRanHandler(Recorder recorder) => this.recorder = recorder;

    public ValueTask HandleAsync(ExplodingNotification notification, CancellationToken cancellationToken)
    {
        recorder.Add("second-ran:" + notification.What);
        return default;
    }
}

public class ReviewFixesTests
{
    // P3 (a): publishing through an INotification-typed variable dispatches on the runtime type and
    // reaches the concrete handlers.
    [Fact]
    public async Task Publish_through_interface_typed_variable_reaches_concrete_handlers()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        INotification notification = new SomethingHappened("iface");
        await mediator.PublishAsync(notification);

        Assert.Equal(2, provider.Recorder().Counter);
    }

    // P3 (b): a concrete notification with no registered handlers is a no-op (does not throw).
    [Fact]
    public async Task Publish_concrete_notification_with_no_handlers_is_a_noop()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        await mediator.PublishAsync(new Unheard("silence"));
    }

    // P4: sending through the generic box-free overload where TRequest is statically IRequest<T>
    // (an interface) falls back to runtime-typed dispatch and reaches the right handler.
    [Fact]
    public async Task Send_generic_overload_with_interface_typed_request_falls_back_and_resolves()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        IRequest<string> request = new Ping("hi");
        var result = await mediator.SendAsync<IRequest<string>, string>(request);

        Assert.Equal("Pong: hi", result);
    }

    // P5: with the parallel publisher, a handler that throws synchronously does not orphan the others;
    // every handler still runs and the failure surfaces on await.
    [Fact]
    public async Task Parallel_publisher_synchronous_throw_still_runs_others_and_surfaces()
    {
        await using var provider = TestHost.Build(o => o.NotificationPublisherType = typeof(ParallelNotificationPublisher));
        var mediator = provider.Mediator();

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await mediator.PublishAsync(new ExplodingNotification("x")));

        Assert.Contains("second-ran:x", provider.Recorder().Events);
    }
}
