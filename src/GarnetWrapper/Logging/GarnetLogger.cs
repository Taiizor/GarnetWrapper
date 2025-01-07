using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace GarnetWrapper.Logging;

/// <summary>
/// Enhanced logging mechanism for Garnet operations
/// </summary>
public class GarnetLogger
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, (DateTime LastLog, int Count)> _rateLimiter;
    private readonly TimeSpan _rateLimitWindow = TimeSpan.FromMinutes(1);

    public GarnetLogger(ILogger logger)
    {
        _logger = logger;
        _rateLimiter = new ConcurrentDictionary<string, (DateTime, int)>();
    }

    /// <summary>
    /// Logs an error with rate limiting to prevent log flooding
    /// </summary>
    public void LogError(Exception ex, string message, params object[] args)
    {
        var key = $"{message}_{string.Join("_", args)}";
        var now = DateTime.UtcNow;

        var (lastLog, count) = _rateLimiter.GetOrAdd(key, _ => (now, 0));

        if (now - lastLog > _rateLimitWindow)
        {
            // Reset counter if window has passed
            _rateLimiter.TryUpdate(key, (now, 1), (lastLog, count));
            _logger.LogError(ex, message, args);
        }
        else if (count < 10) // Limit to 10 similar logs per minute
        {
            _rateLimiter.TryUpdate(key, (lastLog, count + 1), (lastLog, count));
            _logger.LogError(ex, message, args);
        }
        else if (count == 10)
        {
            _rateLimiter.TryUpdate(key, (lastLog, count + 1), (lastLog, count));
            _logger.LogWarning("Rate limit reached for error: {Message}", message);
        }
    }

    /// <summary>
    /// Logs detailed cache operation information
    /// </summary>
    public void LogCacheOperation(string operation, string key, bool success, long elapsedMilliseconds)
    {
        var level = success ? LogLevel.Debug : LogLevel.Warning;
        _logger.Log(level, 
            "Cache {Operation} - Key: {Key}, Success: {Success}, Duration: {Duration}ms",
            operation, key, success, elapsedMilliseconds);
    }

    /// <summary>
    /// Logs connection state changes
    /// </summary>
    public void LogConnectionState(string endpoint, string state, string details = null)
    {
        var level = state == "Connected" ? LogLevel.Information : LogLevel.Warning;
        _logger.Log(level,
            "Redis connection {State} - Endpoint: {Endpoint}{Details}",
            state, endpoint, details != null ? $", Details: {details}" : "");
    }

    /// <summary>
    /// Logs circuit breaker state changes
    /// </summary>
    public void LogCircuitBreakerStateChange(string newState, string reason)
    {
        _logger.LogWarning(
            "Circuit breaker state changed to {State} - Reason: {Reason}",
            newState, reason);
    }

    /// <summary>
    /// Logs performance warnings when operations take longer than expected
    /// </summary>
    public void LogPerformanceWarning(string operation, string key, long elapsedMilliseconds, long threshold)
    {
        if (elapsedMilliseconds > threshold)
        {
            _logger.LogWarning(
                "Slow cache operation detected - Operation: {Operation}, Key: {Key}, Duration: {Duration}ms, Threshold: {Threshold}ms",
                operation, key, elapsedMilliseconds, threshold);
        }
    }

    /// <summary>
    /// Logs cache eviction events
    /// </summary>
    public void LogEviction(string key, string reason)
    {
        _logger.LogInformation(
            "Cache entry evicted - Key: {Key}, Reason: {Reason}",
            key, reason);
    }

    /// <summary>
    /// Logs memory usage statistics
    /// </summary>
    public void LogMemoryStats(long usedMemoryBytes, double hitRate)
    {
        _logger.LogInformation(
            "Cache memory stats - Used Memory: {UsedMemory}MB, Hit Rate: {HitRate}%",
            usedMemoryBytes / 1024.0 / 1024.0, hitRate * 100);
    }

    /// <summary>
    /// Logs batch operation results
    /// </summary>
    public void LogBatchOperation(string operation, int totalItems, int successCount, int failureCount, long elapsedMilliseconds)
    {
        var level = failureCount == 0 ? LogLevel.Information : LogLevel.Warning;
        _logger.Log(level,
            "Batch {Operation} completed - Total: {Total}, Success: {Success}, Failed: {Failed}, Duration: {Duration}ms",
            operation, totalItems, successCount, failureCount, elapsedMilliseconds);
    }

    /// <summary>
    /// Logs security-related events
    /// </summary>
    public void LogSecurityEvent(string eventType, string details)
    {
        _logger.LogWarning(
            "Security event detected - Type: {EventType}, Details: {Details}",
            eventType, details);
    }
} 