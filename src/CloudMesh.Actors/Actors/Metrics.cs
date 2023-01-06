using System.Diagnostics.Metrics;

namespace CloudMesh.Actors
{
    internal static class Metrics
    {
        internal static readonly Meter Meter = new("CloudMesh.Actors", "1.0.0");
    }
}
