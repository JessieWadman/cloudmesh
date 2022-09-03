using CloudMesh.Services.Internal;
using System.Reflection;

namespace CloudMesh.Services
{
    public static class ServiceProxy
    {
        public static T Create<T>() where T : IService
        {
            return DispatchProxy.Create<T, ServiceTransportProxy<T>>();
        }
    }
}
