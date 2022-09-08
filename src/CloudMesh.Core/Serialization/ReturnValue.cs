using CloudMesh.Utils;
using System.Collections.Concurrent;

namespace CloudMesh.Serialization
{
    public abstract class ReturnValue
    {
        private static readonly ConcurrentDictionary<Type, Type> specificTypes = new();

        public static Type Create(Type returnType)
        {
            return specificTypes.GetOrAdd(returnType, _ => typeof(ReturnValue<>).MakeGenericType(returnType));
        }

        public static ReturnValue Create(object? value)
        {
            if (value is null)
                return new ReturnValue<NoReturnType>() { ret = NoReturnType.Instance };
            var specificType = Create(value.GetType());
            var ret = (ReturnValue)Activator.CreateInstance(specificType)!;
            ret.SetValue(value);
            return ret;
        }

        public ExceptionContext? Exception { get; set; }

        public abstract object? GetValue();
        public abstract void SetValue(object? value);
    }

    public class ReturnValue<T> : ReturnValue
    {
        public T ret { get; set; }

        public override object? GetValue() => ret;
        public override void SetValue(object? value)
        {
            if (value is T t)
                ret = t;
            else
                ret = default;
        }
    }
}