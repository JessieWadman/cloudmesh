using CloudMesh.Hosting.AspNetCore.Helpers;
using CloudMesh.Serialization;
using CloudMesh.Services;
using CloudMesh.Services.Hosting;
using CloudMesh.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CloudMesh.Hosting.AspNetCore
{
    internal class AspNetCoreServiceHandler
    {
        public static async Task<object?> HandleAsync(HttpContext context, string serviceName, string methodName, CancellationToken cancellationToken)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<AspNetCoreServiceHandler>>();

            if (!ServiceTypes.ServiceNamesToTypes.TryGetValue(serviceName, out var serviceType))
            {
                logger.LogWarning($"NotFound: No registration for service with name [{serviceName}]");
                return Results.NotFound();
            }

            if (context.RequestServices.GetRequiredService(serviceType) is not IService service)
            {
                logger.LogWarning($"NotFound: No dependency injection for service with name [{serviceName}]");
                return Results.NotFound();
            }

            var method = MethodCache.GetMethod(serviceType, methodName);
            if (method is null)
            {
                logger.LogWarning($"BadRequest: Method {methodName} not found on {serviceType.Name}");
                return Results.BadRequest();
            }

            var args = await ArgumentHelper.DeserializeArgumentsAsync(method, context.Request.Body, cancellationToken);

            if (args is null)
            {
                logger.LogWarning($"BadRequest: Failed to deserialize actor invocation payload.");
                return Results.BadRequest();
            }

            logger.LogDebug($"Invoking {serviceName}/{methodName}");

            try
            {
                var call = Dispatcher.Create(serviceType, service).Invoke(methodName, args, out var returnType);

                var task = AsyncExtensions.ToObjectTask(call);
                var retValue = await task;

                if (retValue == null || retValue is NoReturnType)
                {
                    return Results.NoContent();
                }
                return new { ret = retValue };
            }
            catch (Exception error)
            {
                return Results.Ok(new { Exception = ExceptionContext.Create(error.InnerException ?? error) });
            }
        }
    }
}
