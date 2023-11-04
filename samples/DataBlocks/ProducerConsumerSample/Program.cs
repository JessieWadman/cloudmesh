using CloudMesh.DataBlocks;

await using var consumer = new Consumer();
await using var producer = new Producer(consumer);

// Let producer do its thing for 10 seconds
await Task.Delay(TimeSpan.FromSeconds(10));

Console.WriteLine("Shutting down...");

record Message(string Value);

sealed class Producer : DataBlock
{
    private readonly ICancelable timer;
    
    public Producer(ICanSubmit consumer)
    {
        // Schedule sending a string to ourselves once every second
        timer = DataBlockScheduler.ScheduleTellRepeatedly(this, TimeSpan.FromSeconds(1), "Hello world", this);

        // When we receive a string, send a message to the consumer
        ReceiveAsync<string>(msg => consumer.SubmitAsync(new Message(msg), this));
    }

    protected override ValueTask AfterStop()
    {
        timer.Cancel();
        return ValueTask.CompletedTask;
    }
}

sealed class Consumer : DataBlock
{
    public Consumer()
    {
        // When we receive a message
        ReceiveAsync<Message>(msg =>
        {
            // Write it to the console
            Console.WriteLine(msg.Value);
            return ValueTask.CompletedTask;
        });
    }
}