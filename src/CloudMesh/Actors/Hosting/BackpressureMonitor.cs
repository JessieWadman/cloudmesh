namespace CloudMesh.Actors.Hosting
{
    public record BackpressureDetectedEventArgs(string ActorName, string Id);

    public delegate void BackpressureDetectedDelegate(BackpressureDetectedEventArgs e);

    public static class BackpressureMonitor
    {
        internal static void BackpressureDetected(string actorName, string id)
        {
            var handler = OnBackpressureDetected;
            try
            {
                handler?.Invoke(new(actorName, id));
            }
            catch { }
        }

        public static event BackpressureDetectedDelegate? OnBackpressureDetected;
    }
}
