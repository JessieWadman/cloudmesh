using System.Collections.Concurrent;

namespace CloudMesh.Utils
{
    public abstract class Dispatcher
    {
        private static readonly ConcurrentDictionary<Type, Type> interfaceTypesToDispatcherTypes = new();

        public static Dispatcher Create(Type interfaceType, object targetInstance)
        {
            var dispatcherType = interfaceTypesToDispatcherTypes.GetOrAdd(interfaceType, _ => GetDispatcherTypeFor(interfaceType));
            return (Dispatcher)Activator.CreateInstance(dispatcherType, new object[] { targetInstance })!;
        }

        private static Type GetDispatcherTypeFor(Type actorType)
            => typeof(Dispatcher<>).MakeGenericType(actorType);

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
            /*var retValue = typeof(T).InvokeMember(methodName, BindingFlags.Public | BindingFlags.Instance, null, actor, arguments);
            returnType = retValue switch
            {
                null => typeof(void),
                _ => retValue.GetType()
            };*/

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
