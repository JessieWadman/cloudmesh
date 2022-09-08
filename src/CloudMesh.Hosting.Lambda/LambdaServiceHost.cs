using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using CloudMesh.Serialization;
using CloudMesh.Utils;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace CloudMesh.Hosting.Lambda
{
    public static class LambdaServiceHost<T>
        where T : class, new()
    {
        public static LambdaBootstrapBuilder Create()
        {
            return LambdaBootstrapBuilder.Create<JsonDocument, DynamicReturnValue>(Handler, new CloudMeshLambdaInvocationSerializer());
        }

        private static readonly JsonSerializerOptions serializerOptions = new()
        {
            MaxDepth = 16,
            PropertyNameCaseInsensitive = true,
            WriteIndented =
#if (DEBUG)
            true
#else
            false
#endif
    };

        private static readonly ConcurrentDictionary<string, Type> typeNames = new();

        [LambdaSerializer(typeof(CloudMeshLambdaInvocationSerializer))]
        internal static async Task<DynamicReturnValue> Handler(JsonDocument jdoc, ILambdaContext context)
        {
            var remainingTime = context.RemainingTime;
            // When debugging locally, and using mock lambda tool, remaining time is always TimeSpan.Zero
            if (remainingTime < TimeSpan.FromSeconds(1))
                remainingTime = TimeSpan.FromMinutes(1);

            using var timeout = new CancellationTokenSource(remainingTime);

            object?[] args;
            Type serviceInterface;
            MethodInvocationPayload invocation;
            using (jdoc)
            {
                invocation = jdoc.Deserialize<MethodInvocationPayload>(serializerOptions)
                    ?? throw new InvalidOperationException("Could not deserialize request!");
                if (string.IsNullOrEmpty(invocation.Service) || string.IsNullOrEmpty(invocation.Method))
                    throw new InvalidOperationException("Could not deserialize request!");

                serviceInterface = typeNames.GetOrAdd(invocation.Service,
                    s => typeof(T).GetInterface(invocation.Service)
                         ?? throw new InvalidOperationException($"Service {invocation.Service} not hosted here."));

                var method = MethodCache.GetMethod(serviceInterface, invocation.Method);
                var serializerType = SerializationHelper.GetSerializerTypeForLayout(method);
                var argObj = jdoc.Deserialize(serializerType.Type, serializerOptions)
                    ?? throw new InvalidOperationException("Unable to deserialize request!");

                args = SerializationHelper.GetParameterArray(method, serializerType, argObj, timeout.Token);
            }

            var instance = new T();
            context.Logger.LogLine($"Invoking {serviceInterface.Name}.{invocation.Method} with {args.Length} arguments.");
            var call = serviceInterface.InvokeMember(invocation.Method, BindingFlags.InvokeMethod, null, instance, args);

            var awaitable = AsyncExtensions.ToObjectTask(call);
            var callResult = await awaitable;

            if (callResult is null)
                callResult = NoReturnType.Instance;

            var returnValue = new DynamicReturnValue()
            {
                Value = callResult
            };
            context.Logger.LogLine($"Success: {JsonSerializer.Serialize(returnValue, new JsonSerializerOptions() { WriteIndented = false })}");
            return returnValue;
        }
    }
}