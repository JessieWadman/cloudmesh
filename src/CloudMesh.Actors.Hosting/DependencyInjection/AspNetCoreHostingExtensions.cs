using CloudMesh.Actors.Hosting;
using CloudMesh.Actors.Hosting.Handler;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Net;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class AspNetCoreHostingExtensions
    {
        public static IEndpointConventionBuilder MapActors(this IEndpointRouteBuilder endpoints)
        {
            endpoints.ServiceProvider.GetRequiredService<IActorHost>();

            return endpoints.MapPut("/actors/{type}/{id}/{methodName}", async (HttpContext context, string type, string id, string methodName) => {
                var response = await AspNetCoreActorHandler.HandleAsync(context, type, id, methodName);
                if (response is int code)
                    return Results.StatusCode(code);
                if (response is HttpStatusCode statusCode)
                    return Results.StatusCode((int)statusCode);
                else
                    return Results.Ok(response);
            });
        }
    }
}
