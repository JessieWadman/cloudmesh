using System.Diagnostics;

namespace CloudMesh.DataBlocks.Tests;

public class CancellationBlock : DataBlock
{
    public CancellationBlock(IDataBlockRef monitor)
    {
        ReceiveAsync<string>(async _ =>
        {
            try
            {
                await monitor.SubmitAsync("Processing started", this);
                await Task.Delay(60_000, StoppingToken);
                await monitor.SubmitAsync("ERROR: Processing completed!", this);
            }
            catch (OperationCanceledException)
            {
                await monitor.SubmitAsync("Data block stop signal raised", this);
                // Data block stop signal raised    
            }
            return true;
        });
    }
}

public class CancellationTests
{
    [Fact]
    public async Task TestCancellationToken()
    {
        var testProbe = new TestProbe();
        await using var block = new CancellationBlock(testProbe);
        
        // Send message to data block
        await block.SubmitAsync("Start", null);
        var log = testProbe.Expect<string>();
        Assert.Equal("Processing started", log);
        
        // Stop it, this will raise the StoppingToken
        await block.StopAsync();

        log = testProbe.Expect<string>();
        Assert.Equal("Data block stop signal raised", log);
    }
}