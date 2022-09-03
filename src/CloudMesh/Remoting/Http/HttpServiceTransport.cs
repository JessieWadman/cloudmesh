using CloudMesh.Routing;
using CloudMesh.Services.Internal;
using System.Reflection;

namespace CloudMesh.Remoting.Http
{
    public class HttpServiceTransport : HttpTransport, IServiceTransport
    {
        public static readonly HttpServiceTransport Instance = new HttpServiceTransport();

        public ValueTask<object?> InvokeAsync(ResourceIdentifier route, MethodInfo method, object[] arguments)
        {
            return new(base.InvokeHttpAsync(route, $"/services/{method.DeclaringType!.Name}/{method.Name}", method, arguments));
        }
    }
}
