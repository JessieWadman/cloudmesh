using CloudMesh.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudMesh.Mediator.SourceGenerator.Tests;

// These types are discovered by the generator (analyzer in this project), so calls to the ergonomic
// SendAsync(in Req)/StreamAsync(in Req) go through the GENERATED overloads (fast-dispatch when no behaviors,
// runtime pipeline when behaviors exist). The tests below mirror the runtime PipelineTests/Send/Stream/cancel
// semantics to prove the generated path matches the tested runtime path exactly (the correctness gate).

public sealed class SemanticRecorder
{
    private readonly List<string> _events = new();
    public void Add(string e) { lock (_events) _events.Add(e); }
    public IReadOnlyList<string> Events { get { lock (_events) return _events.ToArray(); } }
}

// --- Ordered behaviors (two, wrapping one handler) ---
public readonly record struct GTracked(string Message) : IRequest<string>;

public sealed class GTrackedHandler : IRequestHandler<GTracked, string>
{
    private readonly SemanticRecorder _r;
    public GTrackedHandler(SemanticRecorder r) => _r = r;
    public ValueTask<string> HandleAsync(GTracked request, CancellationToken ct)
    {
        _r.Add("handler");
        return new("handled:" + request.Message);
    }
}

public sealed class GFirstBehavior : IPipelineBehavior<GTracked, string>
{
    private readonly SemanticRecorder _r;
    public GFirstBehavior(SemanticRecorder r) => _r = r;
    public async ValueTask<string> HandleAsync(GTracked request, RequestHandlerDelegate<string> next, CancellationToken ct)
    {
        _r.Add("first:before");
        var result = await next();
        _r.Add("first:after");
        return result;
    }
}

public sealed class GSecondBehavior : IPipelineBehavior<GTracked, string>
{
    private readonly SemanticRecorder _r;
    public GSecondBehavior(SemanticRecorder r) => _r = r;
    public async ValueTask<string> HandleAsync(GTracked request, RequestHandlerDelegate<string> next, CancellationToken ct)
    {
        _r.Add("second:before");
        var result = await next();
        _r.Add("second:after");
        return result;
    }
}

// --- Short-circuiting behavior ---
public readonly record struct GGuarded(bool Allow) : IRequest<string>;

public sealed class GGuardedHandler : IRequestHandler<GGuarded, string>
{
    public ValueTask<string> HandleAsync(GGuarded request, CancellationToken ct) => new("handled");
}

public sealed class GGuardBehavior : IPipelineBehavior<GGuarded, string>
{
    public ValueTask<string> HandleAsync(GGuarded request, RequestHandlerDelegate<string> next, CancellationToken ct)
        => request.Allow ? next() : new ValueTask<string>("blocked");
}

// --- Exception propagation (no behaviors -> fast path) ---
public readonly record struct GThrows(string Message) : IRequest<int>;

public sealed class GThrowsHandler : IRequestHandler<GThrows, int>
{
    public ValueTask<int> HandleAsync(GThrows request, CancellationToken ct)
        => throw new InvalidOperationException(request.Message);
}

// --- Cancellation (no behaviors -> fast path) ---
public readonly record struct GCancelSensitive(int Value) : IRequest<int>;

public sealed class GCancelSensitiveHandler : IRequestHandler<GCancelSensitive, int>
{
    public ValueTask<int> HandleAsync(GCancelSensitive request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return new(request.Value);
    }
}

// --- Stream with behavior (behavior path) and without (fast path) ---
public readonly record struct GCountUp(int Count) : IStreamRequest<int>;

public sealed class GCountUpHandler : IStreamRequestHandler<GCountUp, int>
{
    public async IAsyncEnumerable<int> HandleAsync(
        GCountUp request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 0; i < request.Count; i++) { ct.ThrowIfCancellationRequested(); yield return i; await Task.Yield(); }
    }
}

