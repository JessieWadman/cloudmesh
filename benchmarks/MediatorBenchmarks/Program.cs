using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using CloudMesh.Mediator; // for the AddCloudMeshMediator extension method
using MediatR;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

using CloudMeshLib = MediatorBenchmarks.Libraries.CloudMeshLib;
using MediatRLib = MediatorBenchmarks.Libraries.MediatRLib;
using MartinotharLib = MediatorBenchmarks.Libraries.MartinotharLib;
using MessagePipeLib = MediatorBenchmarks.Libraries.MessagePipeLib;

// A short job is added on each benchmark class via [ShortRunJob] so the default `dotnet run -c Release`
// produces a quick-but-real table. For a full run, remove the [ShortRunJob] attributes.

var switcher = new BenchmarkSwitcher(new[]
{
    typeof(SendBenchmarks),
    typeof(PublishBenchmarks),
    typeof(SendWithBehaviorsBenchmarks),
});

// If no args given, run all benchmark classes.
if (args.Length == 0)
    switcher.RunAll();
else
    switcher.Run(args);

// ---------------------------------------------------------------------------------------------------
// Fairness rules applied to ALL scenarios:
//  * Each library's IServiceProvider is built ONCE in [GlobalSetup]; the mediator/handler is resolved
//    once into a field and reused across iterations. Nothing is built/resolved inside a [Benchmark].
//  * Handlers/behaviors are registered as SINGLETON for every library. This isolates the mediator's
//    dispatch/allocation overhead (the thing under test) from per-request handler instantiation cost.
//  * Identical request payload ("ping") and response type (string) across libraries; the same three
//    handlers for publish.
//  * All benchmark methods return the result so nothing is optimized away.
// ---------------------------------------------------------------------------------------------------

[MemoryDiagnoser]
[ShortRunJob]
public class SendBenchmarks
{
    private CloudMesh.Mediator.IMediator _cloudMesh = null!;
    // Same instance, typed as the concrete sealed Mediator, to isolate the devirtualization effect.
    private CloudMesh.Mediator.Mediator _cloudMeshConcrete = null!;
    private MediatR.IMediator _mediatR = null!;
    private Mediator.IMediator _martinothar = null!;
    private IAsyncRequestHandler<MessagePipeLib.Ping, string> _messagePipe = null!;

    // Non-readonly so the JIT cannot treat the request (and thus a fully-inlined handler's result) as
    // loop-invariant and hoist the whole operation out of the measurement loop — which otherwise makes
    // ultra-thin direct-resolution paths (MessagePipe) measure the empty-loop floor rather than dispatch.
    private CloudMeshLib.Ping _cloudMeshReq = new("ping");
    private MediatRLib.Ping _mediatRReq = new("ping");
    private MartinotharLib.Ping _martinotharReq = new("ping");
    private MessagePipeLib.Ping _messagePipeReq = new("ping");

    [GlobalSetup]
    public void Setup()
    {
        // CloudMesh.Mediator
        var cm = new ServiceCollection();
        cm.AddCloudMeshMediator(o =>
        {
            o.HandlerLifetime = ServiceLifetime.Singleton;
            o.RegisterServicesFromAssemblyContaining<CloudMeshLib.Ping>();
        });
        _cloudMesh = cm.BuildServiceProvider().GetRequiredService<CloudMesh.Mediator.IMediator>();
        _cloudMeshConcrete = (CloudMesh.Mediator.Mediator)_cloudMesh;

        // MediatR (12.5.0)
        var mr = new ServiceCollection();
        mr.AddMediatR(cfg =>
        {
            cfg.Lifetime = ServiceLifetime.Singleton;
            cfg.RegisterServicesFromAssemblyContaining<MediatRLib.Ping>();
        });
        _mediatR = mr.BuildServiceProvider().GetRequiredService<MediatR.IMediator>();

        // martinothar/Mediator (3.0.2, source generated)
        var mo = new ServiceCollection();
        mo.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Singleton);
        _martinothar = mo.BuildServiceProvider().GetRequiredService<Mediator.IMediator>();

