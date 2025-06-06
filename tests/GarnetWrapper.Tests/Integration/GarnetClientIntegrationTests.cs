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
            var options = new OptionsWrapper<GarnetOptions>(new GarnetOptions
            {
                ConnectionString = "localhost:6379",
                DatabaseId = 1, // Use a different database for integration tests
                EnableCompression = true,
                DefaultExpiry = TimeSpan.FromMinutes(5),
                MaxRetries = 3,
                RetryTimeout = 1000
            });

            ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
            });

            _logger = loggerFactory.CreateLogger<GarnetClient>();
            var circuitBreakerLogger = loggerFactory.CreateLogger<GarnetCircuitBreaker>();
            _circuitBreaker = new GarnetCircuitBreaker(options, circuitBreakerLogger);
            _metrics = new GarnetMetrics();
            _client = new GarnetClient(options, _logger, _circuitBreaker, _metrics);

            // Test Redis connection
            _client.ExistsAsync("test").Wait();
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
        var largeObject = new
        {
            Id = 1,
            Name = "Test",
            Description = new string('x', 1000), // Create a large string
            Data = Enumerable.Range(0, 1000).Select(i => i.ToString()).ToList()
        };

        // Act
        await _client.SetAsync("integration-test:compressed", largeObject);
        dynamic result = await _client.GetAsync<dynamic>("integration-test:compressed");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, (int)result.Id);
        Assert.Equal("Test", (string)result.Name);
    }

    [Fact]
    public async Task CircuitBreaker_ShouldPreventCascadingFailures()
    {
        // Arrange
        int failureCount = 0;

        // Act & Assert
        for (int i = 0; i < 10; i++)
        {
            try
            {
                // Try to access a non-existent Redis instance
                OptionsWrapper<GarnetOptions> badOptions = new(new GarnetOptions
                {
                    ConnectionString = "nonexistent:6379",
                    DatabaseId = 1
                });

                ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                ILogger<GarnetClient> badLogger = loggerFactory.CreateLogger<GarnetClient>();
                ILogger<GarnetCircuitBreaker> circuitBreakerLogger = loggerFactory.CreateLogger<GarnetCircuitBreaker>();
                GarnetCircuitBreaker badCircuitBreaker = new(badOptions, circuitBreakerLogger);
                GarnetMetrics badMetrics = new();

                GarnetClient badClient = new(badOptions, badLogger, badCircuitBreaker, badMetrics);
                await badClient.SetAsync("integration-test:key", "value");
            }
            catch
            {
                failureCount++;
            }
        }

        // Circuit should be open after multiple failures
        Assert.True(failureCount < 10);
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
        await _client.SetAsync(counterKey, 0);

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
        int finalCount = await _client.GetAsync<int>(counterKey);
        Assert.Equal(100, finalCount); // 10 tasks * 10 increments each
    }
}