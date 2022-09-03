using CloudMesh.Actors.Hosting;
using CloudMesh.Hosting.AspNetCore.Helpers;
using CloudMesh.Remoting;
using CloudMesh.Routing;
using CloudMesh.Serialization;
using CloudMesh.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


namespace CloudMesh.Hosting.AspNetCore
{
    internal class AspNetCoreActorHandler
    {
        public static async Task<object?> HandleAsync(HttpContext context, string actorName, string id, string methodName, CancellationToken cancellationToken)
        {
            var actorHost = context.RequestServices.GetRequiredService<IActorHost>();
            var logger = context.RequestServices.GetRequiredService<ILogger<AspNetCoreActorHandler>>();

            if (!actorHost.TryGetHostedActor(actorName, id, out var hostedActor) || hostedActor is null)
            {
                logger.LogWarning($"NotFound: No registration for actor with name [{actorName}]");
                return Results.NotFound();
            }

            var actorType = hostedActor.GetType();
            var method = MethodCache.GetMethod(actorType, methodName);
            if (method is null)
            {
                logger.LogWarning($"BadRequest: Method {methodName} not found on {actorType.Name}");
                return Results.BadRequest();
            }

            var args = await ArgumentHelper.DeserializeArgumentsAsync(method, context.Request.Body, cancellationToken);
            if (args is null)
            {
                logger.LogWarning($"BadRequest: Failed to deserialize actor invocation payload.");
                return Results.BadRequest();
            }

            logger.LogDebug($"Invoking {actorName}/{id}/{methodName}");
            _ = MethodCache.GetMaybeTaskReturnType(method, out _, out var returnsVoid);

            var sender = new ResourceIdentifier("http", context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "0.0.0.0");

            try
            {
                var retValue = await hostedActor.InvokeAsync(methodName, args, sender, !returnsVoid, default);
                if (retValue == null || retValue is NoReturnType)
                {
                    return Results.NoContent();
                }
                return Results.Ok(new { ret = retValue });
            }
            catch (Exception error)
            {
                var exceptionContext = ExceptionContext.Create(error);
                return Results.Ok(new { Exception = exceptionContext });
            }
        }
    }
}
