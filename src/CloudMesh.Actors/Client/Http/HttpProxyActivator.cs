using CloudMesh.Actors.Hosting;
using CloudMesh.Actors.Routing;
using System.Reflection;

namespace CloudMesh.Actors.Client.Http
{
    internal sealed class HttpProxyActivator : ProxyActivator<HttpActorProxy>
    {
        public HttpProxyActivator(IRoutingTable routingTable) 
            : base(routingTable)
        {
        }

        protected override T CreateProxyInstance<T>(IRouter router, string serviceName, string actorName, string id)
        {
            var proxy = DispatchProxy.Create<T, HttpActorProxy>();
            var httpProxy = proxy as HttpActorProxy;
            httpProxy!.Router = router;
            httpProxy.ServiceName = serviceName;
            httpProxy.ActorName = actorName;
            httpProxy.Id = id;
            httpProxy.LocalActorHost = ActorHost.Instance;
            return proxy;            
        }
    }
}