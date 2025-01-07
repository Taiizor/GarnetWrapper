namespace GarnetWrapper;
using GarnetWrapper.Interfaces;
using GarnetWrapper.Logging;
using GarnetWrapper.Metrics;
using GarnetWrapper.Options;
using GarnetWrapper.Resilience;
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
    private readonly GarnetLogger _logger;
    private readonly IGarnetCircuitBreaker _circuitBreaker;
    private readonly GarnetMetrics _metrics;
    private readonly GarnetOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks;
    private readonly ObjectPool<MemoryStream> _memoryStreamPool;
    private readonly Stopwatch _stopwatch;

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
        GarnetMetrics metrics)
    {
        _options = options.Value;
        _logger = new GarnetLogger(logger);
        _circuitBreaker = circuitBreaker;
        _metrics = metrics;
        _locks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _memoryStreamPool = new ObjectPool<MemoryStream>(() => new MemoryStream(), 50);
        _stopwatch = new Stopwatch();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        ConfigurationOptions configOptions = ConfigurationOptions.Parse(_options.ConnectionString);
        configOptions.ConnectTimeout = _options.RetryTimeout;
        configOptions.SyncTimeout = _options.RetryTimeout;
        configOptions.AsyncTimeout = _options.RetryTimeout;
        configOptions.ConnectRetry = _options.MaxRetries;
        configOptions.AbortOnConnectFail = false;
        configOptions.AllowAdmin = true;
        configOptions.KeepAlive = 60; // Keep connection alive
        configOptions.ConnectRetry = 3;
        configOptions.ReconnectRetryPolicy = new ExponentialRetry(100);

        _connection = ConnectionMultiplexer.Connect(configOptions);
        _db = _connection.GetDatabase(_options.DatabaseId);

        _connection.ConnectionFailed += (sender, args) =>
        {
            _logger.LogConnectionState(args.EndPoint.ToString(), "Failed", args.FailureType.ToString());
            _metrics.RecordOperation("connection_failed");
        };

        _connection.ConnectionRestored += (sender, args) =>
        {
            _logger.LogConnectionState(args.EndPoint.ToString(), "Connected");
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
                if (_options.EnableCompression)
                {
                    serialized = await CompressAsync(serialized);
                }

                expiry ??= _options.DefaultExpiry;
                bool result = await _db.StringSetAsync(key, serialized, expiry);

                _metrics.RecordOperation("set_success");
                _stopwatch.Stop();
                _logger.LogCacheOperation("Set", key, result, _stopwatch.ElapsedMilliseconds);
                _logger.LogPerformanceWarning("Set", key, _stopwatch.ElapsedMilliseconds, 100);

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
                    _logger.LogCacheOperation("Get", key, false, _stopwatch.ElapsedMilliseconds);
                    return default;
                }

                _metrics.RecordOperation("cache_hit");
                string stringValue = value.ToString();
                if (_options.EnableCompression)
                {
                    stringValue = await DecompressAsync(stringValue);
                }

                T result = JsonSerializer.Deserialize<T>(stringValue, _jsonOptions);
                _stopwatch.Stop();
                _logger.LogCacheOperation("Get", key, true, _stopwatch.ElapsedMilliseconds);
                _logger.LogPerformanceWarning("Get", key, _stopwatch.ElapsedMilliseconds, 50);

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
            _logger.LogCacheOperation("Delete", key, result, _stopwatch.ElapsedMilliseconds);
            if (result)
            {
                _logger.LogEviction(key, "Manual deletion");
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
                _logger.LogCacheOperation("Lock", key, result, _stopwatch.ElapsedMilliseconds);
                return result;
            }

            _stopwatch.Stop();
            _logger.LogCacheOperation("Lock", key, false, _stopwatch.ElapsedMilliseconds);
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
        await foreach (RedisKey key in server.KeysAsync(pattern: pattern))
        {
            yield return key.ToString();
        }
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
            _logger.LogBatchOperation("GetAllKeys", keys.Count, keys.Count, 0, _stopwatch.ElapsedMilliseconds);
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
        _logger.LogBatchOperation("GetAll", keys.Count(), result.Count, failureCount, _stopwatch.ElapsedMilliseconds);
        return result;
    }

    private async Task<string> CompressAsync(string data)
    {
        MemoryStream memoryStream = _memoryStreamPool.Get();
        try
        {
            memoryStream.SetLength(0);
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
        finally
        {
            _memoryStreamPool.Return(memoryStream);
        }
    }

    private async Task<string> DecompressAsync(string compressedData)
    {
        MemoryStream memoryStream = _memoryStreamPool.Get();
        try
        {
            memoryStream.SetLength(0);
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
        finally
        {
            _memoryStreamPool.Return(memoryStream);
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