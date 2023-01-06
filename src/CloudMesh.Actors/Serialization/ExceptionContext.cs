using System.Runtime.ExceptionServices;

namespace CloudMesh.Serialization
{
    public class ExceptionContext
    {
        public string? ExceptionType { get; init; }
        public string? Message { get; init; }
        public string? StackTrace { get; init; }

        public static ExceptionContext Create(Exception source)
        {
            var info = ExceptionDispatchInfo.Capture(source);
            return new()
            {
                ExceptionType = info.SourceException.GetType().FullName,
                Message = info.SourceException.Message,
                StackTrace = info.SourceException.StackTrace
            };
        }

        public void Throw()
        {
            var exception = new RemoteException(Message, ExceptionType);
            ExceptionDispatchInfo.SetRemoteStackTrace(exception, (StackTrace ?? string.Empty).ReplaceLineEndings());
            ExceptionDispatchInfo.Throw(exception);
        }
    }
}
