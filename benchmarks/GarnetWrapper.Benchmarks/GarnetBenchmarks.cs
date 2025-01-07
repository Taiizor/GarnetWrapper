using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using GarnetWrapper.Metrics;
using GarnetWrapper.Options;
using GarnetWrapper.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace GarnetWrapper.Benchmarks
{
    [MemoryDiagnoser]
    [CategoriesColumn]
    [Config(typeof(Config))]
    [SimpleJob(RuntimeMoniker.Net90)]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
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
                    .WithIterationCount(10)
                    .WithWarmupCount(2)
                    .WithInvocationCount(8)
                    .WithUnrollFactor(2));
            }
        }

        public GarnetBenchmarks()
        {
            // Create a large test value
            StringBuilder sb = new();
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
            ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            ILogger<GarnetClient> logger = loggerFactory.CreateLogger<GarnetClient>();
            ILogger<GarnetCircuitBreaker> circuitBreakerLogger = loggerFactory.CreateLogger<GarnetCircuitBreaker>();
            GarnetMetrics metrics = new();

            // Setup normal client
            OptionsWrapper<GarnetOptions> options = new(new GarnetOptions
            {
                ConnectionString = "localhost:6379",
                DatabaseId = 0,
                EnableCompression = false,
                DefaultExpiry = TimeSpan.FromMinutes(5)
            });
            GarnetCircuitBreaker circuitBreaker = new(options, circuitBreakerLogger);
            _client = new GarnetClient(options, logger, circuitBreaker, metrics);

            // Setup compressed client
            OptionsWrapper<GarnetOptions> compressedOptions = new(new GarnetOptions
            {
                ConnectionString = "localhost:6379",
                DatabaseId = 0,
                EnableCompression = true,
                DefaultExpiry = TimeSpan.FromMinutes(5)
            });
            GarnetCircuitBreaker compressedCircuitBreaker = new(compressedOptions, circuitBreakerLogger);
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
            List<Task> tasks = new();
            for (int i = 0; i < 100; i++)
            {
                string key = $"{TestKey}:concurrent:{i}";
                tasks.Add(_client.SetAsync(key, i));
                tasks.Add(_client.GetAsync<int>(key));
            }
            await Task.WhenAll(tasks);
        }

        [Benchmark(Description = "Distributed Lock"), BenchmarkCategory("Locking")]
        public async Task DistributedLockAsync()
        {
            string lockKey = $"{TestKey}:lock:{Guid.NewGuid()}";
            if (await _client.LockAsync(lockKey, TimeSpan.FromMilliseconds(100)))
            {
                try
                {
                    await _client.IncrementAsync($"{TestKey}:counter");
                }
                finally
                {
                    await _client.UnlockAsync(lockKey);
                    await _client.DeleteAsync(lockKey);
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
}