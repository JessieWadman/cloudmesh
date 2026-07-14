using CloudMesh.Utils;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using CloudMesh.Variant;

namespace CloudMesh.DataBlocks;

/// <summary>
/// Marker type used with <c>ReceiveAny</c> handlers to catch messages of any type not matched by a more
/// specific <c>Receive&lt;T&gt;</c> handler. It is never instantiated or sent as a message itself.
/// </summary>
public static class Any
{
}

/// <summary>
/// The singleton marker message a <see cref="DataBlock"/> delivers to itself when its idle timeout elapses
/// (see <c>SetIdleTimeout</c> and <c>ReceiveTimeout</c>).
/// </summary>
public sealed class Timeout
{
    /// <summary>The shared idle-timeout marker instance.</summary>
    public static readonly Timeout Instance = new();
    private Timeout()
    {
    }
}

/// <summary>
/// Wraps a message travelling through a block's mailbox together with an optional reference to the sender, so a
/// handler can reply via <see cref="DataBlock.Sender"/>. The payload is carried as a boxing-free
/// <see cref="Value"/>.
/// </summary>
public readonly struct Envelope
{
    /// <summary>Creates an envelope around a non-null message.</summary>
    /// <param name="message">The message payload; must not be null.</param>
    /// <param name="sender">The sender to reply to, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public Envelope(Value message, IDataBlockRef? sender)
    {
        if (message.IsNull)
            throw new ArgumentNullException(nameof(message));
        Message = message;
        Sender = sender;
    }

    /// <summary>The block that sent this message, if any, so a handler can reply to it.</summary>
    public IDataBlockRef? Sender { get; }
    /// <summary>The message payload, stored without boxing value types.</summary>
    public Value Message { get; }
}

/// <summary>
/// The "tell" surface of a block: fire-and-forget message submission into a mailbox. Sending is one-way — there
/// is no return value; a handler replies (if at all) by sending back to the <c>sender</c>.
/// </summary>
public interface ICanSubmit
{
    /// <summary>
    /// Submits a message asynchronously, awaiting available mailbox capacity if the target is applying
    /// backpressure (a bounded, full mailbox).
    /// </summary>
    /// <typeparam name="T">The message type. Value types are carried without boxing.</typeparam>
    /// <param name="message">The message to send.</param>
    /// <param name="sender">The sender to reply to, or <see langword="null"/>.</param>
    ValueTask SubmitAsync<T>(T message, IDataBlockRef? sender);

    /// <summary>
    /// Attempts to submit a message without waiting. Returns <see langword="false"/> immediately if the target's
    /// mailbox is full (backpressure) or the target has stopped — nothing is enqueued in that case.
    /// </summary>
    /// <typeparam name="T">The message type. Value types are carried without boxing.</typeparam>
    /// <param name="message">The message to send.</param>
    /// <param name="sender">The sender to reply to, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the message was enqueued; otherwise <see langword="false"/>.</returns>
    bool TrySubmit<T>(T message, IDataBlockRef? sender);
}

/// <summary>
/// An opaque, sendable reference to a block, exposing only its identity (<see cref="Name"/>/<see cref="Path"/>),
/// its <see cref="Parent"/>, and the ability to submit messages — but not its internals. This is what handlers
/// hold and pass around as a "sender".
/// </summary>
public interface IDataBlockRef : ICanSubmit
{
    /// <summary>The parent block in the hierarchy, or <see langword="null"/> for a root block.</summary>
    IDataBlockRef? Parent { get; }
    /// <summary>The block's name, unique among its parent's children.</summary>
    string Name { get; }
    /// <summary>The block's slash-delimited path from the root (e.g. <c>/root/worker-3</c>).</summary>
    string Path { get; }
}

/// <summary>
/// The full block contract used internally to route messages: extends <see cref="IDataBlockRef"/> with access to
/// the underlying mailbox channel and graceful shutdown.
/// </summary>
public interface IDataBlock : IDataBlockRef
{
    /// <summary>Gets the block's mailbox channel. Intended for the framework's routing, not application code.</summary>
    Channel<Envelope> GetChannel();
    /// <summary>Stops the block gracefully: drains the mailbox, runs shutdown hooks, and stops all children.</summary>
    ValueTask StopAsync();
}

