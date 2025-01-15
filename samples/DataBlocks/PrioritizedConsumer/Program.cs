// Showcases how to process messages in a prioritized order using a Channel.CreateUnboundedPrioritized mailbox.
// Note: This sample requires the System.Threading.Channels NuGet package version 4.7.0 or later and dotnet 9 or greater.
// Note: Prioritization only works with *unbounded* channels. Bounded channels do not support prioritization.

using System.Threading.Channels;
using CloudMesh.DataBlocks;

List<Message> messages =
[
    new() { Id = 1, Priority = Priority.Low, Text = "Low priority message #1" },
    new() { Id = 3, Priority = Priority.Low, Text = "Low priority message #2" },
    new() { Id = 2, Priority = Priority.High, Text = "High priority message #3" },
    new() { Id = 5, Priority = Priority.Medium, Text = "Medium priority message #4" },
    new() { Id = 4, Priority = Priority.High, Text = "High priority message #5" },
    new() { Id = 6, Priority = Priority.Medium, Text = "Medium priority message #6" },
    new() { Id = 7, Priority = Priority.Low, Text = "Low priority message #7" }
]; 

Console.WriteLine("By Priority:");
Console.WriteLine("========================================");
await using (var consumer = new PrioritizedConsumer(PrioritizeByMessagePriority.Comparer))
{
    foreach (var msg in messages)
        await consumer.SubmitAsync(msg, null);
}

Console.WriteLine();
Console.WriteLine("By Age:");
Console.WriteLine("========================================");

await using (var consumer = new PrioritizedConsumer(PrioritizeByMessageAge.Comparer))
{
    foreach (var msg in messages)
        await consumer.SubmitAsync(msg, null);
}

internal class PrioritizedConsumer : DataBlock
{
    private static Channel<Envelope> CreateChannel(IComparer<Envelope> prioritizer)
        => Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<Envelope>()
        {
            Comparer = prioritizer,
            SingleReader = true,
            SingleWriter = false
        });
    
    public PrioritizedConsumer(IComparer<Envelope> prioritizer) : base(CreateChannel(prioritizer))
    {
        ReceiveAsync<Message>(msg =>
        {
            Console.WriteLine("{0}: {1}", msg.Id, msg.Text);
            return new(Task.Delay(100, StoppingToken));
        });
    }
}

internal enum Priority
{
    High = 0,
    Medium = 1,
    Low = 2
}

internal sealed class Message
{
    public int Id { get; init; }
    public required string Text { get; init; }
    public required Priority Priority { get; init; }
}

// If a message is older, it has a higher priority, even if it was receiver out of order.
internal sealed class PrioritizeByMessageAge : IComparer<Envelope>
{
    public static readonly PrioritizeByMessageAge Comparer = new();
    
    private PrioritizeByMessageAge()
    {
        
    }
    
    public int Compare(Envelope? x, Envelope? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (y is null) return 1;
        if (x is null) return -1;
        if (x.Message is not Message xMessage || y.Message is not Message yMessage)
            return x.MessageId.CompareTo(y.MessageId);
        return xMessage.Id.CompareTo(yMessage.Id);
    }
}

internal sealed class PrioritizeByMessagePriority : IComparer<Envelope>
{
    public static readonly PrioritizeByMessagePriority Comparer = new();
    
    private PrioritizeByMessagePriority()
    {
    }
    
    public int Compare(Envelope? x, Envelope? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (y is null) return 1;
        if (x is null) return -1;
        if (x.Message is not Message xMessage || y.Message is not Message yMessage)
            return 0;
        return xMessage.Priority.CompareTo(yMessage.Priority);
    }
}