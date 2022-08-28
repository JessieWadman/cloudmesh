using System.Text.Json;

namespace CloudMesh.Actors.Serialization
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

        public void SerializeAsync(Stream utf8Stream, string actorName, string methodName, Type[] argTypes, object[] arguments)
        {
            var obj = SerializationHelper.CreateObjFor(argTypes, arguments);
            JsonSerializer.SerializeAsync(utf8Stream, obj, opts);
        }

        public ReadOnlySpan<byte> Serialize(string actorName, string methodName, Type[] argTypes, object[] arguments)
        {
            var obj = SerializationHelper.CreateObjFor(argTypes, arguments);
            return JsonSerializer.SerializeToUtf8Bytes(obj, opts);
        }

        public async ValueTask<object?[]?> DeserializeAsync(Stream utf8Stream, string actorName, string methodName, Type[] argTypes)
        {
            var serializerType = SerializationHelper.GetSerializerTypeForLayout(argTypes);
            var obj = await JsonSerializer.DeserializeAsync(utf8Stream, serializerType.Type, opts);
            if (obj is null)
                return null;

            var result = new object?[serializerType.Properties.Length];
            for (var i = 0; i < serializerType.Properties.Length; i++)
                result[i] = serializerType.Properties[i].GetValue(obj);
            return result;
        }

        public object?[]? Deserialize(ReadOnlySpan<byte> buffer, string actorName, string methodName, Type[] argTypes)
        {
            var serializerType = SerializationHelper.GetSerializerTypeForLayout(argTypes);
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
