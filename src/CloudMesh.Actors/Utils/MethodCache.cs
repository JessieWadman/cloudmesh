using System.Collections.Concurrent;
using System.Reflection;

namespace CloudMesh.Actors.Utils
{
    public static class MethodCache
    {
        private static readonly ConcurrentDictionary<string, Type[]> parameterTypeCache = new();
        private static readonly ConcurrentDictionary<string, MethodInfo> methodCache = new();

        public static MethodInfo GetMethod(Type type, string methodName)
        {
            var cacheKey = $"{type.FullName}/{methodName}";

            return methodCache.GetOrAdd(cacheKey, _
                => type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException($"The method {methodName} does not exist on type {type.Name}!"));
        }

        public static Type[] GetMethodParameters(Type type, string methodName)
        {
            var cacheKey = $"{type.FullName}/{methodName}";
            return parameterTypeCache.GetOrAdd(cacheKey, _ =>
            {
                var method = GetMethod(type, methodName);
                var methodParameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
                return methodParameterTypes;
            });
        }
    }

    public class MethodCache<T>
    {
        private static readonly ConcurrentDictionary<string, MethodInfo> methodCache = new();
        private static readonly ConcurrentDictionary<string, Type[]> parameterTypeCache = new();

        public static MethodInfo GetMethod(string methodName)
        {
            return methodCache.GetOrAdd(methodName, _
                => typeof(T).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException($"The method {methodName} does not exist on type {typeof(T).Name}!"));
        }

        public static Type[] GetMethodParameters(string methodName)
        {
            return parameterTypeCache.GetOrAdd(methodName, _ =>
            {
                var method = GetMethod(methodName);
                var methodParameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
                return methodParameterTypes;
            });
        }
    }
}