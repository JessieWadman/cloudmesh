using System.Diagnostics;
using CloudMesh.DataBlocks;

await using var aggregator = new MetricsAggregation();

// Simulate doing 1000 HTTP requests
for (var i = 0; i < 1000; i++)
{
    // Clock the duration of each request
    var stopwatch = Stopwatch.StartNew();
    
    // Simulate doing the actual HTTP request
    await Task.Delay(Random.Shared.Next(100));
    
    // Send request duration for aggregation
    await aggregator.SubmitAsync(new HttpClientRequestDurationMetric(stopwatch.ElapsedMilliseconds), null);
}

// Message containing metric to aggregate
record HttpClientRequestDurationMetric(long RequestDurationInMilliseconds);

// The aggregator could publish metrics to for example AWS Cloudwatch, or to a Prometheus database, where each
// store operation has an associated cost, or performance overhead. Therefore we don't want to store each individual
// metric measured, but we instead want to aggregate metrics over a bit of time, thus issue fewer store requests to
// reduce cost and overhead.
sealed class MetricsAggregation : AggregationDataBlock<HttpClientRequestDurationMetric>
{
    public MetricsAggregation() 
        : base(
            // Wait up to 1 second for buffer to fill
            flushFrequency: TimeSpan.FromSeconds(1), 
            // Buffer at most 100 messages before flushing
            bufferSize: 100)
    {
    }

    private long totalDurationOfAllRequestsInWindow;
    private long measureCount;

    protected override bool ReceiveOne(HttpClientRequestDurationMetric metric)
    {
        totalDurationOfAllRequestsInWindow += metric.RequestDurationInMilliseconds;
        measureCount++;
        return true;
    }

    protected override ValueTask FlushAsync()
    {
        Console.WriteLine($"The average duration of the past {measureCount} HTTP client requests was {totalDurationOfAllRequestsInWindow/measureCount} ms");
        totalDurationOfAllRequestsInWindow = 0;
        measureCount = 0;
        return ValueTask.CompletedTask;
    }
}