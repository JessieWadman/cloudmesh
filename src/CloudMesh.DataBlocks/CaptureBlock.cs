using CloudMesh.Variant;

namespace CloudMesh.DataBlocks;

/// <summary>
/// A test double that acts as a message sink: it implements <see cref="IDataBlockRef"/> so you can pass it as a
/// target or sender, and simply records every message it receives (unwrapping <see cref="Value"/> payloads) into
/// an internal queue for later inspection. Primarily for unit tests; see <see cref="TestProbe"/> for assertion
/// helpers on top of it.
/// </summary>
// PERF: This is primarily used for testing, so boxing into the queue is fine.
public class CaptureBlock : IDataBlockRef
{
    /// <summary>The recorded messages, in arrival order.</summary>
    protected readonly Queue<object?> Messages = new();
    /// <summary>Lock guarding <see cref="Messages"/>.</summary>
    protected readonly object Locker = new();

    /// <inheritdoc/>
    public IDataBlockRef? Parent => null;
    /// <inheritdoc/>
    public string Name { get; protected init; } = "CaptureBlock";
    /// <inheritdoc/>
    public string Path { get; protected init; } = "CaptureBlock";

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <summary>Returns all recorded messages (in arrival order) and clears the queue.</summary>
    public object?[] GetAllAndClear()
    {
        lock (Locker)
        {
            var snapshot = Messages.ToArray();
            Messages.Clear();
            return snapshot;
        }
    }

    /// <summary>Discards all recorded messages.</summary>
    public void Clear()
    {
        lock (Locker)
            Messages.Clear();
    }
}
