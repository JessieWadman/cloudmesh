using System.Collections.Concurrent;

namespace CloudMesh.Actors.Client.Http
{
    internal abstract class ReturnValue
    {
        private static readonly ConcurrentDictionary<Type, Type> specificTypes = new();

        public static Type Create(Type returnType)
        {
            return specificTypes.GetOrAdd(returnType, _ => typeof(ReturnValue<>).MakeGenericType(returnType));
        }        

        public abstract object? GetValue();
    }

    internal class ReturnValue<T> : ReturnValue
    {

        public T ret { get; set; }

        public override object? GetValue() => ret;
    }
}
