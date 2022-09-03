using Amazon.Lambda;
using CloudMesh.Routing;
using CloudMesh.Serialization;
using CloudMesh.Services.Internal;
using CloudMesh.Utils;
using System.Reflection;
using System.Text.Json;

namespace CloudMesh.Aws.Remoting
{
    public class LambdaInvoker : IServiceTransport
    {
        public static readonly LambdaInvoker Instance = new();
#if (DEBUG)
        public static bool IncludeFunctionLogs = true;
#else
        public static bool IncludeFunctionLogs = false;
#endif

        private static readonly JsonSerializerOptions IgnoreCase = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async ValueTask<object?> InvokeAsync(ResourceIdentifier route, MethodInfo method, object?[] arguments)
        {
            using var payload = new MemoryStream();
            await Serializer.Instance.SerializeAsync(payload, method, arguments);
            payload.Seek(0, SeekOrigin.Begin);

            using var client = new AmazonLambdaClient();
            var response = await client.InvokeAsync(new Amazon.Lambda.Model.InvokeRequest
            {
                FunctionName = route.Resource,
                InvocationType = InvocationType.RequestResponse,
                PayloadStream = payload,
                LogType = IncludeFunctionLogs ? LogType.Tail : LogType.None
            });

            if (response.ContentLength == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(response.FunctionError))
            {
                throw new LambdaException(response.FunctionError, "LambdaException", response.LogResult);
            }

            var returnType = method.GetMaybeTaskReturnType(out _, out _);
            return await JsonSerializer.DeserializeAsync(response.Payload, returnType, IgnoreCase);
        }
    }
}
