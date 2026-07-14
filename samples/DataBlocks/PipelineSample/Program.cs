using CloudMesh.DataBlocks;

// ---------------------------------------------------------------------------------------------------------------
// A COMPOSITE DATA-PROCESSING PIPELINE
//
// This sample wires several DataBlocks into one pipeline to show how the building blocks compose. It processes a
// stream of incoming orders and demonstrates all four core patterns at once:
//
//   ingest ──▶ RoundRobinDataBlock ──▶ [ 4x EnrichmentWorker ]        (1) FAN-OUT: parallel enrichment
//                                             │
//                                             ▼
//                                        OrderRouter                   (2) ROUTING: by order value
//                                    ┌────────┼──────────────┐
//                                    ▼        ▼              ▼
//                            PriorityWriter  StandardWriter  RevenueAggregator
//                            (3) BUFFER:     (3) BUFFER:      (4) FAN-IN:
//                            small batch,    large batch,     window totals +
//                            fast flush      slow flush       periodic summary
//
// Every block processes one message at a time (so handler state needs no locking), receives typed messages via
// ReceiveAsync<T>, and is fed via SubmitAsync(message, sender). Blocks reference their downstream stage through an
// ICanSubmit passed into their constructor — that's all "wiring a pipeline" is.
// ---------------------------------------------------------------------------------------------------------------

// Build the pipeline back-to-front. Declaration order matters for shutdown: `await using` disposes in REVERSE
// order, so the downstream stages (declared first) are disposed LAST — they stay alive to drain in-flight work
// while the upstream stages (declared later) shut down first.

{ // Disposable scope to force-drain the workers. 
    
// (4) FAN-IN — every order is also tapped here to compute running revenue metrics over a time window.
    await using var metrics = new RevenueAggregator();

// (3) BUFFER — two sinks with different batching profiles.
    await using var priorityWriter = new PriorityWriter();
    await using var standardWriter = new StandardWriter();

// (2) ROUTING — inspects each enriched order and sends it to the right sink (plus the metrics tap).
    await using var router = new OrderRouter(priorityWriter, standardWriter, metrics);

// (1) FAN-OUT — distribute incoming orders across a pool of enrichment workers, round-robin. Each worker enriches
// its order and forwards the result to the router. (Swap RoundRobinDataBlock for SpillOverDataBlock to fill each
// worker to capacity before advancing, instead of spreading evenly.)
    await using var enrichmentPool = new RoundRobinDataBlock();
    enrichmentPool.AddTargets(() => new EnrichmentWorker(router), count: 4);

// Ingest a stream of orders. In a real app this loop would be a Kafka/SQS consumer, a file reader, etc.
    var regions = new[] { "EU", "US", "APAC" };
    Console.WriteLine("Ingesting orders...\n");
    for (var i = 0; i < 60; i++)
    {
        var amount = (decimal)Random.Shared.Next(10, 2500);
        var order = new OrderReceived($"ORD-{i:000}", regions[i % regions.Length], amount);
        await enrichmentPool.SubmitAsync(order, null); // SubmitAsync back-pressures if the pool is saturated
        await Task.Delay(Random.Shared.Next(5, 25)); // simulate a live stream rather than a burst
    }

    Console.WriteLine("\nIngest complete — draining the pipeline...\n");
}

Console.WriteLine("\nDone.");

// ---------------------------------------------------------------------------------------------------------------
// Messages
// ---------------------------------------------------------------------------------------------------------------

// Raw order as it arrives.
record OrderReceived(string OrderId, string Region, decimal Amount);

// Order after enrichment (e.g. tax computed, region validated).
record EnrichedOrder(string OrderId, string Region, decimal Amount, decimal Tax);

// ---------------------------------------------------------------------------------------------------------------
// (1) FAN-OUT stage — one of N identical workers behind the RoundRobinDataBlock.
// ---------------------------------------------------------------------------------------------------------------
sealed class EnrichmentWorker : DataBlock
{
    public EnrichmentWorker(ICanSubmit next)
    {
        // Handle each raw order: do the (simulated) enrichment work, then forward the result downstream.
        ReceiveAsync<OrderReceived>(async raw =>
        {
            await Task.Delay(Random.Shared.Next(5, 30));            // simulate I/O: tax lookup, validation, etc.
            var enriched = new EnrichedOrder(raw.OrderId, raw.Region, raw.Amount, Tax: raw.Amount * 0.25m);
            await next.SubmitAsync(enriched, this);                 // `this` is the sender ref
        });
    }
}

