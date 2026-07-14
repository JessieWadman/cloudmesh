namespace CloudMesh.DataBlocks;

/// <summary>
/// A <see cref="CaptureBlock"/> with blocking assertion helpers for tests, in the spirit of Akka's TestProbe.
/// Use it as a message target, then assert on what arrived with <see cref="Expect{T}(int, Func{T, bool})"/> and
/// <see cref="ExpectNoMessage(int)"/>.
/// </summary>
// PERF: This is primarily used for testing, so boxing is fine.
public class TestProbe : CaptureBlock
{
    private static long counter;

    /// <summary>Creates a probe with a unique auto-generated name.</summary>
    public TestProbe()
    {
        Name = "TestProbe-" + Interlocked.Increment(ref counter);
        Path = Name;
    }

    /// <summary>
    /// Asserts that no message arrives within the given window, throwing if one does.
    /// </summary>
    /// <param name="timeToWaitInMilliseconds">How long to watch for an unexpected message.</param>
    /// <exception cref="InvalidOperationException">A message was received.</exception>
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
                        $"Expected no message to be received, but got one of type {(result == null ? "null" : result.GetType().Name)}");
                }
            }

            Thread.Sleep(10);

            if (DateTime.UtcNow >= waitUntil)
                return;
        }
    }
    
    /// <summary>
    /// Waits for and returns the next message, asserting it is of type <typeparamref name="T"/> (and optionally
    /// satisfies <paramref name="predicate"/>).
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <param name="timeToWaitInMilliseconds">Maximum time to wait for a message.</param>
    /// <param name="predicate">An optional predicate the message must satisfy.</param>
    /// <returns>The received message.</returns>
    /// <exception cref="TimeoutException">No message arrived within the window.</exception>
    /// <exception cref="InvalidOperationException">The message was of the wrong type or failed the predicate.</exception>
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

                    throw new InvalidOperationException($"Expect message of type {typeof(T).Name} but got {result?.GetType().Name ?? "null"}"); 
                }
            }
                
            if (DateTime.UtcNow > waitUntil) 
                throw new TimeoutException("No message received within the expected time.");
                
            Thread.Sleep(10);
        }  
    }
}