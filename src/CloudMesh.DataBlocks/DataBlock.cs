using CloudMesh.Utils;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace CloudMesh.DataBlocks
{
    public sealed class Any { }

    public class Envelope
    {
        private static long messageCounter;

        public Envelope(object message, IDataBlockRef? sender)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Sender = sender;
            MessageId = Interlocked.Increment(ref messageCounter);
        }

        public long MessageId { get; private set; }
        public IDataBlockRef? Sender { get; private set; }
        public object Message { get; private set; }
    }

    public interface ICanSubmit
    {
        ValueTask SubmitAsync(object message, IDataBlockRef? sender);
        bool TrySubmit(object message, IDataBlockRef? sender);
    }

    public interface IDataBlockRef : ICanSubmit
    {
        IDataBlockRef Parent { get; }
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
        private readonly Dictionary<Type, Func<object, ValueTask<bool>>> handlers = new();
        private readonly Task completion;
        private bool completed;
        private TimeSpan? idleTimeout;

        public string Name { get; private set; }
        public IDataBlockRef Parent { get; private set; }
        protected IDataBlockRef? Sender { get; private set; }

        private string path;
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
            : this(Channel.CreateBounded<Envelope>(capacity))
        {
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public DataBlock(Channel<Envelope> mailbox)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            inbox = mailbox;
            completion = Task.Run(() => RunAsync());
        }

        public DataBlock(IDataBlock inbox)
            : this(inbox.GetChannel())
        {
        }

        protected IEnumerable<IDataBlock> Children => children;

        protected IDataBlock ChildOf<T>(Expression<Func<T>> newExpression, string? name = null)
            where T : IDataBlock
        {
            if (completed)
                throw new ObjectDisposedException(GetType().Name);
            var dataBlock = this.InitializeDataBlock(newExpression, name);
            children.Add(dataBlock);
            return dataBlock;
        }

        IDataBlock IDataBlockContainer.ChildOf<T>(Expression<Func<T>> newExpression, string? name)
            => ChildOf(newExpression, name);

        protected IDataBlock? GetChild(string name)
            => children.FirstOrDefault(c => c.Name == name);

        protected IDataBlock GetOrAddChild<T>(string name, Expression<Func<T>> newExpression)
            where T : IDataBlock
        {
            return GetChild(name) ?? ChildOf(newExpression, name);
        }

        public void Become(Action behavior)
        {
            handlers.Clear();
            behavior();
        }

        protected void ReceiveAsync<T>(Func<T, ValueTask<bool>> handler)
        {
            if (typeof(T) == typeof(Any))
                throw new InvalidOperationException("Use ReceiveAny instead!");

            handlers[typeof(T)] = item => handler((T)item);
        }

        protected void ReceiveAsync<T>(Func<T, ValueTask> handler)
        {
            if (typeof(T) == typeof(Any))
                throw new InvalidOperationException("Use ReceiveAny instead!");

            handlers[typeof(T)] = async item =>
            {
                var op = handler((T)item);
                if (!op.IsCompleted)
                    await op;
                return true;
            };
        }

        protected void ReceiveAnyAsync(Func<object, ValueTask<bool>> handler)
        {
            handlers[typeof(Any)] = handler;
        }

        protected void ReceiveAnyAsync(Func<object, ValueTask> handler)
        {
            handlers[typeof(Any)] = async item =>
            {
                var op = handler(item);
                if (!op.IsCompleted)
                    await op;
                return true;
            };
        }

        protected virtual ValueTask BeforeStart() => TaskHelper.CompletedTask;
        protected virtual ValueTask AfterStop() => TaskHelper.CompletedTask;

        protected void SetIdleTimeout(TimeSpan idleTimeout)
        {
            this.idleTimeout = idleTimeout;
        }

        private async Task RunAsync()
        {
            await BeforeStart();

            var hasAnyHandler = handlers.TryGetValue(typeof(Any), out var anyHandler);

            while (true)
            {
                try
                {
                    await foreach (var item in inbox.Reader.ReadAllAsync(idleTimeout ?? TimeSpan.MaxValue))
                    {
                        if (item.Message is null)
                            continue;

                        Sender = item.Sender;

                        var messageType = item.Message.GetType();
                        if (!TryGetHandler(messageType, out var handler) || handler is null)
                        {
                            try
                            {
                                Unhandled(item);
                            }
                            catch { }
                        }
                        else
                        {
                            bool success = false;
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
                                Console.WriteLine($"UNHANDLED EXCEPTION IN {Path}: {ex}");
                            }
                            if (!success)
                            {
                                try
                                {
                                    Unhandled(item);
                                }
                                catch { }
                            }
                        }
                    }
                    break;
                }
                catch (TimeoutException)
                {
                    Stop();
                }
            }

            await AfterStop();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            bool TryGetHandler(Type messageType, out Func<object, ValueTask<bool>>? handler)
            {
                if (handlers.TryGetValue(messageType, out handler))
                    return true;

                if (hasAnyHandler)
                {
                    handler = anyHandler;
                    return true;
                }

                return false;
            }
        }

        protected virtual void Unhandled(Envelope envelope)
        {
            Debug.WriteLine($"WARN [{Path}] Unhandled: {envelope}");
        }

        public void RemoveChild(IDataBlock child)
        {
            if (children.Contains(child))
                children.Remove(child);
        }

        private AsyncLock stopLocker = new();

        public async ValueTask StopAsync()
        {
            using var _ = await stopLocker.LockAsync();
            if (completed)
                return;

            // Stop inbox from receiving more messages
            inbox.Writer.Complete();

            // Wait for all messages in inbox to drain
            await inbox.Reader.Completion;

            // Wait for message loop to exit
            await completion;

            // Stop all children
            var children = Children
                .ToArray() // Force copy of enumeration so it can be modified during stopping 
                .Where(c => c != null)
                .Select(c => c.StopAsync())
                .ToArray();
            await TaskHelper.WhenAll(children);

            completed = true;

            // Remove ourselves from parent
            if (Parent is IDataBlockContainer container)
                container.RemoveChild(this);
        }

        protected void Stop()
        {
            _ = Task.Run(() => StopAsync()).ConfigureAwait(false);
        }

        public bool TrySubmit(object message, IDataBlockRef? sender)
        {
            if (completed)
                return false;
            var envelope = new Envelope(message, sender);
            return inbox.Writer.TryWrite(envelope);
        }

        public ValueTask SubmitAsync(object message, IDataBlockRef? sender)
        {
            if (completed)
                throw new ObjectDisposedException(GetType().Name);

            var envelope = new Envelope(message, sender);
            if (!inbox.Writer.TryWrite(envelope))
            {
                Debug.WriteLine($"Backpresure detected in {Path}");
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
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }
    }
}
