using System.Collections.Concurrent;

namespace CloudMesh.DataBlocks
{
    public class CaptureBlock : DataBlock
    {
        private readonly ConcurrentQueue<object> messages = new();

        public CaptureBlock()
            : base(1)
        {
            ReceiveAnyAsync(obj =>
            {
                messages.Enqueue(obj);
                return ValueTask.CompletedTask;
            });
        }

        public object[] DequeueAll()
        {
            var snapshot = messages.ToArray();
            messages.Clear();
            return snapshot;
        }

        public T Expect<T>(int timeToWaitInMilliseconds = 1000, Func<T, bool>? predicate = null)
        {
            var waitUntil =  DateTime.UtcNow.AddMilliseconds(timeToWaitInMilliseconds);
            while (true)
            {
                if (messages.TryDequeue(out var result))
                {
                    if (result is T value)
                    {
                        if (predicate == null || predicate(value))
                            return value;
                        
                        throw new InvalidOperationException($"Expect message of type {typeof(T).Name} and got one, but the predicate failed.");    
                    }

                    throw new InvalidOperationException($"Expect message of type {typeof(T).Name} but got {result.GetType().Name}");
                }
                
                Thread.Sleep(10);
                
                if (DateTime.UtcNow > waitUntil)
                    throw new TimeoutException("No message received within the expected time.");
            }
        }
        
        public void ExpectNoMessage(int timeToWaitInMilliseconds = 500)
        {
            var waitUntil =  DateTime.UtcNow.AddMilliseconds(timeToWaitInMilliseconds);
            while (true)
            {
                if (messages.TryDequeue(out var result))
                {
                    throw new InvalidOperationException($"Expected no message to be received, but got one of type {result.GetType().Name}");
                }
                
                Thread.Sleep(10);

                if (DateTime.UtcNow >= waitUntil)
                    return;
            }
        }
    }
}
