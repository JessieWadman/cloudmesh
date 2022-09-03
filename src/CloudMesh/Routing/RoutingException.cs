using System.Runtime.Serialization;

namespace CloudMesh.Routing
{
    public class RoutingException : Exception
    {
        public RoutingException()
        {
        }

        public RoutingException(string? message) : base(message)
        {
        }

        public RoutingException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        protected RoutingException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
