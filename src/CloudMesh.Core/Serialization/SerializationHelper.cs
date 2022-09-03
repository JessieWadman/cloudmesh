using CloudMesh.Utils;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace CloudMesh.Serialization
{
    public class SerializerType
    {
        public SerializerType(Type type, PropertyInfo[] properties)
        {
            Type = type;
            Properties = properties;
        }

        public Type Type { get; init; }
        public PropertyInfo[] Properties { get; init; }
    }

    public class SerializationHelper
    {
        private static readonly ConcurrentDictionary<MethodInfo, int> methodsToLayoutIds = new();
        private static readonly ConcurrentDictionary<int, SerializerType> layoutToTypes = new();

        public static SerializerType GetSerializerTypeForLayout(MethodInfo method)
        {
            var parameters = method.GetParameters();
            // Do not serialize cancellationTokens
            parameters = parameters.Where(p => p.ParameterType != typeof(CancellationToken)).ToArray();

            var layoutId = methodsToLayoutIds.GetOrAdd(method, _ =>
                MurmurHash.StringHash(string.Join(",", parameters.Select(p => $"{p.Name}:{p.ParameterType}"))));

            return layoutToTypes.GetOrAdd(layoutId, _ => BuildNewSerializerType(layoutId, parameters!));
        }

        private static SerializerType BuildNewSerializerType(int layoutId, ParameterInfo[] parameters)
        {
            var serializerType = Emitter.ModuleBuilder.DefineType($"SerializedMessage_{layoutId}",
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);

            var props = new List<PropertyInfo>();
            foreach (var parameter in parameters)
                Emitter.DefineProperty(serializerType, parameter.Name!, parameter.ParameterType);

            var builtType = serializerType.CreateType()!;
            foreach (var parameter in parameters)
            {
                props.Add(builtType.GetProperty(parameter.Name, BindingFlags.Instance | BindingFlags.Public)!);
            }
            return new(builtType, props.ToArray());
        }

        public static object CreateObjFor(MethodInfo method, object?[] arguments, out CancellationToken? cancellationToken)
        {
            cancellationToken = null;

            var filteredArguments = arguments;
            var ct = arguments.OfType<CancellationToken>().ToArray();

            if (ct.Length != 0)
            {
                cancellationToken = ct[0];
                filteredArguments = arguments.Where(a => a is not CancellationToken).ToArray();
            }

            if (filteredArguments.Length == 0)
                return EmptyType.Instance;

            var serializationType = GetSerializerTypeForLayout(method);
            var obj = FormatterServices.GetUninitializedObject(serializationType.Type);

            for (var i = 0; i < serializationType.Properties.Length; i++)
                serializationType.Properties[i].SetValue(obj, filteredArguments[i]);
            return obj;
        }
    }

    public readonly struct EmptyType
    {
        public static readonly EmptyType Instance = new();
    }
}
