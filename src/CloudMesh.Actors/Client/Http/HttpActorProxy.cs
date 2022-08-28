using CloudMesh.Actors.Hosting;
using CloudMesh.Actors.Routing;
using CloudMesh.Actors.Serialization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;

namespace CloudMesh.Actors.Client.Http
{
    public class HttpActorProxy : DispatchProxy
    {
        private static readonly SocketsHttpHandler pooledHttpHandler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            MaxConnectionsPerServer = 100
        };

        public string ActorName;
        public string ServiceName;
        public IActorHost? LocalActorHost;
        public IRouter Router;
        public string Id;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            arguments ??= Array.Empty<object>();
            var returnType = method.ReturnType;
            var expectsTask = typeof(Task).IsAssignableFrom(method.ReturnType);
            if (expectsTask)
            {
                if (method.ReturnType.GenericTypeArguments.Length == 0)
                    returnType = typeof(NoReturnType);
                else
                    returnType = method.ReturnType.GenericTypeArguments[0];
            }

            Task<object?> asyncInvocation;

            if (!Router.TryResolve(ServiceName, ActorName, Id, out ActorLocation? actorLocation) || actorLocation is null)
                asyncInvocation = WaitForRoutingTableAndThenInvoke();
            else
                asyncInvocation = InvokeLocalMaybe();

            if (typeof(Task).IsAssignableFrom(method.ReturnType))
                return TaskConverter.Convert(asyncInvocation, returnType);
            else
                return asyncInvocation.GetAwaiter().GetResult();

            async Task<object?> WaitForRoutingTableAndThenInvoke()
            {
                actorLocation = await Router.TryResolveAsync(ServiceName, ActorName, Id, TimeSpan.FromSeconds(3));
                if (actorLocation is null)
                    throw new RoutingException($"Could not resolve an instance for {ServiceName}/{ActorName}");
                return await InvokeLocalMaybe();
            }

            Task<object?> InvokeLocalMaybe()
            {
                // If actor is on localhost, and we're hosting actors, do local invocation rather than across the network
                if (actorLocation.IsLocal &&
                    LocalActorHost is not null &&
                    LocalActorHost.TryGetHostedActor(ActorName, Id, out var localActor) &&
                    localActor is not null)
                {
                    var call = localActor.InvokeAsync(method.Name, arguments, ActorAddress.Local, default);
                    return call;
                }

                return InvokeOverHttp();
            }

            async Task<object?> InvokeOverHttp()
            {
                try
                {
                    using var client = new HttpClient(pooledHttpHandler, false);
                    client.BaseAddress = new Uri($"http://{actorLocation.Address}");

                    var parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();

                    var payload = SerializationHelper.CreateObjFor(parameterTypes, arguments);
                    var response = await client.PutAsJsonAsync($"/actors/{ActorName}/{Id}/{method.Name}", payload);
                    response.EnsureSuccessStatusCode();
                    if (response.StatusCode == HttpStatusCode.NoContent || method.ReturnType == typeof(NoReturnType))
                        return default;

                    var returnValueType = typeof(ReturnValue<>).MakeGenericType(returnType);
                    var returnValue = (ReturnValue?)await response.Content.ReadFromJsonAsync(returnValueType);
                    if (returnValue is null)
                        return default;
                    return returnValue.GetValue();

                }
                catch (Exception ex)
                {
                    throw new RoutingException(ex.Message, ex);
                }
            }
        }
    }
}