using CloudMesh.Routing;
using System.Reflection;

namespace CloudMesh.Services.Internal
{
    public interface IServiceTransport
    {
        ValueTask<object?> InvokeAsync(ResourceIdentifier route, MethodInfo method, object?[] arguments);
    }
}
