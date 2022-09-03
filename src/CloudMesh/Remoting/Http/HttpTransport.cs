using CloudMesh.Routing;
using CloudMesh.Serialization;
using CloudMesh.Utils;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudMesh.Remoting.Http
{
    public abstract class HttpTransport
    {
        protected static readonly SocketsHttpHandler pooledHttpHandler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            MaxConnectionsPerServer = 100
        };

        public static JsonSerializerOptions JsonOptions = new()
        {
#if (DEBUG)
            WriteIndented = true,
#else
            WriteIndented = false,
#endif
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault | JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 12,
            PropertyNameCaseInsensitive = true
        };

        protected virtual async Task<object?> InvokeHttpAsync(ResourceIdentifier route, string relativeUrl,
            MethodInfo method, object?[]? args)
        {
            if (Activity.Current is not null)
            {
                Activity.Current.SetTag("Resource", route.ToString());
                Activity.Current.SetTag("Method", method.Name);
            }
            try
            {
                args ??= Array.Empty<object?>();
                var returnType = method.GetMaybeTaskReturnType(out var _, out var returnsVoid);

                using var client = new HttpClient(pooledHttpHandler, false);
                client.BaseAddress = new Uri(route.ToString());

                var payload = SerializationHelper.CreateObjFor(method, args, out var cancellationToken);
#if (DEBUG)
                Activity.Current?.AddBaggage("Args", JsonSerializer.Serialize(payload, JsonOptions));
#endif

                cancellationToken ??= default;

                Activity.Current?.AddEvent(new("PutAsJsonAsync"));

                var response = await client.PutAsJsonAsync(relativeUrl, payload, JsonOptions, cancellationToken.Value);

                // Magical status code that indicates we skip straight to exception wrapping
                // In .NET 6 you cannot easily StatusCode with payload in minimal APIs, but you can in .NET 7
                if ((int)response.StatusCode == 566)
                {
                    var exceptionContext = await response.Content.ReadFromJsonAsync<ExceptionContext>();
                    if (exceptionContext is not null && !string.IsNullOrWhiteSpace(exceptionContext.Message))
                        exceptionContext.Throw();
                }

                Activity.Current?.AddEvent(new("ParseResponse"));
                Activity.Current?.AddTag("StatusCode", (int)response.StatusCode);

                response.EnsureSuccessStatusCode();
                if (response.StatusCode == HttpStatusCode.NoContent || returnsVoid)
                    return default;

                var returnValueType = typeof(ReturnValue<>).MakeGenericType(returnType);
                ReturnValue? returnValue = null;
                try
                {
                    returnValue = (ReturnValue?)await response.Content.ReadFromJsonAsync(returnValueType, JsonOptions);
                }
                catch (Exception error)
                {
                    throw new TransportException("Failed to parse response from remote endpoint", error);
                }

                if (returnValue is null)
                {
                    Activity.Current?.Complete();
                    return default;
                }

                if (returnValue.Exception is not null && !string.IsNullOrWhiteSpace(returnValue.Exception.Message))
                {
                    returnValue.Exception.Throw();
                }

                Activity.Current?.Complete();
                return returnValue.GetValue();
            }
            catch (Exception e)
            {
                Activity.Current?.Fail(e);
                throw;
            }
            finally
            {
                Activity.Current?.Dispose();
            }
        }
    }
}
