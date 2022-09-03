using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace CloudMesh.DataBlocks
{
    internal static class AsyncExtensions
    {
        public static async IAsyncEnumerable<TSource> ReadAllAsync<TSource>(
            this ChannelReader<TSource> source,
            TimeSpan? timeout,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (true)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (timeout.HasValue)
                    cts.CancelAfter(timeout.Value);

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

                    if (timeout.HasValue)
                        cts.CancelAfter(timeout.Value);

                    // It is possible that the CTS timed-out during the yielding
                    if (cts.IsCancellationRequested)
                        break; // Start a new loop with a new CTS
                }
            }
        }
    }
}
