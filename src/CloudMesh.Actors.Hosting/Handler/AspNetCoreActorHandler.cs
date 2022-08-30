using CloudMesh.Actors.Serialization;
using CloudMesh.Actors.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;

namespace CloudMesh.Actors.Hosting.Handler
{
    internal class AspNetCoreActorHandler
    {
        public static async Task<object?> HandleAsync(HttpContext context, string actorName, string id, string methodName)
        {
            var actorHost = context.RequestServices.GetRequiredService<IActorHost>();
            var logger = context.RequestServices.GetRequiredService<ILogger<AspNetCoreActorHandler>>();

            if (!actorHost.TryGetHostedActor(actorName, id, out var hostedActor) || hostedActor is null)
            {
                logger.LogWarning($"NotFound: No registration for actor with name [{actorName}]");
                return HttpStatusCode.NotFound;
            }

            var actorType = hostedActor.GetType();
            var method = MethodCache.GetMethod(actorType, methodName);
            if (method is null)
            {
                logger.LogWarning($"BadRequest: Method {methodName} not found on {actorType.Name}");
                return HttpStatusCode.BadRequest;
            }

            var methodParamterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var args = Array.Empty<object>();
            if (methodParamterTypes.Length > 0)
            {
                var temp = await Serializer.Instance.DeserializeAsync(context.Request.Body, actorName, methodName, methodParamterTypes);
                if (temp is null)
                {
                    logger.LogWarning($"BadRequest: Failed to deserialize actor invocation payload.");
                    return HttpStatusCode.BadRequest;
                }
                else
                    args = temp!;
            }

            logger.LogDebug($"Invoking {actorName}/{id}/{methodName}");
            var returnType = MethodCache.GetMaybeTaskReturnType(method, out var returnsTask, out var returnsVoid);

            var sender = new ActorAddress(context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "0.0.0.0");

            var retValue = await hostedActor.InvokeAsync(methodName, args, sender, !returnsVoid, default);

            if (retValue == null || retValue is NoReturnType)
            {
                return HttpStatusCode.NoContent;
            }

            return new { ret = retValue };
        }
    }
}