        // MessagePipe (1.8.1) - resolve the request handler directly (no central mediator).
        var mp = new ServiceCollection();
        mp.AddMessagePipe(options => options.RequestHandlerLifetime = InstanceLifetime.Singleton);
        _messagePipe = mp.BuildServiceProvider().GetRequiredService<IAsyncRequestHandler<MessagePipeLib.Ping, string>>();
    }

    // Ergonomic call with NO explicit type args. With the generator active this binds to the generated
    // SendAsync(in Ping) overload: box-free (Ping is a struct) AND, since Ping has no behaviors, it down-casts
    // the concrete Mediator so the primitive call devirtualizes. There is no special dispatch mechanism —
    // compare against the two rows below to see the win is box-elimination + devirtualization.
    [Benchmark(Baseline = true, Description = "CloudMesh (ergonomic, generated overload — box-free + devirtualized)")]
    public ValueTask<string> CloudMesh_Ergonomic()
        => _cloudMesh.SendAsync(_cloudMeshReq);

    // Box-free primitive through the ISender INTERFACE receiver (no devirtualization).
    [Benchmark(Description = "CloudMesh (box-free primitive, interface receiver)")]
    public ValueTask<string> CloudMesh_BoxFree_Interface()
        => _cloudMesh.SendAsync<CloudMeshLib.Ping, string>(in _cloudMeshReq);

    // Box-free primitive through the CONCRETE Mediator receiver (devirtualized). Its Mean should match the
    // generated-ergonomic row to ~0 — proving the ergonomic win is box-elimination + devirtualization, not a
    // separate dispatch path.
    [Benchmark(Description = "CloudMesh (box-free primitive, concrete receiver)")]
    public ValueTask<string> CloudMesh_BoxFree_Concrete()
        => _cloudMeshConcrete.SendAsync<CloudMeshLib.Ping, string>(in _cloudMeshReq);

    [Benchmark(Description = "MediatR 12.5.0")]
    public Task<string> MediatR()
        => _mediatR.Send(_mediatRReq);

    [Benchmark(Description = "martinothar 3.0.2")]
    public ValueTask<string> Martinothar()
        => _martinothar.Send(_martinotharReq);

    [Benchmark(Description = "MessagePipe 1.8.1")]
    public ValueTask<string> MessagePipe()
        => _messagePipe.InvokeAsync(_messagePipeReq);
}

[MemoryDiagnoser]
[ShortRunJob]
public class PublishBenchmarks
{
    private CloudMesh.Mediator.IMediator _cloudMesh = null!;
    private MediatR.IMediator _mediatR = null!;
    private Mediator.IMediator _martinothar = null!;
    private IPublisher<MessagePipeLib.Pinged> _messagePipePublisher = null!;

    private readonly CloudMeshLib.Pinged _cloudMeshNote = new("ping");
    private readonly MediatRLib.Pinged _mediatRNote = new("ping");
    private readonly MartinotharLib.Pinged _martinotharNote = new("ping");
    private readonly MessagePipeLib.Pinged _messagePipeNote = new("ping");

    // Keep subscriptions alive for MessagePipe.
    private IDisposable _mpSubscriptions = null!;

    [GlobalSetup]
    public void Setup()
    {
        var cm = new ServiceCollection();
        cm.AddCloudMeshMediator(o =>
        {
            o.HandlerLifetime = ServiceLifetime.Singleton;
            o.RegisterServicesFromAssemblyContaining<CloudMeshLib.Ping>();
        });
        _cloudMesh = cm.BuildServiceProvider().GetRequiredService<CloudMesh.Mediator.IMediator>();

        var mr = new ServiceCollection();
        mr.AddMediatR(cfg =>
        {
            cfg.Lifetime = ServiceLifetime.Singleton;
            cfg.RegisterServicesFromAssemblyContaining<MediatRLib.Ping>();
        });
        _mediatR = mr.BuildServiceProvider().GetRequiredService<MediatR.IMediator>();

        var mo = new ServiceCollection();
        mo.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Singleton);
        _martinothar = mo.BuildServiceProvider().GetRequiredService<Mediator.IMediator>();

        // MessagePipe: subscribe three handlers at runtime (pub/sub is not auto-discovered).
        var mp = new ServiceCollection();
        mp.AddMessagePipe(options => options.InstanceLifetime = InstanceLifetime.Singleton);
        var provider = mp.BuildServiceProvider();
        _messagePipePublisher = provider.GetRequiredService<IPublisher<MessagePipeLib.Pinged>>();
        var subscriber = provider.GetRequiredService<ISubscriber<MessagePipeLib.Pinged>>();
        var bag = DisposableBag.CreateBuilder();
        subscriber.Subscribe(_ => MessagePipeCounter1()).AddTo(bag);
        subscriber.Subscribe(_ => MessagePipeCounter2()).AddTo(bag);
        subscriber.Subscribe(_ => MessagePipeCounter3()).AddTo(bag);
        _mpSubscriptions = bag.Build();
    }

    private static void MessagePipeCounter1() => MpCounters.Handler1++;
    private static void MessagePipeCounter2() => MpCounters.Handler2++;
    private static void MessagePipeCounter3() => MpCounters.Handler3++;

    [Benchmark(Baseline = true, Description = "CloudMesh")]
    public ValueTask CloudMesh()
        => _cloudMesh.PublishAsync(in _cloudMeshNote);

    [Benchmark(Description = "MediatR 12.5.0")]
    public Task MediatR()
        => _mediatR.Publish(_mediatRNote);

    [Benchmark(Description = "martinothar 3.0.2")]
    public ValueTask Martinothar()
        => _martinothar.Publish(_martinotharNote);

    [Benchmark(Description = "MessagePipe 1.8.1")]
    public void MessagePipe()
        => _messagePipePublisher.Publish(_messagePipeNote);
}

