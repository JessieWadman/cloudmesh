using Amazon.Lambda.Core;
using CloudMesh.Serialization;
using System.Text.Json;

namespace CloudMesh.Hosting.Lambda
{
    /// <summary>
    /// Used for non top-level statement lambdas, as well as for local debugging.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class LambdaHost<T>
        where T : class, new()
    {
        [LambdaSerializer(typeof(CloudMeshLambdaInvocationSerializer))]
        public Task<DynamicReturnValue> HandleAsync(JsonDocument request, ILambdaContext context)
            => LambdaServiceHost<T>.Handler(request, context);
    }
}
