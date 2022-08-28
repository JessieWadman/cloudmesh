using CloudMesh.Actors.Routing;
using System.Reflection;

namespace CloudMesh.Actors.Client
{
    public interface IProxyActivator
    {
        TActorInterface Create<TActorInterface>(string id) where TActorInterface : IActor;
    }

    internal abstract class ProxyActivator : IProxyActivator
    {
        public ProxyActivator()
        {
            ActorProxy.Instance = this;
        }

        public abstract TActorInterface Create<TActorInterface>(string id) where TActorInterface : IActor;
    }

    internal abstract class ProxyActivator<TProxyClass> : ProxyActivator
        where TProxyClass : DispatchProxy
    {
        protected readonly IRoutingTable RoutingTable;

        public ProxyActivator(IRoutingTable routingTable)
            : base()
        {
            this.RoutingTable = routingTable ?? throw new ArgumentNullException(nameof(routingTable));
        }

        public override TActorInterface Create<TActorInterface>(string id)
        {
            if (!ActorProxies.TryGetActorNameFor<TActorInterface>(out var proxyRegistration) || proxyRegistration is null)
                throw new InvalidOperationException($"There is no actor name registered for actor interface {typeof(TActorInterface).Name}! Did you forget to call services.AddActorClient(opts => opts.AddProxy<{typeof(TActorInterface).Name}>(\"{proxyRegistration}\"))?");

            var router = new ConsistentHashRouter(RoutingTable);

            return CreateProxyInstance<TActorInterface>(router, proxyRegistration.ServiceName, proxyRegistration.ActorName, id);
        }

        protected abstract T CreateProxyInstance<T>(IRouter router, string serviceName, string actorName, string id) where T : IActor;
    }
}