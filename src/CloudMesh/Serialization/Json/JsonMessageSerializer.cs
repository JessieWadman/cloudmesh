using System.Reflection;
using System.Text.Json;

namespace CloudMesh.Serialization.Json
{
    public class JsonMessageSerializer : IMessageSerializer
    {
        private static readonly JsonSerializerOptions opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
#if (DEBUG)
            WriteIndented = true
#else
            WriteIndented = false
#endif
        };

        public Task SerializeAsync(Stream utf8Stream, MethodInfo method, object?[] arguments, out CancellationToken? cancellationToken)
        {
            var obj = SerializationHelper.CreateObjFor(method, arguments, out cancellationToken);
            return JsonSerializer.SerializeAsync(utf8Stream, obj, options: opts, cancellationToken: cancellationToken ?? default);
        }

        public ReadOnlySpan<byte> Serialize(MethodInfo method, object?[] arguments)
        {
            var obj = SerializationHelper.CreateObjFor(method, arguments, out _);
            return JsonSerializer.SerializeToUtf8Bytes(obj, opts);
        }

        public async ValueTask<object?[]?> DeserializeAsync(Stream utf8Stream, MethodInfo method)
        {
            var serializerType = SerializationHelper.GetSerializerTypeForLayout(method);
            var obj = await JsonSerializer.DeserializeAsync(utf8Stream, serializerType.Type, opts);
            if (obj is null)
                return null;

            var result = new object?[serializerType.Properties.Length];
            for (var i = 0; i < serializerType.Properties.Length; i++)
                result[i] = serializerType.Properties[i].GetValue(obj);
            return result;
        }

        public object?[]? Deserialize(ReadOnlySpan<byte> buffer, MethodInfo method)
        {
            var serializerType = SerializationHelper.GetSerializerTypeForLayout(method);
            var obj = JsonSerializer.Deserialize(buffer, serializerType.Type, opts);
            if (obj is null)
                return null;

            var result = new object?[serializerType.Properties.Length];
            for (var i = 0; i < serializerType.Properties.Length; i++)
                result[i] = serializerType.Properties[i].GetValue(obj);
            return result;
        }
    }
}
