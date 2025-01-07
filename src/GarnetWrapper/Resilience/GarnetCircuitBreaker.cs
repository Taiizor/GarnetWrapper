using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;

namespace GarnetWrapper.Resilience
{
    public class GarnetCircuitBreaker
    {
        private readonly ILogger<GarnetCircuitBreaker> _logger;
        private readonly AsyncCircuitBreakerPolicy _circuitBreaker;

        public GarnetCircuitBreaker(ILogger<GarnetCircuitBreaker> logger)
        {
            _logger = logger;
            _circuitBreaker = Policy
                .Handle<RedisException>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 3,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (ex, duration) =>
                    {
                        _logger.LogError(ex, "Circuit breaker opened for {Duration}s", duration.TotalSeconds);
                    },
                    onReset: () =>
                    {
                        _logger.LogInformation("Circuit breaker reset");
                    });
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
        {
            return await _circuitBreaker.ExecuteAsync(action);
        }
    }
}