using GarnetWrapper.Interfaces;
using GarnetWrapper.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace GarnetWrapper.Resilience;

/// <summary>
/// Circuit breaker implementation for Garnet cache operations
/// </summary>
/// <remarks>
/// Initializes a new instance of the GarnetCircuitBreaker
/// </remarks>
/// <param name="options">Configuration options</param>
/// <param name="logger">Logger instance</param>
public class GarnetCircuitBreaker(IOptions<GarnetOptions> options, ILogger<GarnetCircuitBreaker> logger) : IGarnetCircuitBreaker
{
    private readonly GarnetOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, CircuitState> _circuits = new();
    private readonly ConcurrentDictionary<string, int> _failureCount = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastFailure = new();
    private readonly ConcurrentDictionary<string, DateTime> _openTime = new();

    /// <summary>
    /// Executes an action with circuit breaker protection
    /// </summary>
    /// <typeparam name="T">Return type of the action</typeparam>
    /// <param name="action">Action to execute</param>
    /// <param name="circuitKey">Optional circuit key for separate circuit breakers</param>
    /// <returns>Result of the action</returns>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string circuitKey = "default")
    {
        if (IsCircuitOpen(circuitKey))
        {
            logger.LogWarning("Circuit {CircuitKey} is open, operation rejected", circuitKey);
            throw new CircuitBreakerOpenException($"Circuit {circuitKey} is open");
        }

        try
        {
            T result = await action();
            ResetFailureCount(circuitKey);
            return result;
        }
        catch (Exception ex)
        {
            IncrementFailureCount(circuitKey);
            logger.LogError(ex, "Operation failed for circuit {CircuitKey}", circuitKey);
            throw;
        }
    }

    private bool IsCircuitOpen(string circuitKey)
    {
        CircuitState currentState = _circuits.GetOrAdd(circuitKey, CircuitState.Closed);
        if (currentState == CircuitState.Open)
        {
            if (_openTime.TryGetValue(circuitKey, out DateTime openTime))
            {
                if (DateTime.UtcNow - openTime > _options.CircuitBreaker.BreakDuration)
                {
                    // Try to move to half-open state
                    _circuits.TryUpdate(circuitKey, CircuitState.HalfOpen, CircuitState.Open);
                    logger.LogInformation("Circuit {CircuitKey} moved to half-open state", circuitKey);
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    private void IncrementFailureCount(string circuitKey)
    {
        int failures = _failureCount.AddOrUpdate(circuitKey, 1, (_, count) => count + 1);
        _lastFailure.AddOrUpdate(circuitKey, DateTime.UtcNow, (_, _) => DateTime.UtcNow);

        if (failures >= _options.CircuitBreaker.FailureThreshold)
        {
            _circuits.TryUpdate(circuitKey, CircuitState.Open, CircuitState.Closed);
            _openTime.AddOrUpdate(circuitKey, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
            logger.LogWarning("Circuit {CircuitKey} opened due to {FailureCount} failures", circuitKey, failures);
        }
    }

    private void ResetFailureCount(string circuitKey)
    {
        _failureCount.TryRemove(circuitKey, out _);
        if (_circuits.TryGetValue(circuitKey, out CircuitState state) && state == CircuitState.HalfOpen)
        {
            _circuits.TryUpdate(circuitKey, CircuitState.Closed, CircuitState.HalfOpen);
            logger.LogInformation("Circuit {CircuitKey} closed after successful operation", circuitKey);
        }
    }
}

/// <summary>
/// Circuit breaker states
/// </summary>
public enum CircuitState
{
    /// <summary>
    /// Circuit is closed and operating normally
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit is open and rejecting requests
    /// </summary>
    Open,

    /// <summary>
    /// Circuit is allowing a limited number of requests to test if the system has recovered
    /// </summary>
    HalfOpen
}

/// <summary>
/// Exception thrown when circuit breaker is open
/// </summary>
public class CircuitBreakerOpenException : Exception
{
    /// <summary>
    /// Initializes a new instance of the CircuitBreakerOpenException
    /// </summary>
    /// <param name="message">Exception message</param>
    public CircuitBreakerOpenException(string message) : base(message)
    {
    }
}