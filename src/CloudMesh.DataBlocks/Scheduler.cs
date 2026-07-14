namespace CloudMesh.DataBlocks
{
    /// <summary>A handle to a scheduled delivery that can be cancelled before it fires (or stops a repeating schedule).</summary>
    public interface ICancelable
    {
        /// <summary>Cancels the scheduled delivery. Safe to call after it has already fired.</summary>
        void Cancel();
    }

    /// <summary>
    /// Schedules delayed and repeating one-way message deliveries to a block, the DataBlocks analogue of an
    /// actor scheduler. Handy for timeouts, retries, and periodic ticks (e.g. flush signals).
    /// </summary>
    public static class DataBlockScheduler
    {
        /// <summary>Delivers a message to <paramref name="target"/> once, after <paramref name="delay"/> (fire-and-forget).</summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="target">The block to deliver to.</param>
        /// <param name="delay">Delay before delivery; <see cref="TimeSpan.Zero"/> delivers as soon as possible.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender to attach, or <see langword="null"/>.</param>
        public static void ScheduleTellOnce<T>(ICanSubmit target, TimeSpan delay, T message, IDataBlockRef? sender)
        {
            _ = Task.Run(async () =>
            {
                if (delay != TimeSpan.Zero)
                    await Task.Delay(delay);
                await target.SubmitAsync(message, sender);
            }).ConfigureAwait(false);
        }
        
        /// <summary>Delivers a message once after a delay given in milliseconds (fire-and-forget).</summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="target">The block to deliver to.</param>
        /// <param name="delayInMilliseconds">Delay in milliseconds before delivery.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender to attach, or <see langword="null"/>.</param>
        public static void ScheduleTellOnce<T>(ICanSubmit target, int delayInMilliseconds, T message, IDataBlockRef? sender)
            => ScheduleTellOnce(target, TimeSpan.FromMilliseconds(delayInMilliseconds), message, sender);

        /// <summary>Delivers a message once after <paramref name="delay"/>, returning a handle to cancel it beforehand.</summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="target">The block to deliver to.</param>
        /// <param name="delay">Delay before delivery.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender to attach, or <see langword="null"/>.</param>
        /// <returns>An <see cref="ICancelable"/> that cancels the pending delivery.</returns>
        public static ICancelable ScheduleTellOnceCancelable<T>(ICanSubmit target, TimeSpan delay, T message, IDataBlockRef? sender)
        {
            var cancellation = new Cancelable();
            var stoppingToken = cancellation.Token;

            _ = Task.Run(async () =>
            {
                using (cancellation)
                {
                    try
                    {
                        await Task.Delay(delay, stoppingToken);
                        if (!stoppingToken.IsCancellationRequested)
                            await target.SubmitAsync(message, sender);
                    }
                    catch (TaskCanceledException)
                    {
                    }
                }
            }).ConfigureAwait(false);

            return cancellation;
        }

        /// <summary>Delivers a message once after a delay given in milliseconds, returning a cancellation handle.</summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="target">The block to deliver to.</param>
        /// <param name="delayInMilliseconds">Delay in milliseconds before delivery.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender to attach, or <see langword="null"/>.</param>
        /// <returns>An <see cref="ICancelable"/> that cancels the pending delivery.</returns>
        public static ICancelable ScheduleTellOnceCancelable<T>(ICanSubmit target, int delayInMilliseconds, T message,
            IDataBlockRef? sender)
            => ScheduleTellOnceCancelable(target, TimeSpan.FromMilliseconds(delayInMilliseconds), message, sender);

        /// <summary>
        /// Repeatedly delivers a message to <paramref name="target"/> every <paramref name="frequency"/> until the
        /// returned handle is cancelled.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="target">The block to deliver to.</param>
        /// <param name="frequency">Interval between deliveries.</param>
        /// <param name="message">The message to send each interval.</param>
        /// <param name="sender">The sender to attach, or <see langword="null"/>.</param>
        /// <returns>An <see cref="ICancelable"/> that stops the repeating schedule.</returns>
        public static ICancelable ScheduleTellRepeatedly<T>(ICanSubmit target, TimeSpan frequency, T message, IDataBlockRef? sender)
        {
            var cancellation = new Cancelable();
            var stoppingToken = cancellation.Token;

            _ = Task.Run(async () =>
            {
                using (cancellation)
                {
                    try
                    {
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            await Task.Delay(frequency, stoppingToken);
                            if (!stoppingToken.IsCancellationRequested)
                                await target.SubmitAsync(message, sender);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                    }
                }
            }).ConfigureAwait(false);

            return cancellation;
        }
        
        /// <summary>Repeatedly delivers a message at an interval given in milliseconds until cancelled.</summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="target">The block to deliver to.</param>
        /// <param name="frequencyInMilliseconds">Interval between deliveries, in milliseconds.</param>
        /// <param name="message">The message to send each interval.</param>
        /// <param name="sender">The sender to attach, or <see langword="null"/>.</param>
        /// <returns>An <see cref="ICancelable"/> that stops the repeating schedule.</returns>
        public static ICancelable ScheduleTellRepeatedly<T>(ICanSubmit target, int frequencyInMilliseconds, T message, IDataBlockRef? sender)
            => ScheduleTellRepeatedly(target, TimeSpan.FromMilliseconds(frequencyInMilliseconds), message, sender);

        /// <summary>The default <see cref="ICancelable"/> implementation, backed by a <see cref="CancellationTokenSource"/>.</summary>
        public class Cancelable : ICancelable, IDisposable
        {
            private CancellationTokenSource? cancellationTokenSource = new();

            /// <summary>The token that is cancelled when <see cref="Cancel"/> is called, or none once disposed.</summary>
            public CancellationToken Token
            {
                get
                {
                    lock(this)
                    {
                        return cancellationTokenSource?.Token ?? CancellationToken.None;
                    }
                }
            }

            /// <inheritdoc/>
            public void Cancel()
            {
                lock (this)
                {
                    cancellationTokenSource?.Cancel();
                }
            }

            /// <summary>Releases the underlying <see cref="CancellationTokenSource"/>.</summary>
            public void Dispose()
            {
                lock (this)
                {
                    cancellationTokenSource?.Dispose();
                    cancellationTokenSource = null;
                }
                GC.SuppressFinalize(this);
            }
        }
    }
}
