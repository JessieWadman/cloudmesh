namespace CloudMesh.DataBlocks
{
    public class CaptureBlock : DataBlock
    {
        private List<object> messages = new();

        public CaptureBlock()
            : base(1)
        {
            ReceiveAnyAsync(obj =>
            {
                lock (messages)
                {
                    messages.Add(obj);
                }
                return ValueTask.CompletedTask;
            });
        }

        public object[] DequeueAll()
        {
            lock (messages)
            {
                var snapshot = messages.ToArray();
                messages.Clear();
                return snapshot;
            }
        }
    }
}
