# GarnetWrapper Sample Application

This sample application demonstrates various usage scenarios and features of the GarnetWrapper library.

## Features Demonstrated

1. **Basic Cache Operations**
   - Setting and retrieving values
   - Working with complex objects
   - Key expiration
   - Pattern scanning

2. **Distributed Locking**
   - Acquiring and releasing locks
   - Handling concurrent operations
   - Race condition prevention

3. **Metrics and Monitoring**
   - Cache hit/miss rates
   - Operation latency tracking
   - Performance monitoring

## Prerequisites

- .NET 9.0 SDK
- Redis server running on localhost:6379 (or update connection string in appsettings.json)

## Running the Sample

1. Start Redis server
2. Run the application:
   ```bash
   dotnet run
   ```

## Code Examples

### Basic Cache Operations

```csharp
// Store a value
await _cache.SetAsync("user:1", new User { Id = 1, Name = "John" });

// Retrieve a value
var user = await _cache.GetAsync<User>("user:1");

// Set with expiration
await _cache.SetAsync("temp:key", "value", TimeSpan.FromMinutes(5));
```

### Distributed Locking

```csharp
if (await _cache.LockAsync("my-lock", TimeSpan.FromSeconds(30)))
{
    try
    {
        // Perform synchronized operation
        await _cache.IncrementAsync("counter");
    }
    finally
    {
        await _cache.UnlockAsync("my-lock");
    }
}
```

### Pattern Scanning

```csharp
await foreach (var key in _cache.ScanAsync("user:*"))
{
    var user = await _cache.GetAsync<User>(key);
    Console.WriteLine($"Found user: {user.Name}");
}
```

## Configuration

The sample uses the following configuration structure in appsettings.json:

```json
{
  "Garnet": {
    "ConnectionString": "localhost:6379",
    "DefaultExpiry": "01:00:00",
    "EnableCompression": true,
    "RetryTimeout": 5000,
    "DatabaseId": 0,
    "MaxRetries": 3
  }
}
```

## Features in Detail

### 1. Cache Operations
- Automatic serialization/deserialization
- Optional compression for large values
- Configurable default expiration
- Batch operations support

### 2. Resilience
- Circuit breaker pattern
- Automatic retries
- Connection pooling
- Error rate monitoring

### 3. Monitoring
- Operation latency tracking
- Cache hit/miss rates
- Memory usage statistics
- Performance warnings

### 4. Distributed Locking
- Atomic operations
- Automatic lock release
- Deadlock prevention
- Lock timeout handling

## Best Practices Demonstrated

1. **Error Handling**
   - Circuit breaker for fault tolerance
   - Graceful degradation
   - Comprehensive logging

2. **Performance**
   - Connection pooling
   - Batch operations
   - Compression for large values

3. **Monitoring**
   - Structured logging
   - Performance metrics
   - Health checks

4. **Security**
   - Rate limiting
   - Resource cleanup
   - Safe concurrent access

## Additional Resources

- [GarnetWrapper Documentation](../../README.md)
- [Redis Documentation](https://redis.io/documentation)
- [.NET Documentation](https://docs.microsoft.com/en-us/dotnet/) 