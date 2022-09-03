using System.Collections.Concurrent;
using System.Reflection;

namespace CloudMesh.Utils
{
    public readonly struct NoReturnType
    {
        public static readonly NoReturnType Instance = new NoReturnType();
        public static readonly Type Type = typeof(NoReturnType);
    }

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

        public static bool IsAsync(this MethodInfo method)
        {
            if (typeof(Task).IsAssignableFrom(method.ReturnType))
                return true;
            if (typeof(ValueTask).IsAssignableFrom(method.ReturnType))
                return true;
            return false;
        }

        private static readonly Type taskType = typeof(Task);

        public static bool IsVoidType(this MethodInfo? method)
        {
            if (method is null)
                return true;
            if (method.ReturnType == typeof(void) || method.ReturnType == typeof(NoReturnType) ||
                method.ReturnType == taskType)
                return true;
            return false;
        }

        public static bool TryGetTaskType(this MethodInfo? method, out Type? returnType)
        {
            returnType = null;
            if (method is null)
                return false;

            if (!taskType.IsAssignableFrom(method.ReturnType))
                return false;

            if (method.ReturnType.GenericTypeArguments.Length == 0)
            {
                returnType = typeof(void);
                return true;
            }

            returnType = method.ReturnType.GenericTypeArguments[0];
            return true;
        }

        public static Type GetMaybeTaskReturnType(this MethodInfo method, out bool isTask, out bool isVoidType)
        {
            if (method.TryGetTaskType(out var returnType) && returnType is not null)
            {
                isTask = true;
                isVoidType = returnType == typeof(void);
                return returnType;
            }
            else
            {
                isTask = false;
                isVoidType = returnType == typeof(void);
                return method.ReturnType;
            }
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
