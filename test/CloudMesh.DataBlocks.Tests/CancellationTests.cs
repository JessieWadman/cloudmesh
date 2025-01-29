using System.Diagnostics;

namespace CloudMesh.DataBlocks.Tests;

public class CancellationBlock : DataBlock
{
    public CancellationBlock(ManualResetEvent processStarted)
    {
        ReceiveAsync<string>(async str =>
        {
            try
            {
                await Task.Delay(50);
                processStarted.Set();
                await Task.Delay(60000, StoppingToken);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Stopped!");
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
        using var processingStarted = new ManualResetEvent(false);
        await using var block = new CancellationBlock(processingStarted);
        
        var timer = Stopwatch.StartNew();
        
        // Send message to data block
        await block.SubmitAsync("Start", null);
        
        Assert.True(timer.ElapsedMilliseconds < 1000);
        
        // Wait for DataBlock to start processing the message
        processingStarted.WaitOne(System.Threading.Timeout.Infinite);
        
        Assert.True(timer.ElapsedMilliseconds < 2000);
        
        // Wait 100 ms
        await Task.Delay(100);
        
        // Stop it, this will raise the StoppingToken
        await block.StopAsync();
        
        // The inner handler of the block waits for 60000ms
        // If we pass this check, which is lower than that, we know we successfully cancelled
        Assert.True(timer.ElapsedMilliseconds < 20000);
    }
}