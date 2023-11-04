using CloudMesh.DataBlocks;

// Capture block allows for simple capturing of any and all messages sent to it. Useful in unit tests where you
// want a mock consumer, that you can then Assert which messages were sent to it.

await using var capture = new CaptureBlock();
await using (var producer = new Producer(capture))
{
    await producer.SubmitAsync("hello world", null);
    await producer.SubmitAsync("the world is round", null);
    // By disposing producer here, we ensure a flush
}

var capturedMessages = capture.DequeueAll();
foreach (var msg in capturedMessages)
    Console.WriteLine(msg); 

// Will print out:
// HELLO WORLD
// THE WORLD IS ROUND

sealed class Producer : DataBlock
{
    public Producer(ICanSubmit consumer)
    {
        ReceiveAsync<string>(str => consumer.SubmitAsync(str.ToUpper(), this));
    }
}
