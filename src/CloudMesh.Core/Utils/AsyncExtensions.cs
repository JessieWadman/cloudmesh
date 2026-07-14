using System.Reflection;

namespace System
{
    /// <summary>
    /// Extension helpers for <see cref="IAsyncEnumerable{T}"/>, <see cref="Task"/>, and <see cref="ValueTask"/>:
    /// materializing async sequences, simple <c>Skip</c>/<c>Take</c>, and turning cancellation into a sentinel
    /// value instead of an exception.
    /// </summary>
    public static class AsyncExtensions
    {
        /// <summary>Enumerates the sequence to completion and returns its elements as a <see cref="List{T}"/>.</summary>
        public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> enumerable)
        {
            List<T> items = new();
            await foreach (var item in enumerable)
            {
                items.Add(item);
            }

            return items;
        }
        
        /// <summary>Enumerates the sequence to completion and returns its elements as an array.</summary>
        public static async Task<T[]> ToArrayAsync<T>(this IAsyncEnumerable<T> enumerable)
            => (await ToListAsync(enumerable)).ToArray();

        /// <summary>Bypasses the first <paramref name="count"/> elements of an async sequence and streams the rest.</summary>
        /// <param name="source">The sequence to skip over.</param>
        /// <param name="count">The number of leading elements to skip.</param>
        public static async IAsyncEnumerable<T> Skip<T>(this IAsyncEnumerable<T> source, int count)
        {
            var itemsSkipped = 0;
            var taking = false;
            
            // Warning! Dragons ahead!
            
            // We only count up to count items, and then set a flag to start taking.
            
            // The more readable approach would be to just check
            //      if (++itemsSkipped > count) yield return item;
            
            // At first glance that seems simpler, but(!) then we would have to be careful about
            // overflow, as we could be counting past int.MaxValue and accidentally start skipping again.
            // If source for example contains int.MaxValue + 5 items, and we call this with Skip(1), we would
            // skip the 1s item, and then start taking, and again skip int.MaxValue + 1 because counter got 
            // overflowed.
            
            await foreach (var item in source)
            {
                if (taking)
                    yield return item;

                if (++itemsSkipped <= count) 
                    continue;
                
                taking = true;
                yield return item;
            }
        }

        /// <summary>Streams at most the first <paramref name="count"/> elements of an async sequence, then stops.</summary>
        /// <param name="source">The sequence to take from.</param>
        /// <param name="count">The maximum number of elements to yield.</param>
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

        /// <summary>
        /// Awaits the task, returning <see langword="false"/> if it is cancelled instead of throwing
        /// <see cref="OperationCanceledException"/>, and <see langword="true"/> if it completes.
        /// </summary>
        public static ValueTask<bool> ReturnFalseOnCancellation(this ValueTask valueTask)
        {
            if (valueTask.IsCanceled)
                return new(false);

            if (valueTask.IsCompletedSuccessfully)
                return new(true);

            return new(Wait());

            async Task<bool> Wait()
            {
                try
                {
                    await valueTask;
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Awaits the task, returning <paramref name="cancellationValue"/> if it is cancelled instead of throwing
        /// <see cref="OperationCanceledException"/>; otherwise returns the task's result.
        /// </summary>
        /// <param name="valueTask">The task to await.</param>
        /// <param name="cancellationValue">The value to return if the task is cancelled.</param>
        public static ValueTask<T> ReturnOnCancellation<T>(this ValueTask<T> valueTask, T cancellationValue)
        {
            if (valueTask.IsCanceled)
                return new(cancellationValue);
            if (valueTask.IsCompletedSuccessfully)
                return new(valueTask.Result);

            return new(Wait());

            async Task<T> Wait()
            {
                try
                {
                    return await valueTask;
                }
                catch (OperationCanceledException)
                {
                    return cancellationValue;
                }
            }
        }

        /// <summary>
        /// Awaits the task, returning <see langword="false"/> if it is cancelled instead of throwing
        /// <see cref="OperationCanceledException"/>, and <see langword="true"/> if it completes.
        /// </summary>
        public static async ValueTask<bool> ReturnFalseOnCancellation(this Task task)
        {
            if (task.IsCanceled)
                return false;
            if (task.IsCompletedSuccessfully)
                return true;

            try
            {
                await task;
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// Awaits the task, returning <paramref name="cancellationValue"/> if it is cancelled instead of throwing
        /// <see cref="OperationCanceledException"/>; otherwise returns the task's result.
        /// </summary>
        /// <param name="task">The task to await.</param>
        /// <param name="cancellationValue">The value to return if the task is cancelled.</param>
        public static async ValueTask<T> ReturnOnCancellation<T>(this Task<T> task, T cancellationValue)
        {
            if (task.IsCanceled)
                return cancellationValue;
            if (task.IsCompletedSuccessfully)
                return task.Result;

            try
            {
                return await task;
            }
            catch (OperationCanceledException)
            {
                return cancellationValue;
            }
        }

        /// <summary>
        /// Infrastructure helper that adapts a <see cref="ValueTask{TResult}"/> of <see cref="object"/> to the
        /// requested awaitable type — <see cref="Task"/>, <see cref="ValueTask"/>, or their generic forms — for
        /// dispatch scenarios where the concrete result type is only known at runtime.
        /// </summary>
        /// <param name="valueTask">The source task carrying a boxed result.</param>
        /// <param name="taskType">The awaitable type to produce.</param>
        /// <returns>An awaitable of <paramref name="taskType"/> that yields the (converted) result.</returns>
        public static object ConvertToTaskType(this ValueTask<object?> valueTask, Type taskType)
        {
            if (taskType == typeof(ValueTask))
                return DropResultAsValueTask();

            if (taskType == typeof(Task))
                return DropResultAsTask();

            var returnType = taskType.GenericTypeArguments[0];
            var convertedValueTask = ConvertValueTaskMethod.MakeGenericMethod(returnType).Invoke(null, new object[] { valueTask })!;

            if (typeof(ValueTask).IsAssignableFrom(taskType))
                return convertedValueTask;

            return ((dynamic)convertedValueTask).AsTask();

            async ValueTask DropResultAsValueTask()
            {
                await valueTask;
            }

            async Task DropResultAsTask()
            {
                await valueTask;
            }
        }

        private static MethodInfo ConvertValueTaskMethod = typeof(AsyncExtensions).GetMethod(nameof(ConvertValueTask),
            BindingFlags.Static | BindingFlags.NonPublic)!;

        private static async ValueTask<T?> ConvertValueTask<T>(ValueTask<object?> task)
        {
            var result = await task;
            if (result == null)
                return default;
            return (T)result;
        }

        /// <summary>
        /// Infrastructure helper that normalizes a value that may be a <see cref="Task"/>, a <see cref="ValueTask"/>,
        /// their generic forms, or a plain value into a single <see cref="Task{TResult}"/> of <see cref="object"/>.
        /// </summary>
        /// <param name="value">A task, value-task, or plain value.</param>
        /// <returns>A task yielding the (possibly awaited) result as a boxed object.</returns>
        public static Task<object?> ToObjectTask(object? value)
        {
            if (value is null)
                return Task.FromResult((object?)null);

            var valueType = value.GetType();

            if (typeof(Task).IsAssignableFrom(valueType))
            {
                return ((Task)value).ContinueWith(t => (object?)((dynamic)t).Result);
            }

            if (typeof(ValueTask).IsAssignableFrom(valueType))
            {
                var task = (Task)((dynamic)value).AsTask();
                return task.ContinueWith(t => (object?)((dynamic)t).Result);
            }

            return Task.FromResult((object?)value);
        }
    }
}