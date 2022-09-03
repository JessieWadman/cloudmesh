using System.Runtime.Serialization;

namespace CloudMesh.Routing
{
    public class NoRouteFoundException : RoutingException
    {
        public NoRouteFoundException()
        {
        }

        public NoRouteFoundException(string? message) : base(message)
        {
        }

        public NoRouteFoundException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        protected NoRouteFoundException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
