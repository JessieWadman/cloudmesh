using CloudMesh.Routing;
using System.Reflection;

namespace CloudMesh.Actors.Internal
{
    public interface IActorTransport
    {
        ValueTask<object?> InvokeAsync(ResourceIdentifier route, string id, MethodInfo method, object?[] arguments);
    }
}
