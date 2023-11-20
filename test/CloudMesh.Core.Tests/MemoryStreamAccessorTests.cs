using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using Xunit.Abstractions;

namespace CloudMesh.Core.Tests;

public class MemoryStreamAccessorTests
{
    private readonly ITestOutputHelper _output;

    public MemoryStreamAccessorTests(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void Benchmark()
    {
        var logger = new AccumulationLogger();

        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddLogger(logger)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        BenchmarkRunner.Run<MemoryStreamAccessorBenchmarks>(config);

        // write benchmark summary
        _output.WriteLine(logger.GetLog());
    }

    public class MemoryStreamAccessorBenchmarks
    {
        private static readonly byte[] TestString = Encoding.UTF8.GetBytes("Hello wonderful world");
        
        [Benchmark]
        public void ToArray()
        {
            using var memoryStream = new MemoryStream();
            memoryStream.Write(TestString);
            memoryStream.Position = 0;
            var arr = memoryStream.ToArray();
            var correct = arr.SequenceEqual(TestString);
        }
        
        [Benchmark]
        public void TryGetBuffer()
        {
            using var memoryStream = new MemoryStream();
            memoryStream.Write(TestString);
            memoryStream.Position = 0;
            memoryStream.TryGetBuffer(out var buffer);
            var correct = buffer.SequenceEqual(TestString);
        }
        
        [Benchmark]
        public unsafe void UnsafeGetMemory()
        {
            using var memoryStream = new MemoryStream();
            memoryStream.Write(TestString);
            memoryStream.Position = 0;
            var mem = memoryStream.UnsafeGetMemory();
            using var pin = mem.Pin();
            var start = (byte*)pin.Pointer;
            
            for (var i = 0; i < TestString.Length; i++)
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
            using var memoryStream = new MemoryStream();
            memoryStream.Write(TestString);
            memoryStream.Position = 0;
            var span = memoryStream.UnsafeGetSpan();
            var correct = span.SequenceEqual(TestString);
        }
    }
}