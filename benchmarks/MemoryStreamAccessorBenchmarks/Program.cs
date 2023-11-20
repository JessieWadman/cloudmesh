using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;

Console.WriteLine("Hello, World!");

var logger = new AccumulationLogger();

var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddLogger(logger)
    .WithOptions(ConfigOptions.DisableOptimizationsValidator);

BenchmarkRunner.Run<MemoryStreamAccessorBenchmarks>(config);

// write benchmark summary
Console.WriteLine(logger.GetLog());

[MemoryDiagnoser(true)]
public class MemoryStreamAccessorBenchmarks
{
    private static readonly byte[] TestString = "Hello wonderful world"u8.ToArray();
    private readonly MemoryStream memoryStream = new();
    
    [GlobalSetup]
    public void Setup()
    {
        for (var i = 0; i < 1_000; i++)
            memoryStream.Write(TestString);
        memoryStream.Position = 0;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        memoryStream.Dispose();
    }

    [Benchmark]
    public void ToArray()
    {
        memoryStream.Position = 0;
        var arr = memoryStream.ToArray();
        var correct = arr.SequenceEqual(TestString);
    }

    [Benchmark]
    public void TryGetBuffer()
    {
        memoryStream.Position = 0;
        memoryStream.TryGetBuffer(out var buffer);
        var correct = buffer.SequenceEqual(TestString);
    }

    [Benchmark]
    public unsafe void UnsafeGetMemory()
    {
        memoryStream.Position = 0;
        var mem = memoryStream.UnsafeGetMemory();
        using var pin = mem.Pin();
        var start = (byte*)pin.Pointer;

        for (var i = 0; i < mem.Length; i++)
        {
            if (start[i] != TestString[i])
            {
                break;
            }
        }
    }

    [Benchmark]
    public unsafe void UnsafeGetSpan()
    {
        memoryStream.Position = 0;
        var span = memoryStream.UnsafeGetSpan();
        var correct = span.SequenceEqual(TestString);
    }
}