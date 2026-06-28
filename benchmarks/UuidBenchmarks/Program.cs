using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using CloudMesh;

Console.WriteLine("Hello, World!");

var logger = new AccumulationLogger();

var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddLogger(logger)
    .WithOptions(ConfigOptions.DisableOptimizationsValidator);

BenchmarkRunner.Run<UuidBenchmarks>(config);

// write benchmark summary
Console.WriteLine(logger.GetLog());

[MemoryDiagnoser(true)]
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