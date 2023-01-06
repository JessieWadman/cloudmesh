using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace CloudMesh.Actors.Hosting
{
    public record BackpressureDetectedEventArgs(string ActorName, string Id);

    public delegate void BackpressureDetectedDelegate(BackpressureDetectedEventArgs e);

    public static class BackpressureMonitor
    {
        
        private static readonly ConcurrentDictionary<string, Counter<int>> counters = new();

        internal static void BackpressureDetected(string actorName, string id)
        {
            try
            {
                counters.GetOrAdd($"actorBackpressureDetected.{actorName}", name => Metrics.Meter.CreateCounter<int>(name)).Add(1);
            }
            catch { }

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
