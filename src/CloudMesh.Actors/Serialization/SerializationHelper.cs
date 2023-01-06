using CloudMesh.Utils;
using System.Collections.Concurrent;
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
        private static readonly ConcurrentDictionary<int, SerializerType> layoutToTypes = new();
        private static readonly ConcurrentDictionary<MethodInfo, int> methodCache = new();

        public static SerializerType GetSerializerTypeForLayout(MethodInfo method)
        {
            var parameters = method.GetParameters();
            // Do not serialize cancellationTokens
            parameters = parameters.Where(p => p.ParameterType != typeof(CancellationToken)).ToArray();

            var layoutId = methodCache.GetOrAdd(method, m =>
            {
                var methodPath = $"{method.DeclaringType!.Assembly.GetName().Name}.{method.DeclaringType.FullName}.{method.Name}";
                var methodExpresion = $"{methodPath}({string.Join(',', parameters.Select(p => p.ParameterType.Name))})";
                var layoutId = MurmurHash.StringHash(methodExpresion);
                return layoutId;
            });

            return layoutToTypes.GetOrAdd(layoutId, l => BuildNewSerializerType(l, parameters!));
        }

        private static readonly ConcurrentDictionary<string, Type> generatedTypes = new();

        private static SerializerType BuildNewSerializerType(int layoutId, ParameterInfo[] parameters)
        {
            var typeName = $"SerializedMessage_{layoutId:x8}";
            if (generatedTypes.TryGetValue(typeName, out var existingType))
            {
                throw new ArgumentException("Layout ID already exists!");
            }

            var serializerType = Emitter.ModuleBuilder.DefineType(typeName,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);

            var props = new List<PropertyInfo>();
            foreach (var parameter in parameters)
                Emitter.DefineProperty(serializerType, parameter.Name!, parameter.ParameterType);

            var builtType = serializerType.CreateType()!;
            foreach (var parameter in parameters)
            {
                props.Add(builtType.GetProperty(parameter.Name, BindingFlags.Instance | BindingFlags.Public)!);
            }

            generatedTypes[typeName] = builtType;
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

        public static object?[] GetParameterArray(
            MethodInfo method, 
            SerializerType serializerType, 
            object source, 
            CancellationToken cancellationTokenToInject)
        {
            var methodParameters = method.GetParameters();
            var result = new object?[methodParameters.Length];

            var propertyIdx = 0;
            for (var i = 0; i < methodParameters.Length; i++)
            {
                if (methodParameters[i].ParameterType == typeof(CancellationToken))
                    result[i] = cancellationTokenToInject;
                else
                    result[i] = serializerType.Properties[propertyIdx++].GetGetMethod()?.Invoke(source, null);
            }
            return result;
        }
    }

    public readonly struct EmptyType
    {
        public static readonly EmptyType Instance = new();
    }
}
