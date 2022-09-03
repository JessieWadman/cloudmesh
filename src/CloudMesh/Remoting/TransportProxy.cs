using CloudMesh.Routing;
using CloudMesh.Utils;
using System.Reflection;

namespace CloudMesh.Remoting
{
    public abstract class TransportProxy<T> : DispatchProxy
    {
        protected abstract ValueTask<ResourceIdentifier?> TryResolveOne();
        protected abstract ValueTask<object?> InvokeAsync(ResourceIdentifier route, MethodInfo method, object?[]? arguments);

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var routeCall = TryResolveOne();
            if (routeCall.IsCompletedSuccessfully)
            {
                var route = routeCall.Result;
                if (route is null)
                    throw new NoRouteFoundException($"No route instance could be found for {method.DeclaringType!.Name}.{method.Name}");
                return Call(route);
            }

            var awaitAndCall = AwaitRouteAndThenCall();
            if (method.IsAsync())
                return AwaitRouteAndThenCall().ConvertToTaskType(method.ReturnType);
            else
                return awaitAndCall.GetAwaiter().GetResult();

            async ValueTask<object?> AwaitRouteAndThenCall()
            {
                var route = await routeCall;
                if (route is null)
                    throw new NoRouteFoundException($"No route instance could be found for {method.DeclaringType!.Name}.{method.Name}");
                return await InvokeAsync(route, method, arguments ?? Array.Empty<object?>());
            }

            object? Call(ResourceIdentifier route)
            {
                var call = InvokeAsync(route, method, arguments ?? Array.Empty<object?>());

                if (!method.IsAsync())
                {
                    return call.GetAwaiter().GetResult();
                }

                return call.ConvertToTaskType(method.ReturnType);
            }
        }
    }
}
