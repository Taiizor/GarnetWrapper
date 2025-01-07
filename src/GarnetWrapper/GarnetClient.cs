namespace GarnetWrapper;
using GarnetWrapper.Interfaces;
using GarnetWrapper.Metrics;
using GarnetWrapper.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// A high-performance distributed caching client for Garnet cache-store
/// Provides operations for storing, retrieving, and managing cached data with support for compression,
/// circuit breaking, and metrics collection
/// </summary>
public class GarnetClient : IGarnetClient
{
    private readonly ConnectionMultiplexer _connection;
    private readonly IDatabase _db;
    private readonly ILogger<GarnetClient> _logger;
    private readonly IGarnetCircuitBreaker _circuitBreaker;
    private readonly IGarnetMetrics _metrics;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks;
    private readonly ObjectPool<MemoryStream> _memoryStreamPool;
    private readonly Stopwatch _stopwatch;
    private readonly IOptions<GarnetOptions> _options;

    /// <summary>
    /// Initializes a new instance of the GarnetClient
    /// </summary>
    /// <param name="options">Configuration options for the Garnet client</param>
    /// <param name="logger">Logger instance for diagnostic information</param>
    /// <param name="circuitBreaker">Circuit breaker for handling fault tolerance</param>
    /// <param name="metrics">Metrics collector for monitoring performance</param>
    public GarnetClient(
        IOptions<GarnetOptions> options,
        ILogger<GarnetClient> logger,
        IGarnetCircuitBreaker circuitBreaker,
        IGarnetMetrics metrics)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _locks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _memoryStreamPool = new ObjectPool<MemoryStream>(() => new MemoryStream(), 50);
        _stopwatch = new Stopwatch();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        ConfigurationOptions configOptions = ConfigurationOptions.Parse(_options.Value.ConnectionString);
        configOptions.ConnectTimeout = _options.Value.RetryTimeout;
        configOptions.SyncTimeout = _options.Value.RetryTimeout;
        configOptions.AsyncTimeout = _options.Value.RetryTimeout;
        configOptions.ConnectRetry = _options.Value.MaxRetries;
        configOptions.AbortOnConnectFail = false;
        configOptions.AllowAdmin = true;
        configOptions.KeepAlive = 60; // Keep connection alive
        configOptions.ConnectRetry = 3;
        configOptions.ReconnectRetryPolicy = new ExponentialRetry(100);

        _connection = ConnectionMultiplexer.Connect(configOptions);
        _db = _connection.GetDatabase(_options.Value.DatabaseId);

        _connection.ConnectionFailed += (sender, args) =>
        {
            _logger.LogError("Redis connection failed to {Endpoint}. Failure type: {FailureType}",
                args.EndPoint.ToString(), args.FailureType.ToString());
            _metrics.RecordOperation("connection_failed");
        };

