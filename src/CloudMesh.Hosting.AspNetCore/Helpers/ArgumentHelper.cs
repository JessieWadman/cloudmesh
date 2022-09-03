using CloudMesh.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CloudMesh.Hosting.AspNetCore.Helpers
{
    internal static class ArgumentHelper
    {
        public static async Task<object?[]?> DeserializeArgumentsAsync(
            MethodInfo method, 
            Stream body, 
            CancellationToken cancellationToken)
        {
            var methodParameters = method.GetParameters().ToList();
            var cancellationTokenParameter = methodParameters
                .Where(p => p.ParameterType == typeof(CancellationToken))
                .FirstOrDefault();

            if (cancellationTokenParameter is not null)
                methodParameters.RemoveAt(cancellationTokenParameter.Position);

            var args = Array.Empty<object>();
            if (methodParameters.Count > 0)
            {
                var temp = await Serializer.Instance.DeserializeAsync(body, method);
                if (temp is null)
                {
                    return null;
                }
                else
                    args = temp!;
            }

            if (cancellationTokenParameter is not null)
            {
                Array.Resize(ref args, args.Length + 1);
                for (var i = args.Length - 1; i > cancellationTokenParameter.Position; --i)
                {
                    args[i] = args[i - 1];
                }
                args[cancellationTokenParameter.Position] = cancellationToken;
            }

            return args;
        }
    }
}
