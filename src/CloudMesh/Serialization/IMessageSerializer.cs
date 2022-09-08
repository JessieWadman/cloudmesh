using System.Reflection;

namespace CloudMesh.Serialization
{
    public interface IMessageSerializer
    {
        Task SerializeAsync(Stream utf8Stream, MethodInfo method, object?[] arguments, out CancellationToken? cancellationToken);
        ReadOnlySpan<byte> Serialize(MethodInfo method, object?[] arguments);
        ValueTask<object?[]?> DeserializeAsync(Stream utf8Stream, MethodInfo method);
        object?[]? Deserialize(ReadOnlySpan<byte> buffer, MethodInfo method);
    }
}
