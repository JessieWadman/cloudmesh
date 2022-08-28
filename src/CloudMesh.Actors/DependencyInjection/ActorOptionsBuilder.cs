using CloudMesh.Actors.Client;
using CloudMesh.Actors.Hosting;
using CloudMesh.Actors.Routing.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CloudMesh.Actors.DependencyInjection
{
    public interface IActorClientOptionsBuilder
    {
        IServiceCollection Services { get; }
        IActorHostOptionsBuilder AddProxy<TActorInterface>(string serviceName, string actorName) where TActorInterface : IActor;
        IActorHostOptionsBuilder AddTestServiceDiscovery();
    }

    public interface IActorHostOptionsBuilder
    {
        IServiceCollection Services { get; }
        IActorHostOptionsBuilder AddActor<TActorType>(string actorName) where TActorType : Actor;
    }

    internal class ActorOptions : IActorHostOptionsBuilder, IActorClientOptionsBuilder
    {
        public IServiceCollection Services { get; }

        public bool ServiceDiscoveryInitialized { get; set; }

        public ActorOptions(IServiceCollection services)
        {
            this.Services = services;
        }

        public IActorHostOptionsBuilder AddActor<TActorType>(string actorName) where TActorType : Actor
        {
            ActorTypes.Register<TActorType>(actorName);
            return this;
        }

        public IActorHostOptionsBuilder AddProxy<TActorInterface>(string serviceName, string actorName) where TActorInterface : IActor
        {
            ActorProxies.Register<TActorInterface>(serviceName, actorName);
            return this;
        }

        public IActorHostOptionsBuilder AddTestServiceDiscovery()
        {
            Services.AddHostedService<TestRoutingTableUpdater>();
            return this;
        }
    }
}