        _connection.ConnectionRestored += (sender, args) =>
        {
            _logger.LogInformation("Redis connection restored to {Endpoint}", args.EndPoint.ToString());
            _metrics.RecordOperation("connection_restored");
        };
    }

    /// <summary>
    /// Asynchronously stores a value in the cache with an optional expiration time
    /// </summary>
    /// <typeparam name="T">The type of the value being stored</typeparam>
    /// <param name="key">The key under which to store the value</param>
    /// <param name="value">The value to store</param>
    /// <param name="expiry">Optional expiration timespan</param>
    /// <returns>True if the value was stored successfully</returns>
    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        _stopwatch.Restart();
        using IDisposable _ = _metrics.MeasureOperation("set");

        return await _circuitBreaker.ExecuteAsync(async () =>
        {
            try
            {
                string serialized = JsonSerializer.Serialize(value, _jsonOptions);
                if (_options.Value.EnableCompression)
                {
                    serialized = await CompressAsync(serialized);
                }

                expiry ??= _options.Value.DefaultExpiry;
                bool result = await _db.StringSetAsync(key, serialized, expiry);

                _metrics.RecordOperation("set_success");
                _stopwatch.Stop();
                _logger.LogInformation("Cache SET operation completed for key {Key} in {ElapsedMs}ms. Success: {Result}",
                    key, _stopwatch.ElapsedMilliseconds, result);

                if (_stopwatch.ElapsedMilliseconds > 100)
                {
                    _logger.LogWarning("Cache SET operation for key {Key} took {ElapsedMs}ms which exceeds the warning threshold of 100ms",
                        key, _stopwatch.ElapsedMilliseconds);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting value for key {Key}", key);
                _metrics.RecordOperation("set_error");
                throw;
            }
        });
    }

    /// <summary>
    /// Asynchronously retrieves a value from the cache
    /// </summary>
    /// <typeparam name="T">The type of the value to retrieve</typeparam>
    /// <param name="key">The key of the value to retrieve</param>
    /// <returns>The retrieved value, or default if not found</returns>
    public async Task<T> GetAsync<T>(string key)
    {
        _stopwatch.Restart();
        using IDisposable _ = _metrics.MeasureOperation("get");

        return await _circuitBreaker.ExecuteAsync(async () =>
        {
            try
            {
                RedisValue value = await _db.StringGetAsync(key);
                if (value.IsNull)
                {
                    _metrics.RecordOperation("cache_miss");
                    _stopwatch.Stop();
                    _logger.LogInformation("Cache GET operation completed for key {Key} in {ElapsedMs}ms. Cache miss.",
                        key, _stopwatch.ElapsedMilliseconds);
                    return default;
                }

                _metrics.RecordOperation("cache_hit");
                string stringValue = value.ToString();
                if (_options.Value.EnableCompression)
                {
                    stringValue = await DecompressAsync(stringValue);
                }

                T result = JsonSerializer.Deserialize<T>(stringValue, _jsonOptions);
                _stopwatch.Stop();
                _logger.LogInformation("Cache GET operation completed for key {Key} in {ElapsedMs}ms. Cache hit.",
                    key, _stopwatch.ElapsedMilliseconds);

                if (_stopwatch.ElapsedMilliseconds > 50)
                {
                    _logger.LogWarning("Cache GET operation for key {Key} took {ElapsedMs}ms which exceeds the warning threshold of 50ms",
                        key, _stopwatch.ElapsedMilliseconds);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting value for key {Key}", key);
                _metrics.RecordOperation("get_error");
                throw;
            }
        });
    }

    /// <summary>
    /// Asynchronously deletes a value from the cache
    /// </summary>
    /// <param name="key">The key of the value to delete</param>
    /// <returns>True if the value was deleted successfully</returns>
    public async Task<bool> DeleteAsync(string key)
    {
        _stopwatch.Restart();
        try
        {
            bool result = await _db.KeyDeleteAsync(key);
            _stopwatch.Stop();
            _logger.LogInformation("Cache DELETE operation completed for key {Key} in {ElapsedMs}ms. Success: {Result}",
                key, _stopwatch.ElapsedMilliseconds, result);
            if (result)
            {
                _logger.LogInformation("Key {Key} was manually deleted", key);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting key {Key}", key);
            throw;
        }
    }

    /// <summary>
    /// Asynchronously checks if a key exists in the cache
    /// </summary>
    /// <param name="key">The key to check</param>
    /// <returns>True if the key exists</returns>
    public async Task<bool> ExistsAsync(string key)
    {
        return await _db.KeyExistsAsync(key);
    }

    /// <summary>
    /// Asynchronously gets the remaining time to live for a key
    /// </summary>
    /// <param name="key">The key to check</param>
    /// <returns>The remaining time to live, or null if the key does not exist or has no expiry</returns>
    public async Task<TimeSpan?> GetTimeToLiveAsync(string key)
    {
        return await _db.KeyTimeToLiveAsync(key);
    }

    /// <summary>
    /// Asynchronously sets the expiration time for a key
    /// </summary>
    /// <param name="key">The key to set expiration for</param>
    /// <param name="expiry">The expiration timespan</param>
    /// <returns>True if the expiration was set successfully</returns>
    public async Task<bool> SetExpiryAsync(string key, TimeSpan expiry)
    {
        return await _db.KeyExpireAsync(key, expiry);
    }

    /// <summary>
    /// Asynchronously increments a numeric value in the cache
    /// </summary>
    /// <param name="key">The key of the value to increment</param>
    /// <param name="value">The amount to increment by</param>
    /// <returns>The new value after incrementing</returns>
    public async Task<long> IncrementAsync(string key, long value = 1)
    {
        return await _db.StringIncrementAsync(key, value);
    }

    /// <summary>
    /// Asynchronously decrements a numeric value in the cache
    /// </summary>
    /// <param name="key">The key of the value to decrement</param>
    /// <param name="value">The amount to decrement by</param>
    /// <returns>The new value after decrementing</returns>
    public async Task<long> DecrementAsync(string key, long value = 1)
    {
        return await _db.StringDecrementAsync(key, value);
    }

    /// <summary>
    /// Asynchronously acquires a distributed lock with better performance
    /// </summary>
    /// <param name="key">The key to use as the lock</param>
    /// <param name="expiryTime">The duration after which the lock automatically expires</param>
    /// <returns>True if the lock was acquired successfully</returns>
    public async Task<bool> LockAsync(string key, TimeSpan expiryTime)
    {
        SemaphoreSlim lockInstance = _locks.GetOrAdd(key, k => new SemaphoreSlim(1, 1));
        _stopwatch.Restart();

        try
        {
            if (await lockInstance.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                string lockValue = Guid.NewGuid().ToString();
                bool result = await _db.LockTakeAsync(key, lockValue, expiryTime);
                _stopwatch.Stop();
                _logger.LogInformation("Cache LOCK operation completed for key {Key} in {ElapsedMs}ms. Success: {Result}",
                    key, _stopwatch.ElapsedMilliseconds, result);
                return result;
            }

            _stopwatch.Stop();
            _logger.LogInformation("Cache LOCK operation failed for key {Key} in {ElapsedMs}ms. Timeout waiting for semaphore.",
                key, _stopwatch.ElapsedMilliseconds);
            return false;
        }
        catch (Exception ex)
        {
            _locks.TryRemove(key, out _);
            _logger.LogError(ex, "Error acquiring lock for key {Key}", key);
            throw;
        }
    }

    /// <summary>
    /// Asynchronously releases a distributed lock
    /// </summary>
    /// <param name="key">The key of the lock to release</param>
    /// <returns>True if the lock was released successfully</returns>
    public async Task<bool> UnlockAsync(string key)
    {
        string lockValue = Guid.NewGuid().ToString();
        return await _db.LockReleaseAsync(key, lockValue);
    }

    /// <summary>
    /// Asynchronously scans for keys matching a pattern
    /// </summary>
    /// <param name="pattern">The pattern to match against keys</param>
    /// <returns>An async enumerable of matching keys</returns>
    public async IAsyncEnumerable<string> ScanAsync(string pattern)
    {
        IServer server = _connection.GetServer(_connection.GetEndPoints().First());
        long cursor = 0;
        do
        {
            RedisResult result = await server.ExecuteAsync("SCAN", cursor.ToString(), "MATCH", pattern, "COUNT", "100");
            RedisResult[] innerResult = (RedisResult[])result;
            cursor = long.Parse((string)innerResult[0]);
            RedisKey[] keys = (RedisKey[])innerResult[1];

            foreach (RedisKey key in keys)
            {
                yield return key.ToString();
            }
        }
        while (cursor != 0);
    }

    public async Task<IEnumerable<string>> GetAllKeysAsync(string pattern = "*")
    {
        _stopwatch.Restart();
        List<string> keys = new();

        try
        {
            await foreach (string key in ScanAsync(pattern))
            {
                keys.Add(key);
            }

            _stopwatch.Stop();
            _logger.LogInformation("Retrieved {KeyCount} keys matching pattern {Pattern} in {ElapsedMs}ms",
                keys.Count, pattern, _stopwatch.ElapsedMilliseconds);
            return keys;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all keys with pattern {Pattern}", pattern);
            throw;
        }
    }

    public async Task<IDictionary<string, T>> GetAllAsync<T>(IEnumerable<string> keys)
    {
        _stopwatch.Restart();
        Dictionary<string, T> result = new();
        int failureCount = 0;

        foreach (string key in keys)
        {
            try
            {
                T value = await GetAsync<T>(key);
                if (value != null)
                {
                    result[key] = value;
                }
            }
            catch
            {
                failureCount++;
            }
        }

        _stopwatch.Stop();
        _logger.LogInformation("Batch GET operation completed. Total: {TotalKeys}, Success: {SuccessCount}, Failed: {FailureCount}, Time: {ElapsedMs}ms",
            keys.Count(), result.Count, failureCount, _stopwatch.ElapsedMilliseconds);
        return result;
    }

    private async Task<string> DecompressAsync(string compressedData)
    {
        using var memoryStream = new MemoryStream();
        try
        {
            byte[] data = Convert.FromBase64String(compressedData);
            await memoryStream.WriteAsync(data, 0, data.Length);
            memoryStream.Position = 0;

            using GZipStream gzipStream = new(memoryStream, CompressionMode.Decompress);
            using StreamReader reader = new(gzipStream);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decompressing data");
            throw;
        }
    }

    private async Task<string> CompressAsync(string data)
    {
        using var memoryStream = new MemoryStream();
        try
        {
            using (GZipStream gzipStream = new(memoryStream, CompressionLevel.Optimal, true))
            using (StreamWriter writer = new(gzipStream))
            {
                await writer.WriteAsync(data);
            }
            return Convert.ToBase64String(memoryStream.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compressing data");
            throw;
        }
    }

    public void Dispose()
    {
        foreach (SemaphoreSlim lockItem in _locks.Values)
        {
            lockItem.Dispose();
        }
        _connection?.Dispose();
    }
}

/// <summary>
/// Simple object pool implementation for reusing objects
/// </summary>
internal class ObjectPool<T>
{
    private readonly ConcurrentBag<T> _objects;
    private readonly Func<T> _objectGenerator;
    private readonly int _maxSize;

    public ObjectPool(Func<T> objectGenerator, int maxSize)
    {
        _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
        _maxSize = maxSize;
        _objects = new ConcurrentBag<T>();
    }

    public T Get()
    {
        return _objects.TryTake(out T item) ? item : _objectGenerator();
    }

    public void Return(T item)
    {
        if (_objects.Count < _maxSize)
        {
            _objects.Add(item);
        }
    }
}