public class GeneratedDispatchSemanticTests
{
    private static (IServiceProvider Sp, SemanticRecorder Rec) Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SemanticRecorder>();
        // The open-generic CountingBehavior<,> (declared in EndToEndTests) is registered globally by the generated
        // method and applies to EVERY request, so its dependency must be available.
        services.AddSingleton<OpenGenericInvocationCounter>();
        services.AddCloudMeshMediatorGeneratedCloudMesh_Mediator_SourceGenerator_Tests();
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<SemanticRecorder>());
    }

    [Fact]
    public async Task Behaviors_wrap_handler_in_registration_order_through_generated_overload()
    {
        var (sp, rec) = Build();
        var mediator = sp.GetRequiredService<IMediator>();

        // Ergonomic call -> generated SendAsync(in GTracked) -> runtime pipeline (has behaviors).
        var result = await mediator.SendAsync(new GTracked("x"));

        Assert.Equal("handled:x", result);
        Assert.Equal(
            new[] { "first:before", "second:before", "handler", "second:after", "first:after" },
            rec.Events);
    }

    [Fact]
    public async Task Behavior_short_circuits_without_calling_handler_through_generated_overload()
    {
        var (sp, _) = Build();
        var mediator = sp.GetRequiredService<IMediator>();

        Assert.Equal("blocked", await mediator.SendAsync(new GGuarded(false)));
        Assert.Equal("handled", await mediator.SendAsync(new GGuarded(true)));
    }

    [Fact]
    public async Task Exception_propagates_through_generated_fast_path()
    {
        var (sp, _) = Build();
        var mediator = sp.GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await mediator.SendAsync(new GThrows("boom")));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task Cancellation_flows_through_generated_fast_path()
    {
        var (sp, _) = Build();
        var mediator = sp.GetRequiredService<IMediator>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await mediator.SendAsync(new GCancelSensitive(1), cts.Token));
    }

    [Fact]
    public async Task Fast_path_and_interface_path_agree_for_no_behavior_request()
    {
        var (sp, _) = Build();
        var mediator = sp.GetRequiredService<IMediator>();

        // Generated fast path (sender is concrete Mediator).
        var viaGenerated = await mediator.SendAsync(new GCancelSensitive(41));
        // Force the box-free primitive directly (bypasses the generated overload).
        var viaPrimitive = await mediator.SendAsync<GCancelSensitive, int>(new GCancelSensitive(41));

        Assert.Equal(41, viaGenerated);
        Assert.Equal(viaPrimitive, viaGenerated);
    }

    [Fact]
    public async Task Custom_ISender_falls_back_to_box_free_primitive_not_fast_path()
    {
        // A non-Mediator ISender must NOT hit the concrete fast path; the generated overload falls back to
        // sender.SendAsync<Req,Resp>. This proves mocks / custom impls keep working.
        var custom = new RecordingSender();
        var _ = await custom.SendAsync(new GCancelSensitive(5));
        Assert.True(custom.BoxFreePrimitiveCalled);
    }

    [Fact]
    public async Task Stream_fast_path_emits_all_items_in_order()
    {
        var (sp, _) = Build();
        var mediator = sp.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var i in mediator.StreamAsync(new GCountUp(4)))
            items.Add(i);

        Assert.Equal(new[] { 0, 1, 2, 3 }, items);
    }

    // Minimal custom ISender that records which primitive was invoked.
    private sealed class RecordingSender : ISender
    {
        public bool BoxFreePrimitiveCalled;

        public ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest<TResponse>
        {
            BoxFreePrimitiveCalled = true;
            return new ValueTask<TResponse>(default(TResponse)!);
        }

        public ValueTask<TResponse> InternalSendDynamicAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => new(default(TResponse)!);

        public IAsyncEnumerable<TResponse> StreamAsync<TRequest, TResponse>(in TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IStreamRequest<TResponse> => Empty<TResponse>();

        public IAsyncEnumerable<TResponse> InternalStreamDynamicAsync<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => Empty<TResponse>();

        private static async IAsyncEnumerable<T> Empty<T>() { yield break; }
    }
}
