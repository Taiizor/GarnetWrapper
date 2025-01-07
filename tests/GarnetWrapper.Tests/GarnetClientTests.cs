using GarnetWrapper.Metrics;
using GarnetWrapper.Options;
using GarnetWrapper.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace GarnetWrapper.Tests;

public class GarnetClientTests
{
    private readonly Mock<IOptions<GarnetOptions>> _optionsMock;
    private readonly Mock<ILogger<GarnetClient>> _loggerMock;
    private readonly Mock<GarnetCircuitBreaker> _circuitBreakerMock;
    private readonly Mock<GarnetMetrics> _metricsMock;
    private readonly GarnetClient _client;

    public GarnetClientTests()
    {
        _optionsMock = new Mock<IOptions<GarnetOptions>>();
        _optionsMock.Setup(x => x.Value).Returns(new GarnetOptions
        {
            Endpoints = new[] { "localhost:6379" },
            DefaultDatabase = 0,
            EnableCompression = false,
            DefaultExpiry = TimeSpan.FromMinutes(5),
            MaxRetries = 3,
            RetryTimeout = 1000
        });

        _loggerMock = new Mock<ILogger<GarnetClient>>();
        _circuitBreakerMock = new Mock<GarnetCircuitBreaker>();
        _metricsMock = new Mock<GarnetMetrics>();

        _client = new GarnetClient(
            _optionsMock.Object,
            _loggerMock.Object,
            _circuitBreakerMock.Object,
            _metricsMock.Object);
    }

    [Fact]
    public async Task SetAsync_ShouldReturnTrue_WhenSuccessful()
    {
        // Arrange
        const string key = "test-key";
        const string value = "test-value";

        // Act
        bool result = await _client.SetAsync(key, value);

        // Assert
        Assert.True(result);
        _metricsMock.Verify(x => x.RecordOperation("set_success"), Times.Once);
    }

    [Fact]
    public async Task SetAsync_WithExpiry_ShouldSetCorrectExpiration()
    {
        // Arrange
        const string key = "test-key";
        const string value = "test-value";
        TimeSpan expiry = TimeSpan.FromMinutes(10);

        // Act
        await _client.SetAsync(key, value, expiry);
        TimeSpan? ttl = await _client.GetTimeToLiveAsync(key);

        // Assert
        Assert.NotNull(ttl);
        Assert.True(ttl.Value <= expiry);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnValue_WhenKeyExists()
    {
        // Arrange
        const string key = "test-key";
        const string expectedValue = "test-value";
        await _client.SetAsync(key, expectedValue);

        // Act
        string result = await _client.GetAsync<string>(key);

        // Assert
        Assert.Equal(expectedValue, result);
        _metricsMock.Verify(x => x.MeasureOperation("get"), Times.Once);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnDefault_WhenKeyDoesNotExist()
    {
        // Arrange
        const string key = "non-existent-key";

        // Act
        string result = await _client.GetAsync<string>(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithCompression_ShouldDecompressValue()
    {
        // Arrange
        _optionsMock.Setup(x => x.Value).Returns(new GarnetOptions
        {
            Endpoints = new[] { "localhost:6379" },
            DefaultDatabase = 0,
            EnableCompression = true
        });

        GarnetClient client = new(
            _optionsMock.Object,
            _loggerMock.Object,
            _circuitBreakerMock.Object,
            _metricsMock.Object);

        const string key = "compressed-key";
        const string value = "test-value-to-compress";

        // Act
        await client.SetAsync(key, value);
        string result = await client.GetAsync<string>(key);

        // Assert
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturnTrue_WhenKeyExists()
    {
        // Arrange
        const string key = "test-key";
        const string value = "test-value";
        await _client.SetAsync(key, value);

        // Act
        bool result = await _client.DeleteAsync(key);

        // Assert
        Assert.True(result);
        Assert.Null(await _client.GetAsync<string>(key));
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrue_WhenKeyExists()
    {
        // Arrange
        const string key = "test-key";
        const string value = "test-value";
        await _client.SetAsync(key, value);

        // Act
        bool result = await _client.ExistsAsync(key);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IncrementAsync_ShouldIncrementValue()
    {
        // Arrange
        const string key = "counter";
        await _client.SetAsync(key, "0");

        // Act
        long result = await _client.IncrementAsync(key);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task DecrementAsync_ShouldDecrementValue()
    {
        // Arrange
        const string key = "counter";
        await _client.SetAsync(key, "1");

        // Act
        long result = await _client.DecrementAsync(key);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task LockAsync_ShouldAcquireLock()
    {
        // Arrange
        const string key = "lock-key";
        TimeSpan expiry = TimeSpan.FromSeconds(30);

        // Act
        bool result = await _client.LockAsync(key, expiry);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ScanAsync_ShouldReturnMatchingKeys()
    {
        // Arrange
        await _client.SetAsync("test:1", "value1");
        await _client.SetAsync("test:2", "value2");
        await _client.SetAsync("other:1", "value3");

        // Act
        List<string> keys = new();
        await foreach (string? key in _client.ScanAsync("test:*"))
        {
            keys.Add(key);
        }

        // Assert
        Assert.Equal(2, keys.Count);
        Assert.Contains("test:1", keys);
        Assert.Contains("test:2", keys);
    }

    [Fact]
    public async Task SetAsync_WithComplexObject_ShouldSerializeAndDeserialize()
    {
        // Arrange
        TestObject testObject = new()
        {
            Id = 1,
            Name = "Test",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _client.SetAsync("complex-object", testObject);
        TestObject result = await _client.GetAsync<TestObject>("complex-object");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(testObject.Id, result.Id);
        Assert.Equal(testObject.Name, result.Name);
        Assert.Equal(testObject.CreatedAt.ToUniversalTime(), result.CreatedAt.ToUniversalTime());
    }
}

public class TestObject
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}