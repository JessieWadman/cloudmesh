using CloudMesh.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudMesh.Mediator.Tests;

public class SendTests
{
    [Fact]
    public async Task Sends_class_request_to_handler_via_inference()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        var result = await mediator.SendAsync(new Ping("hi"));

        Assert.Equal("Pong: hi", result);
    }

    [Fact]
    public async Task Sends_record_request()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        var result = await mediator.SendAsync(new Add(2, 3));

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task Sends_struct_request_via_boxfree_overload()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        var result = await mediator.SendAsync<StructAdd, int>(new StructAdd(4, 5));

        Assert.Equal(9, result);
    }

    [Fact]
    public async Task Sends_void_command()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        await mediator.SendAsync(new DoWork("go"));

        Assert.Contains("work:go", provider.Recorder().Events);
    }

    [Fact]
    public async Task Missing_handler_throws_clear_diagnostic()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        var ex = await Assert.ThrowsAsync<HandlerNotFoundException>(
            async () => await mediator.SendAsync(new Orphan("x")));

        Assert.Equal(typeof(Orphan), ex.RequestType);
    }

    [Fact]
    public async Task Passes_cancellation_token_to_handler()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await mediator.SendAsync(new CancelSensitive(1), cts.Token));
    }

    [Fact]
    public async Task Request_with_two_response_types_resolves_each_handler_without_conflict()
    {
        // The reflection scanner keys on the closed handler interface, so IRequestHandler<MultiResp,int> and
        // IRequestHandler<MultiResp,string> both register without a "duplicate handler" conflict.
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        var asInt = await mediator.SendAsync<MultiResp, int>(new MultiResp(7));
        var asString = await mediator.SendAsync<MultiResp, string>(new MultiResp(7));

        Assert.Equal(7, asInt);
        Assert.Equal("s:7", asString);
    }

    [Fact]
    public async Task Resolves_handler_from_the_current_scope()
    {
        await using var provider = TestHost.Build(o => o.HandlerLifetime = ServiceLifetime.Scoped);

        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.SendAsync(new Add(10, 20));

        Assert.Equal(30, result);
    }
}
