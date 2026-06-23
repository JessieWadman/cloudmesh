using CloudMesh.Utils;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using CloudMesh.Variant;

namespace CloudMesh.DataBlocks;

public static class Any 
{
}

public sealed class Timeout
{
    public static readonly Timeout Instance = new();
    private Timeout()
    {
    }
}

public readonly struct Envelope
{
    public Envelope(Value message, IDataBlockRef? sender)
    {
        if (message.IsNull)
            throw new ArgumentNullException(nameof(message));
        Message = message;
        Sender = sender;
    }

    public IDataBlockRef? Sender { get; }
    public Value Message { get; }
}

public interface ICanSubmit
{
    ValueTask SubmitAsync<T>(T message, IDataBlockRef? sender);
    bool TrySubmit<T>(T message, IDataBlockRef? sender);
}

public interface IDataBlockRef : ICanSubmit
{
    IDataBlockRef? Parent { get; }
    string Name { get; }
    string Path { get; }
}

public interface IDataBlock : IDataBlockRef
{
    Channel<Envelope> GetChannel();
    ValueTask StopAsync();
}

public abstract class DataBlock :
    IDataBlock,
    IDataBlockContainer,
    IDataBlockInitializer,
    IAsyncDisposable,
    IDisposable
{
    private readonly Channel<Envelope> inbox;
    private readonly HashSet<IDataBlock> children = new();
    private readonly Dictionary<string, IDataBlock> childrenByName = new();
    private readonly Dictionary<Type, Func<Value, ValueTask<bool>>> handlers = new();
    private readonly Dictionary<Type, Func<Value, ValueTask<bool>>> fallbackHandlerCache = new();
    private Func<ValueTask>? timeoutHandler;
    private TimeSpan? idleTimeout;
    private Timer? idleTimer;
    private long lastActivityTicks;
        
    private readonly Task completion;
    private readonly CancellationTokenSource stoppingTokenSource = new();
    protected CancellationToken StoppingToken => stoppingTokenSource.Token;
    private bool completed;
    protected bool Stopping { get; private set; }
        
    public string Name { get; private set; }
    public IDataBlockRef? Parent { get; private set; }
    protected IDataBlockRef? Sender { get; private set; }

    private string? path;
        
    public string Path
    {
        get
        {
            if (path != null)
                return path;

            var sb = new StringBuilder();

            if (Parent != null && Parent != this)
                sb.Append(Parent.Path);
            sb.Append('/');
            sb.Append(Name);
            path = sb.ToString();
            return path;
        }
    }

    IDataBlockRef IDataBlockInitializer.Parent { set => Parent = value; }
    string IDataBlockInitializer.Name { get => Name; set => Name = value; }

    Channel<Envelope> IDataBlock.GetChannel() => inbox;
    
    public DataBlock(int capacity = 1)
        : this(Channel.CreateBounded<Envelope>(new BoundedChannelOptions(capacity)
        {
            // The message loop is the sole reader; multiple producers may submit concurrently.
            // Synchronous continuations are disabled so handler code never runs on a producer thread.
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        }))
    {
    }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public DataBlock(Channel<Envelope> mailbox)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
        inbox = mailbox;
        completion = Task.Run(RunAsync);
    }

    public DataBlock(IDataBlock inbox)
        : this(inbox.GetChannel())
    {
    }

    protected IEnumerable<IDataBlock> Children 
    {
        get
        {
            // Create snapshot of children
            var result = children.ToArray();
            // Return as IEnumerable
            return result;
        }
    }

    protected IDataBlock ChildOf<T>(Expression<Func<T>> newExpression, string? name = null)
        where T : IDataBlock
    {
        if (completed)
            throw new ObjectDisposedException(GetType().Name);
        var dataBlock = this.InitializeDataBlock(newExpression, name);
        children.Add(dataBlock);
        childrenByName[dataBlock.Name] = dataBlock;
        return dataBlock;
    }

    IDataBlock IDataBlockContainer.ChildOf<T>(Expression<Func<T>> newExpression, string? name)
        => ChildOf(newExpression, name);

    protected IDataBlock? GetChild(string name)
    {
        return childrenByName.GetValueOrDefault(name);
    }

    protected IDataBlock GetOrAddChild<T>(string name, Expression<Func<T>> newExpression)
        where T : IDataBlock
    {
        var dataBlock = GetChild(name);
        if (dataBlock is null)
            return ChildOf(newExpression, name);

        var child = (DataBlock)dataBlock!;
        if (!child.Stopping)
            return child;

        if (!child.completed)
            child.completion.Wait();
        return ChildOf(newExpression, name);
    }

    public void Become(Action behavior)
    {
        handlers.Clear();
        fallbackHandlerCache.Clear();
        idleTimeout = null;
        idleTimer?.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
        timeoutHandler = null;

        behavior();
    }

    protected void Receive<T>(Func<T, bool> handler)
    {
        if (typeof(T) == typeof(Any))
            throw new InvalidOperationException("Use ReceiveAny instead!");

        handlers[typeof(T)] = item => new ValueTask<bool>(handler(item.As<T>()));
    }

    protected void ReceiveAsync<T>(Func<T, ValueTask<bool>> handler)
    {
        if (typeof(T) == typeof(Any))
            throw new InvalidOperationException("Use ReceiveAny instead!");

        handlers[typeof(T)] = item => handler(item.As<T>());
    }
    
    protected void Receive<T>(Action<T> handler)
    {
        if (typeof(T) == typeof(Any))
            throw new InvalidOperationException("Use ReceiveAny instead!");

        handlers[typeof(T)] = item =>
        {
            handler(item.As<T>());
            return new(true);
        };
    }

    protected void ReceiveAsync<T>(Func<T, ValueTask> handler)
    {
        if (typeof(T) == typeof(Any))
            throw new InvalidOperationException("Use ReceiveAny instead!");

        handlers[typeof(T)] = async item =>
        {
            var op = handler(item.As<T>());
            if (!op.IsCompleted)
                await op;
            return true;
        };
    }

    protected void ReceiveAnyAsync(Func<Value, ValueTask<bool>> handler)
    {
        handlers[typeof(Any)] = handler;
    }
    
    protected void ReceiveAny(Func<Value, bool> handler)
    {
        handlers[typeof(Any)] = item => new ValueTask<bool>(handler(item));
    }

    protected void ReceiveAnyAsync(Func<Value, ValueTask> handler)
    {
        handlers[typeof(Any)] = async item =>
        {
            var op = handler(item);
            if (!op.IsCompleted)
                await op;
            return true;
        };
    }
    
    protected void ReceiveAny(Action<object> handler)
    {
        handlers[typeof(Any)] = item =>
        {
            handler(item);
            return new(true);
        };
    }
        
    protected void ReceiveTimeoutAsync(Func<ValueTask> handler)
    {
        timeoutHandler = handler;
    }
        
    protected void ReceiveTimeout(Action handler)
    {
        ReceiveTimeoutAsync(() =>
        {
            handler();
            return ValueTask.CompletedTask;
        });
    }

    protected virtual void UnhandledException(Exception error)
    {
        Console.WriteLine($"UNHANDLED EXCEPTION IN {Path}: {error}");
    }

    protected virtual ValueTask BeforeStart() => TaskHelper.CompletedTask;
    protected virtual ValueTask AfterStop() => TaskHelper.CompletedTask;

    protected void SetIdleTimeout(TimeSpan? idleTimeout)
    {
        this.idleTimeout = idleTimeout;

        if (idleTimeout.HasValue)
        {
            // A single reusable timer per block, re-armed via Change() — no allocation per reset,
            // so a sliding idle timeout costs nothing on the hot path (see the message loop).
            lastActivityTicks = Environment.TickCount64;
            idleTimer ??= new Timer(
                static state => ((DataBlock)state!).OnIdleTimerFired(),
                this,
                System.Threading.Timeout.InfiniteTimeSpan,
                System.Threading.Timeout.InfiniteTimeSpan);
            idleTimer.Change(idleTimeout.Value, System.Threading.Timeout.InfiniteTimeSpan);
        }
        else
        {
            idleTimer?.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
        }
    }

    private void OnIdleTimerFired() => TrySubmit(Timeout.Instance, this);

    private async Task RunAsync()
    {
        try
        {
            await BeforeStart();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Unhandled exception in BeforeStart of {0}: {1}", Path, error);
            return;
        }

        var reader = inbox.Reader;
        while (true)
        {
            try
            {
                // You must call Stop on the block to gracefully exit an application.
                // Drain synchronously with TryRead and only await when the inbox is empty,
                // avoiding a per-message async-enumerator state machine.
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out var item))
                    {
                        if (item.Message.IsNull)
                            continue;

                        var messageType = item.Message.Type!;

                        if (messageType == typeof(Timeout))
                        {
                            // The timeout was disabled (e.g. via Become) before this marker was dequeued.
                            if (idleTimeout is not { } idle)
                                continue;

                            var idleMs = (long)idle.TotalMilliseconds;
                            var sinceActivity = Environment.TickCount64 - lastActivityTicks;
                            if (sinceActivity < idleMs)
                            {
                                // Not actually idle: a message reset the clock after the timer fired,
                                // or the one-shot timer fired marginally early. Re-arm for the remaining
                                // time so a genuine timeout is never lost.
                                idleTimer?.Change(
                                    TimeSpan.FromMilliseconds(Math.Max(1, idleMs - sinceActivity)),
                                    System.Threading.Timeout.InfiniteTimeSpan);
                                continue;
                            }

                            if (timeoutHandler != null)
                                await timeoutHandler.Invoke();
                            else
                                Stop();
                            continue;
                        }

                        // Any real message counts as activity and slides the idle timeout.
                        // Re-arming a reusable timer via Change() does not allocate.
                        if (idleTimeout is { } activeIdle)
                        {
                            lastActivityTicks = Environment.TickCount64;
                            idleTimer?.Change(activeIdle, System.Threading.Timeout.InfiniteTimeSpan);
                        }

                        Sender = item.Sender;

                        if (!TryGetHandler(messageType, out var handler))
                        {
                            try
                            {
                                Unhandled(item);
                            }
                            catch
                            {
                            }
                        }
                        else
                        {
                            var success = false;
                            try
                            {
                                var handlerOp = handler(item.Message);
                                if (handlerOp.IsCompleted)
                                    success = handlerOp.Result;
                                else
                                    success = await handlerOp;
                            }
                            catch (Exception ex)
                            {
                                Stop();
                                try
                                {
                                    UnhandledException(ex);
                                }
                                catch
                                {
                                }
                            }

                            if (!success)
                            {
                                try
                                {
                                    Unhandled(item);
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                }

                break;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Unhandled exception in {0}: {1}", Path, error);
                Stop();
            }
        }

        await AfterStop();
            
        return;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetHandler(Type type, [NotNullWhen(true)] out Func<Value, ValueTask<bool>>? handler)
    {
        if (handlers.TryGetValue(type, out handler))
            return true;

        if (fallbackHandlerCache.TryGetValue(type, out handler))
            return true;
        
        // Reflection-fallback path
        foreach (var h in handlers)
        {
            if (h.Key.IsAssignableFrom(type))
            {
                fallbackHandlerCache[type] = h.Value;
                handler = h.Value;
                return true;
            }
        }
        
        // Any handler path
        var hasAnyHandler = handlers.TryGetValue(typeof(Any), out var anyHandler);
        
        if (hasAnyHandler)
        {
            handler = anyHandler!;
            fallbackHandlerCache[type] = anyHandler!;
            return true;
        }
        
        handler = null;
        return false;
    }

    protected virtual void Unhandled(Envelope envelope)
    {
        Debug.WriteLine($"WARN [{Path}] Unhandled: {envelope}");
    }

    public void RemoveChild(IDataBlock child)
    {
        children.Remove(child);
        childrenByName.Remove(child.Name);
    }

    private readonly AsyncLock stopLocker = new();

    public async ValueTask StopAsync()
    {
        Stopping = true;
        using var _ = await stopLocker.LockAsync(CancellationToken.None);
        if (completed)
            return;

        // Notify long-running code that we are stopping
        await stoppingTokenSource.CancelAsync();

        // Stop inbox from receiving more messages
        inbox.Writer.Complete();

        // Remove ourselves from parent
        if (Parent is IDataBlockContainer container)
            container.RemoveChild(this);

        // Wait for all messages in inbox to drain
        await inbox.Reader.Completion;

        // Wait for message loop to exit
        await completion;

        // Release the idle timer (if any) now that no more messages will be processed.
        idleTimer?.Dispose();
        idleTimer = null;

        var snapshot = Children.ToArray();

        // Stop all children
        var stopChildren = snapshot
            .ToArray() // Force copy of enumeration so it can be modified during stopping 
            .Where(c => (IDataBlock?)c != null)
            .Select(c => c.StopAsync())
            .ToArray();
        await TaskHelper.WhenAll(stopChildren);

        completed = true;
    }

    protected void Stop()
    {
        _ = Task.Run(StopAsync).ConfigureAwait(false);
    }

    public bool TrySubmit<T>(T message, IDataBlockRef? sender)
    {
        if (completed)
            return false;
        var value = message is Value v ? v : Value.Create(message);
        var envelope = new Envelope(value, sender);
        return inbox.Writer.TryWrite(envelope);
    }

    public ValueTask SubmitAsync<T>(T message, IDataBlockRef? sender)
    {
        if (completed)
            throw new ObjectDisposedException(GetType().Name);

        var value = message is Value v ? v : Value.Create(message);
        var envelope = new Envelope(value, sender);
        if (!inbox.Writer.TryWrite(envelope))
        {
            Debug.WriteLine($"Backpressure detected in {Path}");
            try
            {
                BackpressureMonitor.OnBackpressureDetected?.Invoke(Path);
            }
            catch { }

            return inbox.Writer.WriteAsync(envelope);
        }
        return TaskHelper.CompletedTask;
    }

    ~DataBlock()
    {
        Dispose(false);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await StopAsync();
        Dispose(true);
    }


    protected virtual void Dispose(bool disposeManagedResources)
    {
        if (disposeManagedResources)
            stoppingTokenSource.Dispose();
    }

    public void Dispose()
    {
        var dispose = DisposeAsync();
        if (dispose.IsCompleted)
            return;
        dispose.AsTask().Wait(CancellationToken.None);
    }
}