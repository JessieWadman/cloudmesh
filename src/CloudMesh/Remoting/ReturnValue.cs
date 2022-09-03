using System.Collections.Concurrent;

namespace CloudMesh.Remoting
{
    internal abstract class ReturnValue
    {
        private static readonly ConcurrentDictionary<Type, Type> specificTypes = new();

        public static Type Create(Type returnType)
        {
            return specificTypes.GetOrAdd(returnType, _ => typeof(ReturnValue<>).MakeGenericType(returnType));
        }

        public ExceptionContext? Exception { get; set; }

        public abstract object? GetValue();
    }

    internal class ReturnValue<T> : ReturnValue
    {
        public T ret { get; set; }

        public override object? GetValue() => ret;
    }
}