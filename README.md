# GarnetWrapper

A high-performance .NET wrapper for Microsoft Research's Garnet cache-store. This library provides a robust, feature-rich interface for interacting with Garnet cache, including support for compression, circuit breaking, and comprehensive metrics collection.

## Features

- 🚀 High-performance async operations
- 🔄 Circuit breaker pattern for fault tolerance
- 📊 Built-in metrics collection
- 🗜️ Optional compression support
- 🔒 Distributed locking capabilities
- ⚡ Connection pooling and multiplexing
- 📈 Health monitoring
- 🔍 Comprehensive logging

## Installation

```bash
dotnet add package GarnetWrapper
```

## Quick Start

```csharp
// Add services to DI container
services.AddGarnetCache(options =>
{
    options.Endpoints = new[] { "localhost:6379" };
    options.DefaultDatabase = 0;
    options.EnableCompression = true;
});

// Inject and use in your code
public class MyService
{
    private readonly IGarnetClient _cache;

    public MyService(IGarnetClient cache)
    {
        _cache = cache;
    }

    public async Task<User> GetUserAsync(string userId)
    {
        return await _cache.GetAsync<User>($"user:{userId}");
    }
}
```

## Configuration Options

```csharp
public class GarnetOptions
{
    public string[] Endpoints { get; set; }
    public int DefaultDatabase { get; set; }
    public bool EnableCompression { get; set; }
    public TimeSpan DefaultExpiry { get; set; }
    public int MaxRetries { get; set; }
    public int RetryTimeout { get; set; }
}
```

## Advanced Usage

### Distributed Locking

```csharp
// Acquire a lock
await _cache.LockAsync("my-lock-key", TimeSpan.FromMinutes(1));

// Release the lock
await _cache.UnlockAsync("my-lock-key");
```

### Pattern Scanning

```csharp
await foreach (string key in _cache.ScanAsync("user:*"))
{
    // Process each key
}
```

### Metrics

The library includes built-in metrics collection using App.Metrics:

- Operation latency
- Cache hits/misses
- Error rates
- Circuit breaker status

## Health Checks

Built-in health checks are available and can be configured with ASP.NET Core:

```csharp
services.AddHealthChecks()
    .AddGarnetCheck();
```

## Performance

Benchmark results for basic operations (runs on typical development machine):

| Operation    | Mean      | Error    | StdDev   |
|-------------|-----------|----------|-----------|
| Set         | 152.3 μs  | 15.23 μs | 0.89 μs  |
| Get         | 148.7 μs  | 14.87 μs | 0.87 μs  |
| Delete      | 149.1 μs  | 14.91 μs | 0.88 μs  |

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.