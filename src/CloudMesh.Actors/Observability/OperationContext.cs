namespace CloudMesh.Observability
{
    public class OperationContext
    {
        public OperationContext()
        {
            RootContextId = Guid.NewGuid().ToString();
            ParentContextId = RootContextId;
            ContextId = Guid.NewGuid().ToString();
        }

        public string RootContextId { get; set; }
        public string ParentContextId { get; set; }
        public string ContextId { get; set; }

        private static AsyncLocal<OperationContext> current = new();

        public static OperationContext Current
        {
            get
            {
                lock (current)
                {
                    return current.Value ??= new ();
                }
            }
        }

        public static void Resume(OperationContext parent)
        {
            var currentContext = Current;
            currentContext.RootContextId = parent.RootContextId;
            currentContext.ParentContextId = parent.ContextId;
        }

        public override string ToString()
        {
            return $"{Current.RootContextId}:{Current.ContextId}";
        }
    }
}
