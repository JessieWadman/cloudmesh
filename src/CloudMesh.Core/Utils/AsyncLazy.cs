using System.Runtime.CompilerServices;

namespace CloudMesh.Utils
{
    /// <summary>
    /// Provides support for asynchronous lazy initialization. The factory runs at most once, on first use, and the
    /// resulting value is cached; subsequent awaits return the same completed task. This type is fully thread safe.
    /// </summary>
    /// <typeparam name="T">The type of object that is being asynchronously initialized.</typeparam>
    /// <remarks>
    /// An <see cref="AsyncLazy{T}"/> is awaited directly — it exposes a <see cref="GetAwaiter"/> that yields the
    /// initialized value. Use it for expensive, idempotent initialization (loading config, opening a connection)
    /// that you want to defer until first needed and share thereafter.
    /// </remarks>
    /// <example>
    /// <code>
    /// private readonly AsyncLazy&lt;HttpClient&gt; _client = new(async () =&gt;
    /// {
    ///     var c = new HttpClient();
    ///     await WarmUpAsync(c);
    ///     return c;
    /// });
    ///
    /// public async Task CallAsync() =&gt; await (await _client).GetAsync("/health");
    /// </code>
    /// </example>
    public sealed class AsyncLazy<T>
    {
        /// <summary>
        /// The underlying lazy task.
        /// </summary>
        private readonly Lazy<Task<T>> instance;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncLazy{T}"/> class.
        /// </summary>
        /// <param name="factory">The delegate that is invoked on a background thread to produce the value when it is needed.</param>
        public AsyncLazy(Func<T> factory)
        {
            instance = new Lazy<Task<T>>(() => Task.Run(factory), false);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncLazy{T}"/> class.
        /// </summary>
        /// <param name="factory">The asynchronous delegate that is invoked on a background thread to produce the value when it is needed.</param>
        public AsyncLazy(Func<Task<T>> factory)
        {
            instance = new Lazy<Task<T>>(() => Task.Run(factory), false);
        }

        /// <summary>
        /// Asynchronous infrastructure support. This method permits instances of <see cref="AsyncLazy{T}"/> to be await'ed.
        /// Awaiting triggers initialization if it has not started, then yields the initialized value.
        /// </summary>
        /// <returns>An awaiter for the (possibly still running) initialization task.</returns>
        public TaskAwaiter<T> GetAwaiter()
        {
            return instance.Value.GetAwaiter();
        }

        /// <summary>
        /// Starts the asynchronous initialization, if it has not already started.
        /// </summary>
        public void Start()
        {
            _ = instance.Value;
        }
    }
}
