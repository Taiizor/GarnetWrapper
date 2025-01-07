namespace GarnetWrapper.Options;

/// <summary>
/// Configuration options for GarnetWrapper
/// </summary>
public class GarnetOptions
{
    /// <summary>
    /// Redis server endpoints
    /// </summary>
    public string[] Endpoints { get; set; } = new[] { "localhost:6379" };

    /// <summary>
    /// Redis connection string
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Redis database ID
    /// </summary>
    public int DatabaseId { get; set; } = 0;

    /// <summary>
    /// Default database for Redis operations
    /// </summary>
    public int DefaultDatabase { get; set; } = 0;

    /// <summary>
    /// Whether to enable compression for large values
    /// </summary>
    public bool EnableCompression { get; set; } = false;

    /// <summary>
    /// Default expiration time for cache entries
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Timeout duration for retry attempts in milliseconds
    /// </summary>
    public int RetryTimeout { get; set; } = 1000;

    /// <summary>
    /// Health check configuration
    /// </summary>
    public HealthCheckOptions HealthCheck { get; set; } = new();

    /// <summary>
    /// Metrics collection configuration
    /// </summary>
    public MetricsOptions Metrics { get; set; } = new();

    /// <summary>
    /// Circuit breaker configuration
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    /// <summary>
    /// Logging configuration
    /// </summary>
    public LoggingOptions Logging { get; set; } = new();
}

/// <summary>
/// Health check configuration options
/// </summary>
public class HealthCheckOptions
{
    /// <summary>
    /// Whether health checks are enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Interval between health checks
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Metrics collection configuration options
/// </summary>
public class MetricsOptions
{
    /// <summary>
    /// Whether metrics collection is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Interval between metrics collection
    /// </summary>
    public TimeSpan CollectionInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long to keep metrics data
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Circuit breaker configuration options
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// Number of failures before circuit opens
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Duration circuit stays open before attempting to close
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Minimum number of operations before circuit can open
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;
}

/// <summary>
/// Logging configuration options
/// </summary>
public class LoggingOptions
{
    /// <summary>
    /// Time window for rate limiting
    /// </summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Maximum number of errors to log per window
    /// </summary>
    public int MaxErrorsPerWindow { get; set; } = 10;

    /// <summary>
    /// Performance thresholds for different operations
    /// </summary>
    public Dictionary<string, int> PerformanceThresholds { get; set; } = new()
    {
        { "Set", 100 },
        { "Get", 50 },
        { "Delete", 50 },
        { "Lock", 200 }
    };
}