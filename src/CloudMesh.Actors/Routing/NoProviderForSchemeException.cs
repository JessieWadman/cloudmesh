using System.Runtime.Serialization;

namespace CloudMesh.Routing
{
    public class NoProviderForSchemeException : RoutingException
    {
        public NoProviderForSchemeException()
        {
        }

        public NoProviderForSchemeException(string? message) : base(message)
        {
        }

        public NoProviderForSchemeException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        protected NoProviderForSchemeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