/// <summary>
/// The base class for an actor-like, channel-backed message processor. Each block owns a private, bounded
/// <see cref="Channel{T}"/> mailbox and a single reader loop, so <b>one message is handled at a time, in arrival
/// order, on one thread at a time</b> — handler code never runs concurrently with itself and needs no locking of
/// the block's own state.
/// </summary>
/// <remarks>
/// <para>
/// Derive from <see cref="DataBlock"/> and register typed handlers in your constructor (or a
/// <see cref="Become(Action)"/> behaviour) with <see cref="Receive{T}(Action{T})"/> /
/// <see cref="ReceiveAsync{T}(Func{T, ValueTask})"/>, falling back to <see cref="ReceiveAny(Action{object})"/>.
/// Other blocks send messages one-way via <see cref="ICanSubmit.SubmitAsync{T}"/> /
/// <see cref="ICanSubmit.TrySubmit{T}"/>; a handler can reply to <see cref="Sender"/>.
/// </para>
/// <para>
/// Blocks form a supervision hierarchy: create children with <c>ChildOf</c>, and stopping a parent
/// (<see cref="StopAsync"/>) drains its mailbox and stops all children. Values travel as boxing-free
/// <see cref="Value"/> payloads. An exception thrown by a handler stops the block (after
/// <see cref="UnhandledException"/> is invoked); a handler returning <see langword="false"/> routes the message
/// to <see cref="Unhandled(Envelope)"/>.
/// </para>
/// <para>
/// This base gives you point-to-point processing; the derived blocks add fan-in
/// (<see cref="BufferBlock{T}"/>, <see cref="AggregationDataBlock{T}"/>) and fan-out
/// (<see cref="RoundRobinDataBlock"/>, <see cref="SpillOverDataBlock"/>) topologies.
/// </para>
/// <example>
/// <code>
/// public sealed class Greeter : DataBlock
/// {
///     public Greeter() => Receive&lt;string&gt;(name => Console.WriteLine($"Hello, {name}!"));
/// }
/// </code>
/// </example>
/// </remarks>
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
    /// <summary>A token that is cancelled when the block begins stopping; observe it in long-running handlers.</summary>
    protected CancellationToken StoppingToken => stoppingTokenSource.Token;
    private bool completed;
    /// <summary>Whether the block has begun stopping and will accept no further messages.</summary>
    protected bool Stopping { get; private set; }

    /// <inheritdoc/>
    public string Name { get; private set; }
    /// <inheritdoc/>
    public IDataBlockRef? Parent { get; private set; }
    /// <summary>
    /// The sender of the message currently being handled, or <see langword="null"/>. Valid only inside a handler;
    /// use it to reply. Set fresh before each message is dispatched.
    /// </summary>
    protected IDataBlockRef? Sender { get; private set; }

    private string? path;

    /// <inheritdoc/>
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
    
    /// <summary>
    /// Creates a block with a bounded mailbox of the given capacity. A small capacity gives strong backpressure
    /// (senders wait, or <see cref="ICanSubmit.TrySubmit{T}"/> fails, once it's full); a larger one absorbs
    /// bursts at the cost of memory. The mailbox has a single reader and allows multiple concurrent writers.
    /// </summary>
    /// <param name="capacity">Maximum number of queued messages before the mailbox applies backpressure.</param>
    public DataBlock(int capacity = 1)
        : this(Channel.CreateBounded<Envelope>(new BoundedChannelOptions(capacity)
        {
            // The message loop is the sole reader; multiple producers may submit concurrently.
            // Synchronous continuations are disabled, so handler code never runs on a producer thread.
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        }))
    {
    }

    /// <summary>
    /// Creates a block over a caller-supplied mailbox channel, and immediately starts its reader loop. Use this
    /// to customise the channel (bounded/unbounded, full-mode behaviour, etc.).
    /// </summary>
    /// <param name="mailbox">The channel to use as the block's mailbox.</param>
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public DataBlock(Channel<Envelope> mailbox)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
        inbox = mailbox;
        completion = Task.Run(RunAsync);
    }

    /// <summary>
    /// Creates a block that shares another block's mailbox channel (advanced; e.g. a worker reading the same
    /// inbox).
    /// </summary>
    /// <param name="inbox">The block whose mailbox channel to share.</param>
    public DataBlock(IDataBlock inbox)
        : this(inbox.GetChannel())
    {
    }

    /// <summary>A snapshot of this block's current child blocks. Safe to enumerate while children change.</summary>
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

    /// <summary>
    /// Creates and supervises a child block from a <c>new T(args)</c> expression, wiring up its parent, name, and
    /// path. Pass the construction as an expression (e.g. <c>() =&gt; new Worker(dep)</c>) rather than a
    /// constructed instance, so the framework can assign identity before the constructor runs.
    /// </summary>
    /// <typeparam name="T">The child block type.</typeparam>
    /// <param name="newExpression">A <c>() =&gt; new T(...)</c> expression describing how to construct the child.</param>
    /// <param name="name">An explicit child name; if omitted, a unique name is generated.</param>
    /// <returns>A reference to the new child block.</returns>
    /// <exception cref="ObjectDisposedException">The parent has already stopped.</exception>
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

    /// <summary>Looks up a child block by name, or returns <see langword="null"/> if none exists.</summary>
    /// <param name="name">The child's name.</param>
    protected IDataBlock? GetChild(string name)
    {
        return childrenByName.GetValueOrDefault(name);
    }

    /// <summary>
    /// Returns the existing child with the given name, or creates it from <paramref name="newExpression"/> if it
    /// doesn't exist (or has stopped). Useful for lazily routing to per-key child workers.
    /// </summary>
    /// <typeparam name="T">The child block type.</typeparam>
    /// <param name="name">The child's name.</param>
    /// <param name="newExpression">A <c>() =&gt; new T(...)</c> expression used to create the child if needed.</param>
    /// <returns>The existing or newly created child block.</returns>
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

    /// <summary>
    /// Switches the block to a new behaviour: clears all current handlers and idle timeout, then runs
    /// <paramref name="behavior"/> to register the replacement handlers. This lets a block model a state machine
    /// (e.g. <c>Disconnected</c> → <c>Connected</c>) by swapping its whole handler set atomically between messages.
    /// </summary>
    /// <param name="behavior">A callback that registers the new set of <c>Receive</c> handlers.</param>
    public void Become(Action behavior)
    {
        handlers.Clear();
        fallbackHandlerCache.Clear();
        idleTimeout = null;
        idleTimer?.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
        timeoutHandler = null;

        behavior();
    }

    /// <summary>
    /// Registers a synchronous handler for messages of type <typeparamref name="T"/> that returns whether the
    /// message was handled. Returning <see langword="false"/> routes the message to <see cref="Unhandled(Envelope)"/>.
    /// </summary>
    /// <typeparam name="T">The message type to handle (not <see cref="Any"/> — use <c>ReceiveAny</c> for that).</typeparam>
    /// <param name="handler">Handler returning <see langword="true"/> if it consumed the message.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is <see cref="Any"/>.</exception>
    protected void Receive<T>(Func<T, bool> handler)
    {
        if (typeof(T) == typeof(Any))
            throw new InvalidOperationException("Use ReceiveAny instead!");

        handlers[typeof(T)] = item => new ValueTask<bool>(handler(item.As<T>()));
    }

    /// <summary>
    /// Registers an asynchronous handler for messages of type <typeparamref name="T"/> that returns whether the
    /// message was handled. Returning <see langword="false"/> routes the message to <see cref="Unhandled(Envelope)"/>.
    /// </summary>
    /// <typeparam name="T">The message type to handle (not <see cref="Any"/>).</typeparam>
    /// <param name="handler">Async handler returning <see langword="true"/> if it consumed the message.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is <see cref="Any"/>.</exception>
    protected void ReceiveAsync<T>(Func<T, ValueTask<bool>> handler)
    {
        if (typeof(T) == typeof(Any))
            throw new InvalidOperationException("Use ReceiveAny instead!");

        handlers[typeof(T)] = item => handler(item.As<T>());
    }

    /// <summary>Registers a synchronous handler for messages of type <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The message type to handle (not <see cref="Any"/>).</typeparam>
    /// <param name="handler">The handler to invoke.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is <see cref="Any"/>.</exception>
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

    /// <summary>Registers an asynchronous handler for messages of type <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The message type to handle (not <see cref="Any"/>).</typeparam>
    /// <param name="handler">The async handler to invoke.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is <see cref="Any"/>.</exception>
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

    /// <summary>
    /// Registers a catch-all async handler invoked for any message not matched by a specific
    /// <see cref="Receive{T}(Action{T})"/>. The message is passed as a raw <see cref="Value"/> so you can inspect
    /// its <see cref="Value.Type"/> without boxing.
    /// </summary>
    /// <param name="handler">Async handler returning <see langword="true"/> if it consumed the message.</param>
    protected void ReceiveAnyAsync(Func<Value, ValueTask<bool>> handler)
    {
        handlers[typeof(Any)] = handler;
    }

    /// <summary>Registers a synchronous catch-all handler for any otherwise-unmatched message.</summary>
    /// <param name="handler">Handler returning <see langword="true"/> if it consumed the message.</param>
    protected void ReceiveAny(Func<Value, bool> handler)
    {
        handlers[typeof(Any)] = item => new ValueTask<bool>(handler(item));
    }

    /// <summary>Registers an asynchronous catch-all handler for any otherwise-unmatched message.</summary>
    /// <param name="handler">The async handler to invoke.</param>
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

    /// <summary>
    /// Registers a synchronous catch-all handler that receives the message as a boxed <see cref="object"/>
    /// (convenient when type doesn't matter, at the cost of boxing value-type payloads).
    /// </summary>
    /// <param name="handler">The handler to invoke.</param>
    protected void ReceiveAny(Action<object> handler)
    {
        handlers[typeof(Any)] = item =>
        {
            handler(item);
            return new(true);
        };
    }

    /// <summary>
    /// Registers the async handler invoked when the idle timeout set by <see cref="SetIdleTimeout(TimeSpan?)"/>
    /// elapses with no messages received. If no timeout handler is registered, an elapsed idle timeout stops the
    /// block instead.
    /// </summary>
    /// <param name="handler">The async handler to run on idle timeout.</param>
    protected void ReceiveTimeoutAsync(Func<ValueTask> handler)
    {
        timeoutHandler = handler;
    }

    /// <summary>Registers the synchronous handler invoked when the idle timeout elapses.</summary>
    /// <param name="handler">The handler to run on idle timeout.</param>
    protected void ReceiveTimeout(Action handler)
    {
        ReceiveTimeoutAsync(() =>
        {
            handler();
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// Called when a handler throws. The block is stopped immediately afterwards. Override to log or report the
    /// failure; the default writes it to the console.
    /// </summary>
    /// <param name="error">The exception thrown by the handler.</param>
    protected virtual void UnhandledException(Exception error)
    {
        Console.WriteLine($"UNHANDLED EXCEPTION IN {Path}: {error}");
    }

    /// <summary>
    /// A hook run once on the message loop before the first message is processed. Override for async
    /// initialization (e.g. opening a connection) or to register handlers via <c>Receive</c>/<c>ReceiveAny</c>.
    /// </summary>
    protected virtual ValueTask BeforeStart() => TaskHelper.CompletedTask;

    /// <summary>
    /// A hook run once after the mailbox has drained and the loop has exited, before children are stopped.
    /// Override for async cleanup or a final flush.
    /// </summary>
    protected virtual ValueTask AfterStop() => TaskHelper.CompletedTask;

    /// <summary>
    /// Arms (or, with <see langword="null"/>, disarms) a sliding idle timeout. If no message arrives within the
    /// window, the block delivers a <see cref="Timeout"/> marker to itself, invoking the
    /// <see cref="ReceiveTimeout(Action)"/> handler if registered, or stopping the block otherwise. Each received
    /// message resets the window. Re-arming is allocation-free.
    /// </summary>
    /// <param name="idleTimeout">The idle window, or <see langword="null"/> to disable the timeout.</param>
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

    /// <summary>
    /// Called when a message has no matching handler (and no <c>ReceiveAny</c> catch-all), or when a handler
    /// returned <see langword="false"/>. The default logs a warning; override to dead-letter or forward.
    /// </summary>
    /// <param name="envelope">The unhandled message and its sender.</param>
    protected virtual void Unhandled(Envelope envelope)
    {
        Debug.WriteLine($"WARN [{Path}] Unhandled: {envelope}");
    }

    /// <summary>Removes a child from this block's supervision set. Called by the framework as children stop.</summary>
    /// <param name="child">The child block to detach.</param>
    public void RemoveChild(IDataBlock child)
    {
        children.Remove(child);
        childrenByName.Remove(child.Name);
    }

    private readonly AsyncLock stopLocker = new();

    /// <summary>
    /// Stops the block gracefully: signals <see cref="StoppingToken"/>, stops accepting new messages, drains the
    /// mailbox, runs <see cref="AfterStop"/>, then recursively stops all children. Idempotent and safe to await
    /// concurrently.
    /// </summary>
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

    /// <summary>
    /// Requests a graceful stop from inside a handler without awaiting it (fire-and-forget). Use this to stop
    /// self; use <see cref="StopAsync"/> from outside when you need to await completion.
    /// </summary>
    protected void Stop()
    {
        _ = Task.Run(StopAsync).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public bool TrySubmit<T>(T message, IDataBlockRef? sender)
    {
        if (completed)
            return false;
        var value = message is Value v ? v : Value.Create(message);
        var envelope = new Envelope(value, sender);
        return inbox.Writer.TryWrite(envelope);
    }

    /// <inheritdoc/>
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

    /// <summary>Stops the block (see <see cref="StopAsync"/>) and releases its resources.</summary>
    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await StopAsync();
        Dispose(true);
    }


    /// <summary>Releases resources held by the block.</summary>
    /// <param name="disposeManagedResources"><see langword="true"/> to also dispose managed resources.</param>
    protected virtual void Dispose(bool disposeManagedResources)
    {
        if (disposeManagedResources)
            stoppingTokenSource.Dispose();
    }

    /// <summary>Synchronously stops and disposes the block. Prefer <see cref="DisposeAsync"/>.</summary>
    public void Dispose()
    {
        var dispose = DisposeAsync();
        if (dispose.IsCompleted)
            return;
        dispose.AsTask().Wait(CancellationToken.None);
    }
}