using CloudMesh.Actors.Client;
using CloudMesh.Actors.Client.Http;
using CloudMesh.Actors.DependencyInjection;
using CloudMesh.Actors.Hosting;
using CloudMesh.Actors.Routing;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class TestIpStore
    {
        public static string IpAddress { get; set; }

        public static void Set(string url)
        {
            var uri = new Uri(url);
            IpAddress = uri.Host + ":" + uri.Port;
        }
    }

    

    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddActorHosting(this IServiceCollection services, Action<IActorHostOptionsBuilder> configure)
        {
            LocalIpAddressResolver.Instance = LocalIpAddressResolvers.FromNetworkInterfaces();


            services.AddSingleton<IActorHost, ActorHost>();

            var options = new ActorOptions(services);
            configure(options);

            return services;
        }

        public static IServiceCollection AddActorClient(this IServiceCollection services, Action<IActorClientOptionsBuilder> configure)
        {
            LocalIpAddressResolver.Instance = LocalIpAddressResolvers.FromNetworkInterfaces();

            var routingTable = new RoutingTable();
            var proxyActivator = new HttpProxyActivator(routingTable);

            services.AddSingleton<IProxyActivator>(proxyActivator);
            services.AddSingleton<IRoutingTable>(routingTable);

            var options = new ActorOptions(services);
            configure(options);

            return services;
        }
    }
}
