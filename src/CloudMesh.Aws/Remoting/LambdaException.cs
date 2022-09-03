using CloudMesh.Remoting;

namespace CloudMesh.Aws.Remoting
{
    public class LambdaException : RemoteException
    {
        public LambdaException(string? message, string? exceptionType, string? log)
            : base(message, exceptionType)
        {
            Log = log;
        }

        public string? Log { get; }
    }
}
