using System.Collections.Concurrent;
using System.Reflection;

namespace CloudMesh.Actors.Client
{
    public static class TaskConverter
    {
        private static ConcurrentDictionary<Type, Func<Task<object?>, Task>> converters = new();
        private static MethodInfo method = typeof(TaskConverter).GetMethod(nameof(ConvertReturnType), BindingFlags.Static | BindingFlags.NonPublic)!;

        public static Task Convert(Task<object?> task, Type returnType)
        {
            if (typeof(void) == returnType || typeof(NoReturnType) == returnType)
                return DiscardReturnType(task);

            var converter = converters.GetOrAdd(returnType, CreateConverter);
            return converter(task);
        }

        private static Func<Task<object?>, Task> CreateConverter(Type returnType)
            => (Func<Task<object?>, Task>)method.MakeGenericMethod(returnType).Invoke(null, null);

        private static async Task DiscardReturnType(Task<object?> source)
        {
            await source;
        }

        private static Func<Task<object?>, Task> ConvertReturnType<T>()
        {
            return task => task.ContinueWith(t => (T)t.Result);
        }
    }
}
