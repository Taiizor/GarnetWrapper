using App.Metrics;
using App.Metrics.Counter;
using App.Metrics.Timer;

namespace GarnetWrapper.Metrics
{
    public class GarnetMetrics
    {
        private readonly IMetrics _metrics;
        private readonly CounterOptions _operationCounter;
        private readonly TimerOptions _operationTimer;

        public GarnetMetrics()
        {
            _metrics = new MetricsBuilder()
                .Configuration.Configure(options =>
                {
                    options.AddEnvTag();
                    options.AddServerTag();
                })
                .Build();

            _operationCounter = new CounterOptions
            {
                Name = "garnet_operations",
                MeasurementUnit = Unit.Items,
                Context = "Garnet.Operations"
            };

            _operationTimer = new TimerOptions
            {
                Name = "garnet_operation_duration",
                MeasurementUnit = Unit.Items,
                DurationUnit = TimeUnit.Milliseconds,
                RateUnit = TimeUnit.Milliseconds,
                Context = "Garnet.Operations"
            };
        }

        public void RecordOperation(string operation)
        {
            if (string.IsNullOrEmpty(operation))
            {
                return;
            }

            MetricTags tags = new(new[] { "operation" }, new[] { operation });
            _metrics.Measure.Counter.Increment(_operationCounter, tags);
        }

        public IDisposable MeasureOperation(string operation)
        {
            if (string.IsNullOrEmpty(operation))
            {
                return null;
            }

            MetricTags tags = new(new[] { "operation" }, new[] { operation });
            return _metrics.Measure.Timer.Time(_operationTimer, tags);
        }
    }
}