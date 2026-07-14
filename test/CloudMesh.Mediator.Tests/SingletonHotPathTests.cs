using CloudMesh.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudMesh.Mediator.Tests;

/// <summary>
/// Tests for the singleton send hot-path cache (Stage 2.5). Handlers are registered manually via
/// AddCloudMeshMediatorCore so each test controls the exact lifetime and behavior set, isolating the cache
/// behavior from assembly scanning.
/// </summary>
public class SingletonHotPathTests
{
    private static IServiceProvider BuildSingleton()
    {
        var services = new ServiceCollection();
        services.AddCloudMeshMediatorCore(new MediatorOptions { HandlerLifetime = ServiceLifetime.Singleton });
        services.AddSingleton<IRequestHandler<WhoAmI, Guid>, WhoAmIHandler>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Singleton_fast_path_returns_correct_result_and_reuses_the_cached_instance()
    {
        var sp = BuildSingleton();
        var mediator = sp.GetRequiredService<IMediator>();

        var first = await mediator.SendAsync<WhoAmI, Guid>(new WhoAmI(0));
        var second = await mediator.SendAsync<WhoAmI, Guid>(new WhoAmI(0));
        // The DI singleton instance is the same; the cached handler must be that same instance across calls.
        var direct = sp.GetRequiredService<IRequestHandler<WhoAmI, Guid>>();
        var directId = await direct.HandleAsync(new WhoAmI(0), default);

        Assert.NotEqual(Guid.Empty, first);
        Assert.Equal(first, second);      // same cached instance served both sends
        Assert.Equal(first, directId);    // and it's the DI singleton
    }

    [Fact]
    public async Task Scoped_handler_yields_different_instances_across_scopes_no_captive_dependency()
    {
        var services = new ServiceCollection();
        services.AddCloudMeshMediatorCore(new MediatorOptions { HandlerLifetime = ServiceLifetime.Scoped });
        services.AddScoped<IRequestHandler<WhoAmI, Guid>, WhoAmIHandler>();
        var sp = services.BuildServiceProvider();

        Guid a1, a2, b1;
        using (var scopeA = sp.CreateScope())
        {
            var m = scopeA.ServiceProvider.GetRequiredService<IMediator>();
            a1 = await m.SendAsync<WhoAmI, Guid>(new WhoAmI(0));
            a2 = await m.SendAsync<WhoAmI, Guid>(new WhoAmI(0));
        }
        using (var scopeB = sp.CreateScope())
        {
            var m = scopeB.ServiceProvider.GetRequiredService<IMediator>();
            b1 = await m.SendAsync<WhoAmI, Guid>(new WhoAmI(0));
        }

        Assert.Equal(a1, a2);       // same scoped instance within a scope
        Assert.NotEqual(a1, b1);    // different instance across scopes (not captured/stale)
    }

    [Fact]
    public async Task Transient_handler_is_resolved_each_call()
    {
        var services = new ServiceCollection();
        services.AddCloudMeshMediatorCore(new MediatorOptions { HandlerLifetime = ServiceLifetime.Transient });
        services.AddTransient<IRequestHandler<WhoAmI, Guid>, WhoAmIHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var first = await mediator.SendAsync<WhoAmI, Guid>(new WhoAmI(0));
        var second = await mediator.SendAsync<WhoAmI, Guid>(new WhoAmI(0));

        // A fresh transient instance per call -> different ids (not cached).
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Runtime_registered_behavior_still_runs_under_singleton_lifetime()
    {
        // HandlerLifetime = Singleton, handler registered as singleton, but a behavior is registered manually.
        // The runtime behavior-presence check must still see it -> the behavior runs (not bypassed by the cache).
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddCloudMeshMediatorCore(new MediatorOptions { HandlerLifetime = ServiceLifetime.Singleton });
        services.AddSingleton<IRequestHandler<LateBehaviorRequest, string>, LateBehaviorRequestHandler>();
        services.AddSingleton<IPipelineBehavior<LateBehaviorRequest, string>, LateBehavior>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();
        var recorder = sp.GetRequiredService<Recorder>();

        var result = await mediator.SendAsync<LateBehaviorRequest, string>(new LateBehaviorRequest("x"));

        Assert.Equal("handled:x", result);
        Assert.Contains("late-behavior", recorder.Events); // behavior was NOT skipped
    }

    [Fact]
    public async Task Singleton_no_handler_still_throws_HandlerNotFound()
    {
        var services = new ServiceCollection();
        services.AddCloudMeshMediatorCore(new MediatorOptions { HandlerLifetime = ServiceLifetime.Singleton });
        // No handler registered for WhoAmI.
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<HandlerNotFoundException>(
            async () => await mediator.SendAsync<WhoAmI, Guid>(new WhoAmI(0)));
    }
}
