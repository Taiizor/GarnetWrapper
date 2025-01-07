using GarnetWrapper.Health;
using GarnetWrapper.Interfaces;
using GarnetWrapper.Metrics;
using GarnetWrapper.Options;
using GarnetWrapper.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GarnetWrapper.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGarnet(this IServiceCollection services, Action<GarnetOptions> configureOptions)
        {
            services.Configure(configureOptions);

            services.AddSingleton<IGarnetClient, GarnetClient>();
            services.AddSingleton<GarnetCircuitBreaker>();
            services.AddSingleton<GarnetMetrics>();

            services.AddHealthChecks()
                .AddCheck<GarnetHealthCheck>("garnet");

            services.AddLogging(builder =>
            {
                builder.AddDebug();
                builder.AddConsole();
            });

            return services;
        }
    }
}