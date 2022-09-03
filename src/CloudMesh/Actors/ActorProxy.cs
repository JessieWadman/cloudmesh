using CloudMesh.Actors.Internal;
using System.Reflection;

namespace CloudMesh.Actors
{
    public static class ActorProxy
    {
        public static T Create<T>(string id) where T : IActor
        {
            var proxy = DispatchProxy.Create<T, ActorTransportProxy<T>>();

            var actorProxy = (ActorTransportProxy<T>)(object)proxy;
            actorProxy.Id = id;

            return proxy;
        }
    }
}
