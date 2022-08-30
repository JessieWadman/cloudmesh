using CloudMesh.Utils;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Serialization;

namespace CloudMesh.Actors.Serialization
{
    internal class SerializerType
    {
        public SerializerType(Type type)
        {
            Type = type;
            Properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        }

        public Type Type { get; init; }
        public PropertyInfo[] Properties { get; init; }
    }

    internal class SerializationHelper
    {
        private static readonly ConcurrentDictionary<ulong, SerializerType> layoutToTypes = new();

        public static SerializerType GetSerializerTypeForLayout(Type[] types)
        {
            var propertyTypeNames = string.Join(",", types.Select(t => t.Name));
            var layoutId = MurmurHash2.HashString(propertyTypeNames);
            return layoutToTypes.GetOrAdd(layoutId, _ => new SerializerType(BuildNewSerializerType(layoutId, types)));
        }

        private static Type BuildNewSerializerType(ulong layoutId, Type[] types)
        {
            var serializerType = Emitter.ModuleBuilder.DefineType($"SerializedMessage_{layoutId}", 
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);

            for (var i = 0; i < types.Length; i++)
                Emitter.DefineProperty(serializerType, $"arg{i}", types[i]);

            return serializerType.CreateType()!;
        }

        public static object CreateObjFor(Type[] argTypes, object?[] arguments)
        {
            if (argTypes.Length == 0)
                return EmptyType.Instance;

            var serializationType = GetSerializerTypeForLayout(argTypes);
            var obj = FormatterServices.GetUninitializedObject(serializationType.Type);
            for (var i = 0; i < serializationType.Properties.Length; i++)
                serializationType.Properties[i].SetValue(obj, arguments[i]);
            return obj;
        }

        private readonly struct EmptyType 
        { 
            public static readonly EmptyType Instance = new EmptyType();
        }
    }
}
