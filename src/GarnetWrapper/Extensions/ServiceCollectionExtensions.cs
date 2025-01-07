using GarnetWrapper.Health;
using GarnetWrapper.Interfaces;
using GarnetWrapper.Metrics;
using GarnetWrapper.Options;
using GarnetWrapper.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GarnetWrapper.Extensions
{
    /// <summary>
    /// Extension methods for IServiceCollection to register GarnetWrapper services
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds GarnetWrapper services to the specified IServiceCollection
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to</param>
        /// <param name="configureOptions">Action to configure GarnetOptions</param>
        /// <returns>The IServiceCollection for chaining</returns>
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

        /// <summary>
        /// Adds GarnetWrapper services to the specified IServiceCollection using configuration section
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to</param>
        /// <param name="configuration">The configuration section containing GarnetOptions</param>
        /// <returns>The IServiceCollection for chaining</returns>
        public static IServiceCollection AddGarnetCache(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<GarnetOptions>(configuration);

            // Register dependencies
            services.AddSingleton<GarnetMetrics>();
            services.AddSingleton<GarnetCircuitBreaker>();
            services.AddSingleton<IGarnetClient, GarnetClient>();

            return services;
        }
    }
}