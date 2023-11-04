using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using Xunit.Abstractions;

namespace CloudMesh.Core.Tests;

public class FastDecimalParserTests
{
    private readonly ITestOutputHelper _output;

    public FastDecimalParserTests(ITestOutputHelper output)
    {
        _output = output;
    }
    
    // [Fact]
    public void Benchmark()
    {
        var logger = new AccumulationLogger();

        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddLogger(logger)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        BenchmarkRunner.Run<FastDecimalBenchmarks>(config);

        // write benchmark summary
        _output.WriteLine(logger.GetLog());
    }

    public class FastDecimalBenchmarks
    {
        private const string DecimalString = "123490123456.1235412";
        
        [Benchmark]
        public void FastDecimalParsing()
        {
            OptimizationHelpers.FastTryParseDecimal(DecimalString, DecimalSeparators.ISO, out var parsedValue);
        }
        
        [Benchmark]
        public void DefaultDecimalParsing()
        {
            decimal.TryParse(DecimalString, CultureInfo.InvariantCulture, out var parsedValue);
        }
    }
}