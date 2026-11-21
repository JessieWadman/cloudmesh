using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using CloudMesh;

Console.WriteLine("Hello, World!");

var logger = new AccumulationLogger();

var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddLogger(logger)
    .WithOptions(ConfigOptions.DisableOptimizationsValidator);

BenchmarkRunner.Run<UuidBenchmarks>(config);
BenchmarkRunner.Run<ContendedUuidBenchmarks>(config);

// write benchmark summary
Console.WriteLine(logger.GetLog());

[MemoryDiagnoser(true)]
[Config(typeof(QuickConfig))]
public class UuidBenchmarks
{
    [Benchmark]
    public void DotNetNewGuid()
    {
        Guid.NewGuid();
    }
    
    [Benchmark]
    public void DotNetCreateVersion7()
    {
        Guid.CreateVersion7();
    }

    [Benchmark]
    public void CloudMeshUuid()
    {
        Uuid.Create();
    }
}

/// <summary>
/// Aggregate throughput under contention: a fixed number of ids split across <see cref="Threads"/> threads.
/// The reported Mean is wall time per id across all threads (lower = more total throughput).
/// </summary>
[MemoryDiagnoser]
[Config(typeof(ContendedConfig))]
public class ContendedUuidBenchmarks
{
    private const int TotalOps = 1 << 22; // 4,194,304 ids per invocation

    [Params(1, 4, 8, 16, 32)]
    public int Threads { get; set; }

    private Guid _sink;

    [GlobalSetup]
    public void Setup()
    {
        ThreadPool.GetMinThreads(out var worker, out var io);
        ThreadPool.SetMinThreads(Math.Max(worker, 64), io);
    }

    private void Run(Func<Guid> create)
    {
        var perThread = TotalOps / Threads;
        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, _ =>
        {
            Guid acc = default;
            for (var i = 0; i < perThread; i++)
                acc = create();
            _sink = acc;
        });
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = TotalOps)]
    public void DotNetNewGuid() => Run(Guid.NewGuid);

    [Benchmark(OperationsPerInvoke = TotalOps)]
    public void DotNetCreateVersion7() => Run(Guid.CreateVersion7);

    [Benchmark(OperationsPerInvoke = TotalOps)]
    public void CloudMeshUuid() => Run(Uuid.Create);
}
// Keep runs short: each benchmark case (one method x one param value) finishes in well under 30 seconds.

/// <summary>Single-threaded micro benchmarks: one launch, few short iterations.</summary>
public class QuickConfig : ManualConfig
{
    public QuickConfig() => AddJob(Job.Default
        .WithLaunchCount(1)
        .WithWarmupCount(3)
        .WithIterationCount(5)
        .WithIterationTime(Perfolizer.Horology.TimeInterval.FromMilliseconds(100)));
}

/// <summary>
/// Contended benchmarks: each invocation already mints 4M ids (~30-300 ms), so skip the pilot and unrolling
/// and run a fixed handful of invocations.
/// </summary>
public class ContendedConfig : ManualConfig
{
    public ContendedConfig() => AddJob(Job.Default
        .WithLaunchCount(1)
        .WithWarmupCount(2)
        .WithIterationCount(5)
        .WithUnrollFactor(1)
        .WithInvocationCount(2));
}