// ---------------------------------------------------------------------------------------------------------------
// (2) ROUTING stage — content-based routing. A plain DataBlock that inspects the message and forwards it to a
// different downstream block depending on its value. This is how you fork a pipeline by message content.
// ---------------------------------------------------------------------------------------------------------------
sealed class OrderRouter : DataBlock
{
    private const decimal HighValueThreshold = 1500m;

    public OrderRouter(ICanSubmit priority, ICanSubmit standard, ICanSubmit metricsTap)
    {
        ReceiveAsync<EnrichedOrder>(async order =>
        {
            // Tap: every order contributes to the running metrics (fan-in), regardless of where it's routed.
            await metricsTap.SubmitAsync(order, this);

            // Route by order value.
            if (order.Amount >= HighValueThreshold)
                await priority.SubmitAsync(order, this);
            else
                await standard.SubmitAsync(order, this);
        });
    }
}

// ---------------------------------------------------------------------------------------------------------------
// (3) BUFFER stage — batch orders before "persisting" them, to amortize the per-write cost (a DB insert, a bulk
// API call, etc.). BufferBlock<T> flushes when it reaches maxCapacity OR maxWaitTimeToFlush elapses, whichever
// comes first. Two profiles below show the tradeoff between latency and batch size.
// ---------------------------------------------------------------------------------------------------------------
sealed class PriorityWriter : BufferBlock<EnrichedOrder>
{
    // High-value orders: small batches, flushed quickly (low latency matters more than batch efficiency).
    public PriorityWriter() : base(maxCapacity: 5, maxWaitTimeToFlush: TimeSpan.FromMilliseconds(300)) { }

    protected override ValueTask FlushAsync(EnrichedOrder[] batch)
    {
        Console.WriteLine($"  [PRIORITY] flushed {batch.Length,2} high-value order(s): {string.Join(", ", batch.Select(o => o.OrderId))}");
        return ValueTask.CompletedTask;
    }
}

sealed class StandardWriter : BufferBlock<EnrichedOrder>
{
    // Normal orders: large batches, flushed less often (throughput matters more than latency).
    public StandardWriter() : base(maxCapacity: 20, maxWaitTimeToFlush: TimeSpan.FromSeconds(1)) { }

    protected override ValueTask FlushAsync(EnrichedOrder[] batch)
    {
        Console.WriteLine($"  [STANDARD] bulk-wrote {batch.Length,2} order(s)");
        return ValueTask.CompletedTask;
    }
}

// ---------------------------------------------------------------------------------------------------------------
// (4) FAN-IN stage — many upstream messages converge here and are aggregated over a time window. Instead of
// emitting one metric per order, AggregationDataBlock<T> accumulates in ReceiveOne and emits a single summary
// per window in FlushAsync — far fewer downstream writes (Prometheus, CloudWatch, ...).
// ---------------------------------------------------------------------------------------------------------------
sealed class RevenueAggregator : AggregationDataBlock<EnrichedOrder>
{
    public RevenueAggregator() : base(flushFrequency: TimeSpan.FromSeconds(1), bufferSize: 1000) { }

    private int _count;
    private decimal _revenue;

    // Called once per received order — accumulate window state. (No locking needed: one message at a time.)
    protected override bool ReceiveOne(EnrichedOrder order)
    {
        _count++;
        _revenue += order.Amount;
        return true;
    }

    // Called once per window — emit the summary and reset.
    protected override ValueTask FlushAsync()
    {
        if (_count > 0)
            Console.WriteLine($"[METRICS] window: {_count,2} orders, revenue {_revenue,9:C}, avg {_revenue / _count:C}");
        _count = 0;
        _revenue = 0;
        return ValueTask.CompletedTask;
    }
}
