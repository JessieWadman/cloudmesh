using CloudMesh.Actors.Routing.Ecs;
using CloudMesh.Actors.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ActorClientAwsExtensions
    {
        public static IActorClientOptionsBuilder AddCloudMapServiceDiscovery(this IActorClientOptionsBuilder builder, string cloudMapNamespace)
        {
            CloudMapRouterTableUpdater.CloudMapNamespace = cloudMapNamespace;                
            builder.Services.AddHostedService<CloudMapRouterTableUpdater>();
            return builder;
        }
    }
}
