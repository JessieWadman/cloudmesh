namespace CloudMesh.Actors
{
    public class Envelope
    {
        private static long messageCounter;

        public Envelope(string methodName, object[]? args, ActorAddress sender)
        {
            MessageId = Interlocked.Increment(ref messageCounter);
            MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
            Args = args;
            Sender = sender ?? throw new ArgumentNullException(nameof(sender));
        }

        public long MessageId { get; private set; }
        public ActorAddress Sender { get; private set; }
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
