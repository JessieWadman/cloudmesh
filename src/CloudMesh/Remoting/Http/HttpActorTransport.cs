using CloudMesh.Actors.Internal;
using CloudMesh.Routing;
using System.Reflection;

namespace CloudMesh.Remoting.Http
{
    public class HttpActorTransport : HttpTransport, IActorTransport
    {
        public static readonly HttpActorTransport Instance = new();

        public ValueTask<object?> InvokeAsync(ResourceIdentifier route, string id, MethodInfo method, object[] arguments)
        {
            return new(base.InvokeHttpAsync(route, $"/actors/{method.DeclaringType!.Name}/{id}/{method.Name}", method, arguments));
        }
    }
}
