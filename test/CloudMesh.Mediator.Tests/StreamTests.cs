using Xunit;

namespace CloudMesh.Mediator.Tests;

public class StreamTests
{
    [Fact]
    public async Task Streams_all_items_in_order()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        var items = new List<int>();
        await foreach (var i in mediator.StreamAsync(new CountUp(4)))
            items.Add(i);

        Assert.Equal(new[] { 0, 1, 2, 3 }, items);
    }

    [Fact]
    public async Task Stream_behavior_transforms_items()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();

        var items = new List<int>();
        await foreach (var i in mediator.StreamAsync(new CountUpDoubled(3)))
            items.Add(i);

        Assert.Equal(new[] { 0, 2, 4 }, items);
    }

    [Fact]
    public async Task Stream_honors_cancellation()
    {
        await using var provider = TestHost.Build();
        var mediator = provider.Mediator();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var i in mediator.StreamAsync(new CountUp(100)).WithCancellation(cts.Token))
            {
                if (i == 2) await cts.CancelAsync();
            }
        });
    }
}
