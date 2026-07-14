using CloudMesh.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudMesh.Mediator.Tests;

public class NotificationTests
{
    [Fact]
    public async Task Publishes_to_all_handlers()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        await mediator.PublishAsync(new SomethingHappened("boom"));

        var events = provider.Recorder().Events;
        Assert.Contains("A:boom", events);
        Assert.Contains("B:boom", events);
        Assert.Equal(2, provider.Recorder().Counter);
    }

    [Fact]
    public async Task Publish_with_no_handlers_is_a_noop()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        await mediator.PublishAsync(new Unheard("silence"));
    }

    [Fact]
    public async Task Publish_object_dispatches_on_runtime_type()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        object notification = new SomethingHappened("dynamic");
        await mediator.PublishAsync(notification);

        Assert.Equal(2, provider.Recorder().Counter);
    }

    [Fact]
    public async Task Parallel_publisher_invokes_all_handlers()
    {
        await using var provider = TestHost.Build(o => o.NotificationPublisherType = typeof(ParallelNotificationPublisher));
        var mediator = provider.Mediator();

        await mediator.PublishAsync(new SomethingHappened("parallel"));

        Assert.Equal(2, provider.Recorder().Counter);
    }
}
