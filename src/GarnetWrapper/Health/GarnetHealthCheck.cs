using GarnetWrapper.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace GarnetWrapper.Health
{
    public class GarnetHealthCheck : IHealthCheck
    {
        private readonly IGarnetClient _client;
        private readonly ILogger<GarnetHealthCheck> _logger;
        private const string TestKey = "health_check";

        public GarnetHealthCheck(IGarnetClient client, ILogger<GarnetHealthCheck> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                HealthCheckData testData = new() { Status = "test" };
                await _client.SetAsync(TestKey, testData, TimeSpan.FromSeconds(5));
                HealthCheckData result = await _client.GetAsync<HealthCheckData>(TestKey);

                return result?.Status == "test"
                    ? HealthCheckResult.Healthy("Garnet connection is healthy")
                    : HealthCheckResult.Degraded("Garnet connection is degraded");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Garnet health check failed");
                return HealthCheckResult.Unhealthy(ex.Message);
            }
        }
    }

    public class HealthCheckData
    {
        public string Status { get; set; }
    }
}