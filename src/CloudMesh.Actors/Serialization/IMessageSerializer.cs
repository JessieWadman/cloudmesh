namespace CloudMesh.Actors.Serialization
{
    public interface IMessageSerializer
    {
        void SerializeAsync(Stream utf8Stream, string actorName, string methodName, Type[] argTypes, object[] arguments);
        ReadOnlySpan<byte> Serialize(string actorName, string methodName, Type[] argTypes, object[] arguments);
        ValueTask<object?[]?> DeserializeAsync(Stream utf8Stream, string actorName, string methodName, Type[] argTypes);
        object?[]? Deserialize(ReadOnlySpan<byte> buffer, string actorName, string methodName, Type[] argTypes);
    }
}
