using CloudMesh.Mediator.Compatibility;
using Xunit;

namespace CloudMesh.Mediator.Tests;

// MediatR-shaped handler: method 'Handle', returns Task. Should be discovered by scanning and adapted.
public sealed record LegacyPing(string Message) : IRequest<string>;

public sealed class LegacyPingHandler : Compatibility.IRequestHandler<LegacyPing, string>
{
    public Task<string> Handle(LegacyPing request, CancellationToken cancellationToken)
        => Task.FromResult("Legacy: " + request.Message);
}

public class CompatibilityTests
{
    [Fact]
    public async Task MediatR_style_handler_is_discovered_and_adapted()
    {
        using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        var result = await mediator.Send(new LegacyPing("hi"));

        Assert.Equal("Legacy: hi", result);
    }

    [Fact]
    public async Task Compat_send_publish_createstream_extension_methods_work()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        Assert.Equal("Pong: x", await mediator.Send(new Ping("x")));

        await mediator.Publish(new SomethingHappened("y"));
        Assert.Equal(2, provider.Recorder().Counter);

        var items = new List<int>();
        await foreach (var i in mediator.CreateStream(new CountUp(2)))
            items.Add(i);
        Assert.Equal(new[] { 0, 1 }, items);
    }

    [Fact]
    public async Task Compat_send_void_command_returns_task()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        await mediator.Send(new DoWork("compat"));

        Assert.Contains("work:compat", provider.Recorder().Events);
    }
}
