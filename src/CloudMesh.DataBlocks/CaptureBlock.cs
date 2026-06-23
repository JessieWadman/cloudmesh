using CloudMesh.Variant;

namespace CloudMesh.DataBlocks;

// PERF: This is primarily used for testing, so boxing into the queue is fine. 
public class CaptureBlock : IDataBlockRef 
{ 
    protected readonly Queue<object?> Messages = new();
    protected readonly object Locker = new();
    
    public IDataBlockRef? Parent => null;
    public string Name { get; protected init; } = "CaptureBlock";
    public string Path { get; protected init; } = "CaptureBlock";
    
    public ValueTask SubmitAsync<T>(T message, IDataBlockRef? sender) 
    { 
        lock (Locker) 
        { 
            if (message is Value v) 
                Messages.Enqueue(v.As<object>()); 
            else
                Messages.Enqueue(message); 
        } 
        return ValueTask.CompletedTask; 
    }

    public bool TrySubmit<T>(T message, IDataBlockRef? sender)
    {
        lock (Locker)
        {
            if (message is Value v)
                Messages.Enqueue(v.As<object>());
            else
                Messages.Enqueue(message);
        }

        return true;
    }
        
    public object?[] GetAllAndClear() 
    {
        lock (Locker)
        {
            var snapshot = Messages.ToArray();
            Messages.Clear();
            return snapshot;
        }
    }

    public void Clear()
    {
        lock (Locker)
            Messages.Clear();
    }
}
