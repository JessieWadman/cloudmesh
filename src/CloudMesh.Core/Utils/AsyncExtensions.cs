using System.Reflection;

namespace System
{
    public static class AsyncExtensions
    {
        public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> enumerable)
        {
            List<T> items = new();
            await foreach (var item in enumerable)
            {
                items.Add(item);
            }

            return items;
        }
        
        public static async Task<T[]> ToArrayAsync<T>(this IAsyncEnumerable<T> enumerable)
            => (await ToListAsync(enumerable)).ToArray();

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
            else
                return Task.FromResult((object?)value);
        }
    }
}