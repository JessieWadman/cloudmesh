using System.Runtime.Serialization;

namespace CloudMesh.Serialization
{

    public class RemoteException : Exception
    {
        public RemoteException()
        {
        }

        public RemoteException(string? message) : base(message)
        {
        }

        public RemoteException(string? message, string? exceptionType)
            : base(message)
        {
            ExceptionType = exceptionType;
        }

        public RemoteException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        protected RemoteException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public string? ExceptionType { get; }
    }
}
