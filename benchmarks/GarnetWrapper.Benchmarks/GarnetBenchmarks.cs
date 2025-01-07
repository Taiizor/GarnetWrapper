using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GarnetWrapper.Options;
using GarnetWrapper.Resilience;
using GarnetWrapper.Metrics;
using System.Text;

namespace GarnetWrapper.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
[Config(typeof(Config))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class GarnetBenchmarks
{
    private GarnetClient _client;
    private GarnetClient _compressedClient;
    private const string TestKey = "benchmark-test-key";
    private const string TestValue = "benchmark-test-value";
    private readonly string _largeValue;
    private readonly TestObject _complexObject;

    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default
                .WithIterationCount(100)
                .WithWarmupCount(5));
        }
    }

    public GarnetBenchmarks()
    {
        // Create a large test value
        var sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            sb.Append($"Value{i},");
        }
        _largeValue = sb.ToString();

        // Create a complex test object
        _complexObject = new TestObject
        {
            Id = 1,
            Name = "Benchmark Test",
            Description = _largeValue,
            CreatedAt = DateTime.UtcNow,
            Tags = Enumerable.Range(0, 100).Select(i => $"tag{i}").ToList(),
            Properties = Enumerable.Range(0, 100).ToDictionary(i => $"key{i}", i => $"value{i}")
        };
    }

    [GlobalSetup]
    public void Setup()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<GarnetClient>();
        var metrics = new GarnetMetrics();

        // Setup normal client
        var options = Options.Create(new GarnetOptions
        {
            Endpoints = new[] { "localhost:6379" },
            DefaultDatabase = 0,
            EnableCompression = false,
            DefaultExpiry = TimeSpan.FromMinutes(5)
        });
        var circuitBreaker = new GarnetCircuitBreaker(options);
        _client = new GarnetClient(options, logger, circuitBreaker, metrics);

        // Setup compressed client
        var compressedOptions = Options.Create(new GarnetOptions
        {
            Endpoints = new[] { "localhost:6379" },
            DefaultDatabase = 0,
            EnableCompression = true,
            DefaultExpiry = TimeSpan.FromMinutes(5)
        });
        var compressedCircuitBreaker = new GarnetCircuitBreaker(compressedOptions);
        _compressedClient = new GarnetClient(compressedOptions, logger, compressedCircuitBreaker, metrics);
    }

    [Benchmark(Description = "Set Small Value"), BenchmarkCategory("Small Data")]
    public async Task SetSmallAsync()
    {
        await _client.SetAsync(TestKey, TestValue);
    }

    [Benchmark(Description = "Get Small Value"), BenchmarkCategory("Small Data")]
    public async Task GetSmallAsync()
    {
        await _client.GetAsync<string>(TestKey);
    }

    [Benchmark(Description = "Set Large Value"), BenchmarkCategory("Large Data")]
    public async Task SetLargeAsync()
    {
        await _client.SetAsync($"{TestKey}:large", _largeValue);
    }

    [Benchmark(Description = "Get Large Value"), BenchmarkCategory("Large Data")]
    public async Task GetLargeAsync()
    {
        await _client.GetAsync<string>($"{TestKey}:large");
    }

    [Benchmark(Description = "Set Complex Object"), BenchmarkCategory("Complex Object")]
    public async Task SetComplexAsync()
    {
        await _client.SetAsync($"{TestKey}:complex", _complexObject);
    }

    [Benchmark(Description = "Get Complex Object"), BenchmarkCategory("Complex Object")]
    public async Task GetComplexAsync()
    {
        await _client.GetAsync<TestObject>($"{TestKey}:complex");
    }

    [Benchmark(Description = "Set Large Value (Compressed)"), BenchmarkCategory("Compression")]
    public async Task SetLargeCompressedAsync()
    {
        await _compressedClient.SetAsync($"{TestKey}:compressed", _largeValue);
    }

    [Benchmark(Description = "Get Large Value (Compressed)"), BenchmarkCategory("Compression")]
    public async Task GetLargeCompressedAsync()
    {
        await _compressedClient.GetAsync<string>($"{TestKey}:compressed");
    }

    [Benchmark(Description = "Concurrent Operations"), BenchmarkCategory("Concurrency")]
    public async Task ConcurrentOperationsAsync()
    {
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            var key = $"{TestKey}:concurrent:{i}";
            tasks.Add(_client.SetAsync(key, i));
            tasks.Add(_client.GetAsync<int>(key));
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Distributed Lock"), BenchmarkCategory("Locking")]
    public async Task DistributedLockAsync()
    {
        const string lockKey = $"{TestKey}:lock";
        if (await _client.LockAsync(lockKey, TimeSpan.FromSeconds(1)))
        {
            try
            {
                await _client.IncrementAsync($"{TestKey}:counter");
            }
            finally
            {
                await _client.UnlockAsync(lockKey);
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _compressedClient.Dispose();
    }
}

public class TestObject
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> Tags { get; set; }
    public Dictionary<string, string> Properties { get; set; }
}

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<GarnetBenchmarks>();
    }
} 