internal static class MpCounters
{
    public static long Handler1;
    public static long Handler2;
    public static long Handler3;
}

// Scenario 3: Send with 2 pipeline behaviors. MessagePipe is OMITTED (no request/response pipeline
// behavior equivalent; it uses per-handler filters, which are shaped differently).
[MemoryDiagnoser]
[ShortRunJob]
public class SendWithBehaviorsBenchmarks
{
    private CloudMesh.Mediator.IMediator _cloudMesh = null!;
    private MediatR.IMediator _mediatR = null!;
    private Mediator.IMediator _martinothar = null!;

    private readonly CloudMeshLib.PingWithBehaviors _cloudMeshReq = new("ping");
    private readonly MediatRLib.PingWithBehaviors _mediatRReq = new("ping");
    private readonly MartinotharLib.PingWithBehaviors _martinotharReq = new("ping");

    [GlobalSetup]
    public void Setup()
    {
        // CloudMesh: behaviors are auto-discovered by assembly scan (closed IPipelineBehavior<,>).
        var cm = new ServiceCollection();
        cm.AddCloudMeshMediator(o =>
        {
            o.HandlerLifetime = ServiceLifetime.Singleton;
            o.RegisterServicesFromAssemblyContaining<CloudMeshLib.Ping>();
        });
        _cloudMesh = cm.BuildServiceProvider().GetRequiredService<CloudMesh.Mediator.IMediator>();

        // MediatR: register open behaviors explicitly.
        var mr = new ServiceCollection();
        mr.AddMediatR(cfg =>
        {
            cfg.Lifetime = ServiceLifetime.Singleton;
            cfg.RegisterServicesFromAssemblyContaining<MediatRLib.Ping>();
        });
        mr.AddSingleton<MediatR.IPipelineBehavior<MediatRLib.PingWithBehaviors, string>, MediatRLib.Behavior1>();
        mr.AddSingleton<MediatR.IPipelineBehavior<MediatRLib.PingWithBehaviors, string>, MediatRLib.Behavior2>();
        _mediatR = mr.BuildServiceProvider().GetRequiredService<MediatR.IMediator>();

        // martinothar: behaviors are not auto-discovered; register manually as singletons.
        var mo = new ServiceCollection();
        mo.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Singleton);
        mo.AddSingleton<Mediator.IPipelineBehavior<MartinotharLib.PingWithBehaviors, string>, MartinotharLib.Behavior1>();
        mo.AddSingleton<Mediator.IPipelineBehavior<MartinotharLib.PingWithBehaviors, string>, MartinotharLib.Behavior2>();
        _martinothar = mo.BuildServiceProvider().GetRequiredService<Mediator.IMediator>();
    }

    // Ergonomic call; PingWithBehaviors HAS 2 behaviors, so the generated overload deliberately does NOT use the
    // fast path (a runtime behavior must never be skipped) and routes to the runtime pipeline.
    [Benchmark(Baseline = true, Description = "CloudMesh (ergonomic / behavior path)")]
    public ValueTask<string> CloudMesh_Ergonomic()
        => _cloudMesh.SendAsync(_cloudMeshReq);

    [Benchmark(Description = "CloudMesh (box-free primitive)")]
    public ValueTask<string> CloudMesh_BoxFree()
        => _cloudMesh.SendAsync<CloudMeshLib.PingWithBehaviors, string>(in _cloudMeshReq);

    [Benchmark(Description = "MediatR 12.5.0")]
    public Task<string> MediatR()
        => _mediatR.Send(_mediatRReq);

    [Benchmark(Description = "martinothar 3.0.2")]
    public ValueTask<string> Martinothar()
        => _martinothar.Send(_martinotharReq);
}
