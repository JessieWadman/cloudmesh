namespace CloudMesh.DataBlocks;

public class CaptureBlock : IDataBlockRef 
{ 
    protected readonly Queue<object> Messages = new();
    protected readonly object Locker = new();
    
    public IDataBlockRef? Parent => null;
    public string Name { get; protected set; } = "CaptureBlock";
    public string Path { get; protected set; } = "CaptureBlock";
    
    public ValueTask SubmitAsync(object message, IDataBlockRef? sender) 
    { 
        lock (Locker) 
        { 
            Messages.Enqueue(message); 
        } 
        return ValueTask.CompletedTask; 
    }

    public bool TrySubmit(object message, IDataBlockRef? sender)
    {
        lock (Locker)
            Messages.Enqueue(message);
        return true;
    }
        
    public object[] GetAllAndClear() 
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
