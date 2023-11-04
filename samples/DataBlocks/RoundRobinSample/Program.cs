using CloudMesh.DataBlocks;

await using var workerPool = new RoundRobinDataBlock();
workerPool.AddTargets(
    // Factory for creating target blocks
    () => new BufferedConsoleWriter(),
    // We want 5 of them
    count: 5);

// We now have a round robin block, that forwards messages to 5 different worker blocks, each of which will buffer
// up to 10 messages, for up to 1 second, before writing all the messages to the console.

for (var i = 0; i < 100; i++)
    await workerPool.SubmitAsync($"Message #{i}", null);

// You can observe the output of the program, to see how it bursts out 10 messages at a time for each worker, then 
// pauses.
// If you step through the code, you can also observe how the call above to SubmitAsync will periodically block when
// all workers are busy and all buffers are full.


public sealed class BufferedConsoleWriter : BufferBlock<string>
{
    public BufferedConsoleWriter() 
        : base(
            // Buffer up to 10 messages 
            maxCapacity: 10, 
            // Flush (if there are any messages) at least once per second, even if buffer is not full.
            maxWaitTimeToFlush: TimeSpan.FromSeconds(1)) 
    {
    }

    protected override ValueTask FlushAsync(string[] messages)
    {
        foreach (var message in messages)
            Console.WriteLine(message);
        // Simulate small delay, such as writing to disk or network
        return new(Task.Delay(TimeSpan.FromSeconds(3)));
    }
}
