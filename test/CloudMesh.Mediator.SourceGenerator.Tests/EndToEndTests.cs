using CloudMesh.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudMesh.Mediator.SourceGenerator.Tests;

// Handlers declared HERE are discovered by the generator (referenced as an Analyzer in this test project),
// so AddCloudMeshMediatorGenerated() and the zero-boxing SendAsync(in Req) overloads are emitted for them.

public readonly record struct Add(int A, int B) : IRequest<int>;

public sealed class AddHandler : IRequestHandler<Add, int>
{
    public ValueTask<int> HandleAsync(Add request, CancellationToken ct) => new(request.A + request.B);
}

public readonly record struct Countdown(int From) : IStreamRequest<int>;

public sealed class CountdownHandler : IStreamRequestHandler<Countdown, int>
{
    public async IAsyncEnumerable<int> HandleAsync(
        Countdown request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = request.From; i > 0; i--)
        {
            yield return i;
            await Task.Yield();
        }
    }
}

public sealed class Beep : INotification { }

public sealed class BeepCounter
{
    public int Count;
}

public sealed class BeepHandler : INotificationHandler<Beep>
{
    private readonly BeepCounter _counter;
    public BeepHandler(BeepCounter counter) => _counter = counter;
    public ValueTask HandleAsync(Beep notification, CancellationToken ct)
    {
        Interlocked.Increment(ref _counter.Count);
        return default;
    }
}

public sealed class DoublingBehavior : IPipelineBehavior<Add, int>
{
    public async ValueTask<int> HandleAsync(Add request, RequestHandlerDelegate<int> next, CancellationToken ct)
        => (await next()) * 2;
}

// --- Open-generic behavior + a dedicated request it wraps (kept separate from Add to not disturb its result). ---

public readonly record struct Echo(int V) : IRequest<int>;

public sealed class EchoHandler : IRequestHandler<Echo, int>
{
    public ValueTask<int> HandleAsync(Echo request, CancellationToken ct) => new(request.V);
}

public sealed class OpenGenericInvocationCounter
{
    public int Count;
}

// The common open-generic behavior shape. Must be registered as an OPEN generic and closed by DI per request.
public sealed class CountingBehavior<TReq, TResp> : IPipelineBehavior<TReq, TResp>
    where TReq : IRequest<TResp>
{
    private readonly OpenGenericInvocationCounter _counter;
    public CountingBehavior(OpenGenericInvocationCounter counter) => _counter = counter;

    public ValueTask<TResp> HandleAsync(TReq request, RequestHandlerDelegate<TResp> next, CancellationToken ct)
    {
        Interlocked.Increment(ref _counter.Count);
        return next();
    }
}

public class EndToEndTests
{
    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<BeepCounter>();
        services.AddSingleton<OpenGenericInvocationCounter>();
        // The generated method is suffixed with this assembly's sanitized name (per-assembly, collision-free).
        services.AddCloudMeshMediatorGeneratedCloudMesh_Mediator_SourceGenerator_Tests();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Send_uses_generated_registration_and_pipeline()
    {
        var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // 2 + 3 = 5, doubled by the pipeline behavior = 10.
        var result = await mediator.SendAsync<Add, int>(new Add(2, 3));
        Assert.Equal(10, result);
    }

    [Fact]
    public async Task Zero_boxing_overload_binds_and_works()
    {
        var sp = BuildProvider();
        ISender sender = sp.GetRequiredService<ISender>();

        // This binds to the generated SendAsync(this ISender, in Add, ...) — the box-free path.
        var add = new Add(10, 20);
        var result = await sender.SendAsync(add);
        Assert.Equal(60, result); // (10+20)*2
    }

    [Fact]
    public async Task Stream_uses_generated_registration()
    {
        var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var collected = new List<int>();
        await foreach (var v in mediator.StreamAsync<Countdown, int>(new Countdown(3)))
            collected.Add(v);

        Assert.Equal(new[] { 3, 2, 1 }, collected);
    }

    [Fact]
    public async Task Stream_zero_boxing_overload_binds()
    {
        var sp = BuildProvider();
        ISender sender = sp.GetRequiredService<ISender>();

        var collected = new List<int>();
        var req = new Countdown(2);
        await foreach (var v in sender.StreamAsync(req))
            collected.Add(v);

        Assert.Equal(new[] { 2, 1 }, collected);
    }

    [Fact]
    public async Task Publish_uses_generated_notification_registration()
    {
        var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();
        var counter = sp.GetRequiredService<BeepCounter>();

        await mediator.PublishAsync(new Beep());

        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task Open_generic_behavior_runs_via_generated_registration()
    {
        var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();
        var counter = sp.GetRequiredService<OpenGenericInvocationCounter>();

        // CountingBehavior<TReq,TResp> was registered as an OPEN generic and closed by DI for Echo.
        var result = await mediator.SendAsync<Echo, int>(new Echo(42));

        Assert.Equal(42, result);        // behavior passes through
        Assert.True(counter.Count >= 1); // open-generic behavior actually ran
    }
}
