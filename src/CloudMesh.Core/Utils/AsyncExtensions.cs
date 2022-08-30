using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace System
{
    public static class AsyncExtensions
    {
        public static async Task<T[]> ToArrayAsync<T>(this IAsyncEnumerable<T> enumerable)
        {
            List<T> items = new();
            await foreach (var item in enumerable)
            {
                items.Add(item);
            }
            return items.ToArray();
        }

        public static async IAsyncEnumerable<T> Take<T>(this IAsyncEnumerable<T> source, int count)
        {
            var itemsReturned = 0;
            await foreach (var item in source)
            {
                yield return item;
                if (++itemsReturned >= count)
                    yield break;
            }
        }

        public static async IAsyncEnumerable<TSource> ReadAllAsync<TSource>(
            this ChannelReader<TSource> source,
            TimeSpan timeout,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (true)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);
                while (true)
                {
                    try
                    {
                        if (!await source.WaitToReadAsync(cts.Token).ConfigureAwait(false))
                            yield break;
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException();
                    }
                    while (source.TryRead(out var item))
                    {
                        yield return item;
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    cts.CancelAfter(timeout);
                    // It is possible that the CTS timed-out during the yielding
                    if (cts.IsCancellationRequested)
                        break; // Start a new loop with a new CTS
                }
            }
        }
    }
}
