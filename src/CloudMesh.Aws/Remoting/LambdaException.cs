using CloudMesh.Serialization;
using System.Runtime.Serialization;

namespace CloudMesh.Aws.Remoting
{
    public class LambdaException : RemoteException
    {
        public LambdaException()
        {
        }

        public LambdaException(string? message) 
            : base(message)
        {
        }

        public LambdaException(string? message, string? exceptionType) 
            : base(message, exceptionType)
        {
        }

        public LambdaException(string? message, Exception? innerException) 
            : base(message, innerException)
        {
        }

        public LambdaException(string? message, string? exceptionType, string? log)
            : base(message, exceptionType)
        {
            Log = log;
        }

        protected LambdaException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public string? Log { get; }
    }
}
