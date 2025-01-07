using App.Metrics;
using App.Metrics.Counter;
using App.Metrics.Timer;

namespace GarnetWrapper.Metrics;

public interface IGarnetMetrics
{
    IDisposable MeasureOperation(string operationName);
    void RecordOperation(string operationName);
}

public class GarnetMetrics : IGarnetMetrics
{
    private readonly IMetrics _metrics;
    private readonly TimerOptions _operationTimer;

    public GarnetMetrics()
    {
        _metrics = new MetricsBuilder()
            .Configuration.Configure(options =>
            {
                options.AddAppTag("GarnetWrapper");
            })
            .Build();

        _operationTimer = new TimerOptions
        {
            Name = "Garnet Operation Timer",
            MeasurementUnit = Unit.Calls,
            DurationUnit = TimeUnit.Milliseconds,
            RateUnit = TimeUnit.Seconds
        };
    }

    public virtual IDisposable MeasureOperation(string operationName)
    {
        TimerOptions timerOptions = new()
        {
            Name = _operationTimer.Name,
            MeasurementUnit = _operationTimer.MeasurementUnit,
            DurationUnit = _operationTimer.DurationUnit,
            RateUnit = _operationTimer.RateUnit,
            Tags = new MetricTags("operation", operationName)
        };
        return _metrics.Measure.Timer.Time(timerOptions);
    }

    public virtual void RecordOperation(string operationName)
    {
        _metrics.Measure.Counter.Increment(new CounterOptions
        {
            Name = "Garnet Operation Counter",
            MeasurementUnit = Unit.Calls,
            Tags = new MetricTags("operation", operationName)
        });
    }
}