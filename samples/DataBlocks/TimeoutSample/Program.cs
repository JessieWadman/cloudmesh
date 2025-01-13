// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using CloudMesh.DataBlocks;

using var stopApplication = new CancellationTokenSource();
await using var idleTimeout = new IdleTimeout(stopApplication);
try
{
    await Task.Delay(TimeSpan.FromSeconds(50), stopApplication.Token);
}
catch (TaskCanceledException)
{
    Console.WriteLine("Stopping...");
}

Console.WriteLine("Exiting");

public class IdleTimeout : DataBlock
{
    private readonly CancellationTokenSource stopApplication;
    private DateTime lastTimerStarted = DateTime.UtcNow;
    
    public IdleTimeout(CancellationTokenSource stopApplication)
    {
        this.stopApplication = stopApplication;
        WaitingToStart();
    }

    protected override ValueTask AfterStop()
    {
        Console.WriteLine("AfterStop called after {0:n0} ms", (DateTime.Now - lastTimerStarted).TotalMilliseconds);
        stopApplication.Cancel();
        return ValueTask.CompletedTask;
    }

    private void WaitingToStart()
    {
        Console.WriteLine("WaitingToStart: Starting 5 second timer");
        var stopWatch = Stopwatch.StartNew();
        SetIdleTimeout(TimeSpan.FromSeconds(5));
        ReceiveTimeout(() =>
        {
            Console.WriteLine("WaitingToStart: Received timeout after {0} ms", stopWatch.ElapsedMilliseconds);
            Become(Running);
        });
        // Should invoke callback after 5 seconds of inactivity
    }

    private void Running()
    {
        // This simulates backwards compatibility, because no timeout handler is set.
        // Everytime we call Become() we reset the timeout handler.
        // And because we don't set a timeout handler, the block will terminate after 7 seconds of inactivity.
        Console.WriteLine("Running: Starting 7 second timer");
        lastTimerStarted = DateTime.Now;
        SetIdleTimeout(TimeSpan.FromSeconds(7));
        // Should terminate after 7 seconds of inactivity because no timeout handler is set
    }
}