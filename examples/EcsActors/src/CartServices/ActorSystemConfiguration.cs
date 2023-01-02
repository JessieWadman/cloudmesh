using CartServices;
using CartServices.Helpers;
using CloudMesh.Routing;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Partition;
using Proto.DependencyInjection;
using Proto.Remote;
using Proto.Remote.GrpcNet;

namespace Microsoft.Extensions.DependencyInjection;

public static class ActorSystemConfiguration
{
    public static void AddActorSystem(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton(provider =>
        {
            var host = LocalIpAddressResolver.Instance.Resolve();
            var port = 3500;

            // actor system configuration

            var actorSystemConfig = ActorSystemConfig
                .Setup();

            // remote configuration

            var remoteConfig = GrpcNetRemoteConfig
                .BindTo(host, port)
                .WithProtoMessages(MessagesReflection.Descriptor);

            // cluster configuration

            var clusterConfig = ClusterConfig
                .Setup(
                    clusterName: "ProtoClusterTutorial",
                    clusterProvider: CloudMapClusterProvider.EcsAutoRegisteredInstances(config => config
                            .CloudMapNamespace("example1.cloudmesh")
                                .CloudMapServiceName("CartService", defaultPort: 3500)
                                    .Kinds<VehicleGrain, DayReportGrain, VehicleRouteGrain>()),
                    identityLookup: new PartitionIdentityLookup()
                )
                .WithClusterKind(
                    kind: VehicleGrainActor.Kind,
                    prop: Props.FromProducer(() =>
                            new VehicleGrainActor(
                                (context, clusterIdentity) => new VehicleGrain(context, clusterIdentity)
                            )
                        )
                )
                .WithClusterKind(
                    kind: VehicleRouteGrainActor.Kind,
                    prop: Props.FromProducer(() =>
                        new VehicleRouteGrainActor(
                            (context, clusterIdentity) => new VehicleRouteGrain(context, clusterIdentity)
                        )
                    )
                )
                .WithClusterKind(
                    kind: DayReportGrainActor.Kind,
                    prop: Props.FromProducer(() =>
                        new DayReportGrainActor(
                            (context, clusterIdentity) => new DayReportGrain(context, clusterIdentity)
                        )
                    )
                );

            // create the actor system

            return new ActorSystem(actorSystemConfig)
                .WithServiceProvider(provider)
                .WithRemote(remoteConfig)
                .WithCluster(clusterConfig);
        });
    }
}
