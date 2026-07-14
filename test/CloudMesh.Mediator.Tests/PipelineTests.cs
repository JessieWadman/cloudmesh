using Xunit;

namespace CloudMesh.Mediator.Tests;

public class PipelineTests
{
    [Fact]
    public async Task Behaviors_wrap_handler_in_registration_order()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        var result = await mediator.SendAsync(new Tracked("x"));

        Assert.Equal("handled:x", result);
        Assert.Equal(
            new[] { "first:before", "second:before", "handler", "second:after", "first:after" },
            provider.Recorder().Events);
    }

    [Fact]
    public async Task Behavior_can_short_circuit_without_calling_handler()
    {
        using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        Assert.Equal("blocked", await mediator.SendAsync(new Guarded(false)));
        Assert.Equal("handled", await mediator.SendAsync(new Guarded(true)));
    }

    [Fact]
    public async Task Open_generic_behavior_registered_by_reflection_scanner_runs()
    {
        // The reflection scanner must register the open-generic OpenGenericCountingBehavior<,> as an
        // open-generic descriptor; MS DI closes it for OpenGenTarget (which satisfies the marker constraint).
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        var result = await mediator.SendAsync(new OpenGenTarget(7));

        Assert.Equal(7, result);
        Assert.Contains("open-generic:before", provider.Recorder().Events);
    }
}
