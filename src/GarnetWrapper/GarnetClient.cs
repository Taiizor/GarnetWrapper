namespace GarnetWrapper;
using GarnetWrapper.Interfaces;
using GarnetWrapper.Metrics;
using GarnetWrapper.Options;
using GarnetWrapper.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

public class GarnetClient : IGarnetClient
{
    private readonly ConnectionMultiplexer _connection;
    private readonly IDatabase _db;
    private readonly ILogger<GarnetClient> _logger;
    private readonly GarnetCircuitBreaker _circuitBreaker;
    private readonly GarnetMetrics _metrics;
    private readonly GarnetOptions _options;

    public GarnetClient(
        IOptions<GarnetOptions> options,
        ILogger<GarnetClient> logger,
        GarnetCircuitBreaker circuitBreaker,
        GarnetMetrics metrics)
    {
        _options = options.Value;
        _logger = logger;
        _circuitBreaker = circuitBreaker;
        _metrics = metrics;

        ConfigurationOptions configOptions = ConfigurationOptions.Parse(_options.ConnectionString);
        configOptions.ConnectTimeout = _options.RetryTimeout;
        configOptions.SyncTimeout = _options.RetryTimeout;
        configOptions.AsyncTimeout = _options.RetryTimeout;
        configOptions.ConnectRetry = _options.MaxRetries;
        configOptions.AbortOnConnectFail = false;

        _connection = ConnectionMultiplexer.Connect(configOptions);
        _db = _connection.GetDatabase(_options.DatabaseId);
    }

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        using IDisposable _ = _metrics.MeasureOperation("set");
        return await _circuitBreaker.ExecuteAsync(async () =>
        {
            try
            {
                string serialized = JsonSerializer.Serialize(value);
                if (_options.EnableCompression)
                {
                    serialized = await CompressAsync(serialized);
                }

                expiry ??= _options.DefaultExpiry;
                bool result = await _db.StringSetAsync(key, serialized, expiry);
                _metrics.RecordOperation("set_success");
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

    public async Task<T> GetAsync<T>(string key)
    {
        using IDisposable _ = _metrics.MeasureOperation("get");
        return await _circuitBreaker.ExecuteAsync(async () =>
        {
            try
            {
                RedisValue value = await _db.StringGetAsync(key);
                if (value.IsNull)
                {
                    return default;
                }

                string stringValue = value.ToString();
                if (_options.EnableCompression)
                {
                    stringValue = await DecompressAsync(stringValue);
                }

                return JsonSerializer.Deserialize<T>(stringValue, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting value for key {Key}", key);
                _metrics.RecordOperation("get_error");
                throw;
            }
        });
    }

    public async Task<bool> DeleteAsync(string key)
    {
        return await _db.KeyDeleteAsync(key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        return await _db.KeyExistsAsync(key);
    }

    public async Task<TimeSpan?> GetTimeToLiveAsync(string key)
    {
        return await _db.KeyTimeToLiveAsync(key);
    }

    public async Task<bool> SetExpiryAsync(string key, TimeSpan expiry)
    {
        return await _db.KeyExpireAsync(key, expiry);
    }

    public async Task<long> IncrementAsync(string key, long value = 1)
    {
        return await _db.StringIncrementAsync(key, value);
    }

    public async Task<long> DecrementAsync(string key, long value = 1)
    {
        return await _db.StringDecrementAsync(key, value);
    }

    public async Task<bool> LockAsync(string key, TimeSpan expiryTime)
    {
        string lockValue = Guid.NewGuid().ToString();
        return await _db.LockTakeAsync(key, lockValue, expiryTime);
    }

    public async Task<bool> UnlockAsync(string key)
    {
        string lockValue = Guid.NewGuid().ToString();
        return await _db.LockReleaseAsync(key, lockValue);
    }

    public async IAsyncEnumerable<string> ScanAsync(string pattern)
    {
        IServer server = _connection.GetServer(_connection.GetEndPoints().First());
        await foreach (RedisKey key in server.KeysAsync(pattern: pattern))
        {
            yield return key.ToString();
        }
    }

    private async Task<string> CompressAsync(string data)
    {
        using MemoryStream memoryStream = new();
        using (GZipStream gzipStream = new(memoryStream, CompressionLevel.Optimal))
        using (StreamWriter writer = new(gzipStream))
        {
            await writer.WriteAsync(data);
        }
        return Convert.ToBase64String(memoryStream.ToArray());
    }

    private async Task<string> DecompressAsync(string compressedData)
    {
        byte[] data = Convert.FromBase64String(compressedData);
        using MemoryStream memoryStream = new(data);
        using GZipStream gzipStream = new(memoryStream, CompressionMode.Decompress);
        using StreamReader reader = new(gzipStream);
        return await reader.ReadToEndAsync();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}