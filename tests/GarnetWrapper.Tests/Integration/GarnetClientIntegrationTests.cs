using GarnetWrapper.Interfaces;
using GarnetWrapper.Metrics;
using GarnetWrapper.Options;
using GarnetWrapper.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GarnetWrapper.Tests.Integration;

[Collection("Integration Tests")]
public class GarnetClientIntegrationTests : IAsyncLifetime
{
    private readonly GarnetClient _client;
    private readonly ILogger<GarnetClient> _logger;
    private readonly GarnetCircuitBreaker _circuitBreaker;
    private readonly GarnetMetrics _metrics;

    public GarnetClientIntegrationTests()
    {
        try
        {
            OptionsWrapper<GarnetOptions> options = new(new GarnetOptions
            {
                ConnectionString = "localhost:6379",
                DatabaseId = 0, // Varsayılan veritabanını kullanıyoruz
                EnableCompression = true,
                DefaultExpiry = TimeSpan.FromMinutes(5),
                MaxRetries = 3,
                RetryTimeout = 1000,
                CircuitBreaker = new CircuitBreakerOptions
                {
                    FailureThreshold = 3,
                    BreakDuration = TimeSpan.FromSeconds(10),
                    MinimumThroughput = 2
                }
            });

            ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
            });

            _logger = loggerFactory.CreateLogger<GarnetClient>();
            ILogger<GarnetCircuitBreaker> circuitBreakerLogger = loggerFactory.CreateLogger<GarnetCircuitBreaker>();
            _circuitBreaker = new GarnetCircuitBreaker(options, circuitBreakerLogger);
            _metrics = new GarnetMetrics();

            // Try to connect to Redis with retry
            int retryCount = 0;
            const int maxRetries = 3;
            Exception lastException = null;

            while (retryCount < maxRetries)
            {
                try
                {
                    _client = new GarnetClient(options, _logger, _circuitBreaker, _metrics);
                    
                    // Test connection with a simple SET operation
                    var testKey = "integration-test:connection-test";
                    _client.SetAsync(testKey, "test").Wait();
                    _client.DeleteAsync(testKey).Wait();
                    return; // Connection successful
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Failed to connect to Redis (attempt {RetryCount}/{MaxRetries})", retryCount + 1, maxRetries);
                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        Thread.Sleep(1000 * retryCount); // Exponential backoff
                    }
                }
            }

            throw new SkipException($"Redis server is not available after {maxRetries} attempts. Integration tests will be skipped.", lastException);
        }
        catch (Exception ex)
        {
            throw new SkipException("Redis server is not available. Integration tests will be skipped.", ex);
        }
    }

    public async Task InitializeAsync()
    {
        // Clean up the test database before each test run
        await _client.DeleteAsync("integration-test:*");
    }

    public async Task DisposeAsync()
    {
        // Clean up after tests
        await _client.DeleteAsync("integration-test:*");
        _client.Dispose();
    }

    [Fact]
    public async Task CompleteWorkflow_ShouldSucceed()
    {
        // Set a value
        await _client.SetAsync("integration-test:key1", "value1");

        // Verify it exists
        bool exists = await _client.ExistsAsync("integration-test:key1");
        Assert.True(exists);

        // Get the value
        string value = await _client.GetAsync<string>("integration-test:key1");
        Assert.Equal("value1", value);

        // Set expiry
        await _client.SetExpiryAsync("integration-test:key1", TimeSpan.FromSeconds(1));

        // Wait for expiry
        await Task.Delay(1500);

        // Verify key has expired
        exists = await _client.ExistsAsync("integration-test:key1");
        Assert.False(exists);
    }

    [Fact]
    public async Task Compression_ShouldWork()
    {
        // Arrange
        var largeObject = new TestLargeObject
        {
            Id = 1,
            Name = "Test",
            Description = new string('x', 1000), // Create a large string
            Data = Enumerable.Range(0, 1000).Select(i => i.ToString()).ToList()
        };

        // Act
        await _client.SetAsync("integration-test:compressed", largeObject);
        var result = await _client.GetAsync<TestLargeObject>("integration-test:compressed");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Test", result.Name);
        Assert.Equal(1000, result.Description.Length);
        Assert.Equal(1000, result.Data.Count);
    }

    public class TestLargeObject
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> Data { get; set; }
    }

    [Fact]
    public async Task CircuitBreaker_ShouldPreventCascadingFailures()
    {
        // Arrange
        int failureCount = 0;
        var badOptions = new OptionsWrapper<GarnetOptions>(new GarnetOptions
        {
            ConnectionString = "nonexistent:6379",
            DatabaseId = 0,
            RetryTimeout = 100, // Kısa timeout
            MaxRetries = 1, // Tek deneme
            CircuitBreaker = new CircuitBreakerOptions
            {
                FailureThreshold = 2, // 2 hatadan sonra devre kesici açılsın
                BreakDuration = TimeSpan.FromSeconds(1),
                MinimumThroughput = 1
            }
        });

        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        ILogger<GarnetClient> badLogger = loggerFactory.CreateLogger<GarnetClient>();
        ILogger<GarnetCircuitBreaker> circuitBreakerLogger = loggerFactory.CreateLogger<GarnetCircuitBreaker>();
        IGarnetCircuitBreaker badCircuitBreaker = new GarnetCircuitBreaker(badOptions, circuitBreakerLogger);
        IGarnetMetrics badMetrics = new GarnetMetrics();

        // Act
        using var badClient = new GarnetClient(badOptions, badLogger, badCircuitBreaker, badMetrics);
        
        for (int i = 0; i < 10; i++)
        {
            try
            {
                await badClient.SetAsync("test-key", "test-value");
                await Task.Delay(100); // Kısa bekleme
            }
            catch
            {
                failureCount++;
            }
        }

        // Assert
        Assert.True(failureCount > 0, "En az bir hata olmalı");
        Assert.True(failureCount < 10, "Devre kesici devreye girmeli ve tüm istekler hata vermemeli");
    }

    [Fact]
    public async Task ConcurrentOperations_ShouldSucceed()
    {
        // Arrange
        const int concurrentTasks = 100;
        List<Task> tasks = new();

        // Act
        for (int i = 0; i < concurrentTasks; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await _client.SetAsync($"integration-test:concurrent:{i}", i);
                int value = await _client.GetAsync<int>($"integration-test:concurrent:{i}");
                Assert.Equal(i, value);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        List<string> keys = new();
        await foreach (string? key in _client.ScanAsync("integration-test:concurrent:*"))
        {
            keys.Add(key);
        }
        Assert.Equal(concurrentTasks, keys.Count);
    }

    [Fact]
    public async Task DistributedLock_ShouldPreventRaceConditions()
    {
        // Arrange
        const string lockKey = "integration-test:lock";
        const string counterKey = "integration-test:counter";
        await _client.SetAsync(counterKey, 0L);

        List<Task> tasks = new();
        Random random = new();

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < 10; j++)
                {
                    // Try to acquire lock
                    if (await _client.LockAsync(lockKey, TimeSpan.FromSeconds(5)))
                    {
                        try
                        {
                            // Simulate some work
                            await Task.Delay(random.Next(10, 50));

                            // Increment counter
                            await _client.IncrementAsync(counterKey);
                        }
                        finally
                        {
                            // Release lock
                            await _client.UnlockAsync(lockKey);
                        }
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        long finalCount = await _client.GetAsync<long>(counterKey);
        Assert.Equal(100L, finalCount); // 10 tasks * 10 increments each
    }
}