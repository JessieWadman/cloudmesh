using Amazon.Lambda;
using Amazon.Lambda.Model;
using CloudMesh.Routing;
using CloudMesh.Serialization;
using CloudMesh.Services.Internal;
using CloudMesh.Utils;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
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
            await Serializer.Instance.SerializeAsync(payload, method, arguments, out var cancellationToken);
            payload.Seek(-1, SeekOrigin.End);
            payload.Write(Encoding.UTF8.GetBytes($",\"$service\":\"{method.DeclaringType!.Name}\",\"$method\":\"{method.Name}\"}}"));
            payload.Seek(0, SeekOrigin.Begin);

            var test = Encoding.UTF8.GetString(payload.ToArray());

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(10)); // Max lambda timeout is 15 mintues.
            if (cancellationToken == null || cancellationToken.Value == default) 
                cancellationToken = timeout.Token;

            InvokeResponse? response;
            using var client = new AmazonLambdaClient();
            try
            {
                response = await client.InvokeAsync(new Amazon.Lambda.Model.InvokeRequest
                {
                    FunctionName = route.Resource,
                    InvocationType = InvocationType.RequestResponse,
                    PayloadStream = payload,
                    LogType = IncludeFunctionLogs ? LogType.Tail : LogType.None
                }, cancellationToken ?? default);
            }
            catch (Exception error)
            {
                Console.WriteLine($"[Lambda invoker]: UNHANDLED EXCEPTION INVOKING LAMBDA {route.Resource}: {error.Message}");
                throw new LambdaException($"Failed to invoke lambda: {error.Message}", error);
            }

            if (response.ContentLength == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(response.FunctionError))
            {
                string? log = null;
                if (IncludeFunctionLogs)
                {
                    log = Encoding.UTF8.GetString(Convert.FromBase64String(response.LogResult));
                    var exception = new LambdaException(response.FunctionError, "LambdaException", log);
                    ExceptionDispatchInfo.SetRemoteStackTrace(exception, log);
                    ExceptionDispatchInfo.Throw(exception);
                }
                throw new LambdaException(response.FunctionError, "LambdaException", log);
            }

            var returnType = method.GetMaybeTaskReturnType(out _, out _);

            try
            {
                using var jdoc = JsonDocument.Parse(response.Payload);
                return jdoc.RootElement.GetProperty("Value").Deserialize(returnType, IgnoreCase);
            }
            catch (Exception error)
            {
                Console.WriteLine($"[Lambda] UNHANDLED EXCEPTION DESERIALIZING RESPONSE: {error.ToString()}");
                throw new LambdaException("Failed to deserialize response");
            }
        }
    }
}
