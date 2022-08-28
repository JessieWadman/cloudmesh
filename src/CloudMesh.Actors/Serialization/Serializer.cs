namespace CloudMesh.Actors.Serialization
{
    public abstract class Serializer
    {
        public static IMessageSerializer Instance = new JsonMessageSerializer();
    }
}
