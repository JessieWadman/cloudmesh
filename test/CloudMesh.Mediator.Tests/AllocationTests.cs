using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudMesh.Mediator.Tests;

public class AllocationTests
{
    [Fact]
    public async Task Boxfree_fastpath_with_no_behaviors_allocates_nothing()
    {
        // Singleton handler so resolution returns a cached instance; struct request so nothing boxes;
        // no pipeline behaviors registered for FastPing so the fast path must skip GetServices entirely.
        await using var provider = TestHost.Build(o => o.HandlerLifetime = ServiceLifetime.Singleton);
        var mediator = provider.Mediator();

        // Warm up JIT and first-call caches (behavior-presence cache, handler realization).
        for (var i = 0; i < 100; i++)
            _ = await mediator.SendAsync<FastPing, int>(new FastPing(i));

        // Keep results alive by summing into a long (no boxing — unlike GC.KeepAlive(object), which would
        // box each int and pollute the measurement).
        const int iterations = 1000;
        long sum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            sum += await mediator.SendAsync<FastPing, int>(new FastPing(i));
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(sum >= 0);
        Assert.True(allocated == 0, $"Expected zero allocations on the fast path, but {allocated} bytes were allocated over {iterations} sends.");
    }
}
