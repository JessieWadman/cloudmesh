using CloudMesh.Routing;

namespace CloudMesh.Actors.Hosting
{
    public class MethodInvocationEnvelope
    {
        private static long messageCounter;

        public MethodInvocationEnvelope(string methodName, object[]? args, ResourceIdentifier sender)
        {
            MessageId = Interlocked.Increment(ref messageCounter);
            MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
            Args = args;
            Sender = sender ?? throw new ArgumentNullException(nameof(sender));
        }

        public long MessageId { get; private set; }
        public ResourceIdentifier Sender { get; private set; }
        public string MethodName { get; init; }
        public object[]? Args { get; init; }
        public object? Result { get; set; }
        public TaskCompletionSource Completion { get; init; } = new();

        public void Complete(object? result)
        {
            this.Result = result;
            Completion.SetResult();
        }
    }
}
