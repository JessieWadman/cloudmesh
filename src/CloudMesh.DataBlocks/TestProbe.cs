namespace CloudMesh.DataBlocks;

public class TestProbe : CaptureBlock
{
    private static long counter;
    
    public TestProbe()
    {
        Name = "TestProbe-" + Interlocked.Increment(ref counter);
        Path = Name;
    }

    public void ExpectNoMessage(int timeToWaitInMilliseconds = 5000)
    {
        var waitUntil = DateTime.UtcNow.AddMilliseconds(timeToWaitInMilliseconds);
        while (true)
        {
            lock (Locker)
            {
                if (Messages.TryDequeue(out var result))
                {
                    throw new InvalidOperationException(
                        $"Expected no message to be received, but got one of type {result.GetType().Name}");
                }
            }

            Thread.Sleep(10);

            if (DateTime.UtcNow >= waitUntil)
                return;
        }
    }
    
    public T Expect<T>(int timeToWaitInMilliseconds = 10000, Func<T, bool>? predicate = null)
    {
        var waitUntil = DateTime.UtcNow.AddMilliseconds(timeToWaitInMilliseconds); 
        while (true) 
        {
            lock (Locker)
            {
                if (Messages.TryDequeue(out var result)) 
                {
                    if (result is T value)
                    {
                        if (predicate == null || predicate(value))
                            return value;
                        throw new InvalidOperationException($"Expect message of type {typeof(T).Name} and got one, but the predicate failed.");
                    }

                    throw new InvalidOperationException($"Expect message of type {typeof(T).Name} but got {result.GetType().Name}"); 
                }
            }
                
            if (DateTime.UtcNow > waitUntil) 
                throw new TimeoutException("No message received within the expected time.");
                
            Thread.Sleep(10);
        }  
    }
}