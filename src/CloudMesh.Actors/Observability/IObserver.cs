namespace CloudMesh.Actors.Observability
{
    public interface IObservableSpan
    {
        void Annotate(string key, string value);
        void Trace(string message);
        void Fail(int errorCode, string error);
        void Complete();
        string RootContextId { get; }
        string ParentContextId { get; }
        string ContextId { get; }
    }

    public static class ObservableSpanExtensions
    {
        public static void Annotate(this IObservableSpan span, Dictionary<string, string> annotations)
        {
            foreach (var kp in annotations)
                span.Annotate(kp.Key, kp.Value);
        }
    }

    public interface IObservableContext
    {
        IObservableSpan Resume(string rootContextId, string parentContextId, string spanName);
        IObservableSpan StartNew(string spanName);
    }

    public interface IObserver
    {
        IObservableSpan BeginSpan(string spanName);
    }
}