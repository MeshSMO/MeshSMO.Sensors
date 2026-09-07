namespace MeshSMO.Sensors.Forecasting;

public interface IForecastService
{
    ForecastResult Forecast(
        ForecastSeries series,
        TimeSpan horizon,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default);
}
