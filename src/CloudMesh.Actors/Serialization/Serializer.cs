using CloudMesh.Serialization.Json;

namespace CloudMesh.Serialization
{
    public abstract class Serializer
    {
        public static IMessageSerializer Instance = new JsonMessageSerializer();
    }
}
