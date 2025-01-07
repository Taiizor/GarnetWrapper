using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using GarnetWrapper.Options;
using Microsoft.Extensions.Options;

namespace GarnetWrapper.Benchmarks;

[MemoryDiagnoser]
public class GarnetBenchmarks
{
    private GarnetClient _client;
    private const string TestKey = "benchmark-test-key";
    private const string TestValue = "benchmark-test-value";

    [GlobalSetup]
    public void Setup()
    {
        var options = Options.Create(new GarnetOptions
        {
            Endpoints = new[] { "localhost:6379" },
            DefaultDatabase = 0
        });
        _client = new GarnetClient(options);
    }

    [Benchmark]
    public async Task SetAsync()
    {
        await _client.SetAsync(TestKey, TestValue);
    }

    [Benchmark]
    public async Task GetAsync()
    {
        await _client.GetAsync<string>(TestKey);
    }

    [Benchmark]
    public async Task SetAndGetAsync()
    {
        await _client.SetAsync(TestKey, TestValue);
        await _client.GetAsync<string>(TestKey);
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<GarnetBenchmarks>();
    }
} 