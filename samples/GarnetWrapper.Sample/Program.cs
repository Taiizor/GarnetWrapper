using GarnetWrapper.Extensions;
using GarnetWrapper.Interfaces;

IHostBuilder builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        // Add Garnet services
        services.AddGarnet(options =>
        {
            options.DefaultExpiry = TimeSpan.FromMinutes(30);
            options.ConnectionString = "localhost:6379";
            options.EnableCompression = true;
            options.RetryTimeout = 1000;
            options.MaxRetries = 3;
            options.DatabaseId = 0;
        });

        // Add sample services
        services.AddHostedService<CacheExampleService>();
        services.AddHostedService<DistributedLockExampleService>();
        services.AddHostedService<MetricsExampleService>();
    })
    .ConfigureLogging((hostContext, logging) =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.AddDebug();
    });

await builder.RunConsoleAsync();

public class CacheExampleService : BackgroundService
{
    private readonly IGarnetClient _cache;
    private readonly ILogger<CacheExampleService> _logger;

    public CacheExampleService(IGarnetClient cache, ILogger<CacheExampleService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Basic cache operations
            await _cache.SetAsync("user:1", new User { Id = 1, Name = "John Doe", Email = "john@example.com" });
            User user = await _cache.GetAsync<User>("user:1");
            _logger.LogInformation("Retrieved user: {Name}", user?.Name);

            // Batch operations
            List<User> users = Enumerable.Range(1, 10)
                .Select(i => new User { Id = i, Name = $"User {i}", Email = $"user{i}@example.com" })
                .ToList();

            foreach (User? u in users)
            {
                await _cache.SetAsync($"user:{u.Id}", u);
            }

            // Pattern scanning
            await foreach (string? key in _cache.ScanAsync("user:*"))
            {
                User cachedUser = await _cache.GetAsync<User>(key);
                _logger.LogInformation("Found user: {Name}", cachedUser?.Name);
            }

            // Expiration
            await _cache.SetAsync("temp:key", "temporary value", TimeSpan.FromSeconds(5));
            bool exists = await _cache.ExistsAsync("temp:key");
            _logger.LogInformation("Temporary key exists: {Exists}", exists);

            await Task.Delay(6000); // Wait for expiration
            exists = await _cache.ExistsAsync("temp:key");
            _logger.LogInformation("Temporary key exists after expiration: {Exists}", exists);

            // Counter operations
            await _cache.SetAsync("counter", "0");
            for (int i = 0; i < 5; i++)
            {
                long value = await _cache.IncrementAsync("counter");
                _logger.LogInformation("Counter value: {Value}", value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in cache example service");
        }
    }
}

public class DistributedLockExampleService : BackgroundService
{
    private readonly IGarnetClient _cache;
    private readonly ILogger<DistributedLockExampleService> _logger;

    public DistributedLockExampleService(IGarnetClient cache, ILogger<DistributedLockExampleService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            const string lockKey = "example:lock";
            const string counterKey = "example:counter";

            // Initialize counter
            await _cache.SetAsync(counterKey, 0);

            // Simulate multiple processes trying to increment the counter
            List<Task> tasks = new();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < 10; j++)
                    {
                        if (await _cache.LockAsync(lockKey, TimeSpan.FromSeconds(5)))
                        {
                            try
                            {
                                // Simulate some work
                                await Task.Delay(100);
                                await _cache.IncrementAsync(counterKey);
                            }
                            finally
                            {
                                await _cache.UnlockAsync(lockKey);
                            }
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);

            int finalCount = await _cache.GetAsync<int>(counterKey);
            _logger.LogInformation("Final counter value with distributed lock: {Value}", finalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in distributed lock example service");
        }
    }
}

public class MetricsExampleService : BackgroundService
{
    private readonly IGarnetClient _cache;
    private readonly ILogger<MetricsExampleService> _logger;
    private readonly Random _random = new();

    public MetricsExampleService(IGarnetClient cache, ILogger<MetricsExampleService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Simulate random cache operations
                string key = $"metrics:key:{_random.Next(1, 11)}";
                string value = $"value-{Guid.NewGuid()}";

                // Mix of hits and misses
                if (_random.Next(2) == 0)
                {
                    await _cache.SetAsync(key, value);
                }
                else
                {
                    await _cache.GetAsync<string>(key);
                }

                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in metrics example service");
        }
    }
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}