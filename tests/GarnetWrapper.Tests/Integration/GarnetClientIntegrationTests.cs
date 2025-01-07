using GarnetWrapper.Metrics;
using GarnetWrapper.Options;
using GarnetWrapper.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

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
        var options = Options.Create(new GarnetOptions
        {
            Endpoints = new[] { "localhost:6379" },
            DefaultDatabase = 1, // Use a different database for integration tests
            EnableCompression = true,
            DefaultExpiry = TimeSpan.FromMinutes(5),
            MaxRetries = 3,
            RetryTimeout = 1000
        });

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = loggerFactory.CreateLogger<GarnetClient>();
        _circuitBreaker = new GarnetCircuitBreaker(options);
        _metrics = new GarnetMetrics();
        _client = new GarnetClient(options, _logger, _circuitBreaker, _metrics);
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
        var result = await _client.GetAsync<dynamic>("integration-test:compressed");

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
                var badClient = new GarnetClient(
                    Options.Create(new GarnetOptions
                    {
                        Endpoints = new[] { "nonexistent:6379" },
                        DefaultDatabase = 1
                    }),
                    _logger,
                    _circuitBreaker,
                    _metrics
                );

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
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < concurrentTasks; i++)
        {
            var task = Task.Run(async () =>
            {
                await _client.SetAsync($"integration-test:concurrent:{i}", i);
                var value = await _client.GetAsync<int>($"integration-test:concurrent:{i}");
                Assert.Equal(i, value);
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Assert
        var keys = new List<string>();
        await foreach (var key in _client.ScanAsync("integration-test:concurrent:*"))
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

        var tasks = new List<Task>();
        var random = new Random();

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
        var finalCount = await _client.GetAsync<int>(counterKey);
        Assert.Equal(100, finalCount); // 10 tasks * 10 increments each
    }
} 