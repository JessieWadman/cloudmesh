using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace CloudMesh.Routing
{
    public interface ILocalIpAddressResolver
    {
        string Resolve();
    }

    public static class LocalIpAddressResolver
    {
        public static ILocalIpAddressResolver Instance = FromNetworkInterfaces();

        public static ILocalIpAddressResolver FromNetworkInterfaces() => new SocketEndpointResolver();
        public static ILocalIpAddressResolver FromEnvironmentVariable(string environmentVariableName)
            => new EnvironmentVariableLocalIpAddressResolver(environmentVariableName);

        public static ILocalIpAddressResolver FromValue(string ipAddress)
            => new FixedValueLocalIpAddressResolver(ipAddress);

        public static ILocalIpAddressResolver From(Func<string> resolver)
            => new FuncLocalIpAddressResolver(resolver);

        private class SocketEndpointResolver : ILocalIpAddressResolver
        {
            private readonly object locker = new();
            private DateTime lastResolved;
            private string? lastResolvedAddress;

            public string Resolve()
            {
                lock (locker)
                {
                    if (lastResolvedAddress is null || lastResolved < DateTime.Now.AddMinutes(-1))
                    {
                        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                        socket.Connect("8.8.8.8", 65530);
                        IPEndPoint endPoint = (IPEndPoint)socket.LocalEndPoint!;
                        lastResolvedAddress = endPoint.Address.ToString();
                        lastResolved = DateTime.Now;
                        Debug.WriteLine($"IP Address determined as {lastResolvedAddress}");
                    }

                    return lastResolvedAddress;
                }
            }
        }

        private class EnvironmentVariableLocalIpAddressResolver : ILocalIpAddressResolver
        {
            private readonly string environmentVariableName;

            public EnvironmentVariableLocalIpAddressResolver(string environmentVariableName)
            {
                this.environmentVariableName = environmentVariableName ?? throw new ArgumentNullException(nameof(environmentVariableName));
            }

            public string Resolve()
                => Environment.GetEnvironmentVariable(environmentVariableName) ?? throw new InvalidOperationException($"Missing environment variable {environmentVariableName}");
        }

        private class FixedValueLocalIpAddressResolver : ILocalIpAddressResolver
        {
            private readonly string value;

            public FixedValueLocalIpAddressResolver(string value)
            {
                this.value = value ?? throw new ArgumentNullException(nameof(value));
            }

            public string Resolve() => value;
        }

        private class FuncLocalIpAddressResolver : ILocalIpAddressResolver
        {
            private readonly Func<string> resolver;

            public FuncLocalIpAddressResolver(Func<string> resolver)
            {
                this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            }

            public string Resolve() => resolver();
        }
    }
}
