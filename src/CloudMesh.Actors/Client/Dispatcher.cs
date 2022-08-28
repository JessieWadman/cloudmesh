using CloudMesh.Actors.Utils;
using System.Collections.Concurrent;

namespace CloudMesh.Actors.Client
{
    internal abstract class Dispatcher
    {
        private static readonly ConcurrentDictionary<Type, Type> actorTypesToDispatcherTypes = new();

        public static Dispatcher Create(Type actorType, IActor targetInstance)
        {
            var dispatcherType = actorTypesToDispatcherTypes.GetOrAdd(actorType, _ => GetDispatcherTypeFor(actorType));
            return (Dispatcher)Activator.CreateInstance(dispatcherType, new object[] { targetInstance })!;
        }

        private static Type GetDispatcherTypeFor(Type actorType)
        {
            return typeof(Dispatcher<>).MakeGenericType(actorType);
        }

        public abstract Type[] GetMethodParameters(string methodName);

        public abstract object? Invoke(string methodName, object[] arguments, out Type returnType);
    }

    internal class Dispatcher<T> : Dispatcher
    {
        private readonly T actor;

        public Dispatcher(T actor)
        {
            this.actor = actor;
        }

        public override object? Invoke(string methodName, object[] arguments, out Type returnType)
        {
            var method = MethodCache<T>.GetMethod(methodName);
            returnType = method.ReturnType;
            var retValue = method.Invoke(actor, arguments);
            return retValue;
        }

        public override Type[] GetMethodParameters(string methodName)
            => MethodCache<T>.GetMethodParameters(methodName);

        public override string ToString()
        {
            return $"Dispatcher<{typeof(T).Name}>";
        }
    }
}