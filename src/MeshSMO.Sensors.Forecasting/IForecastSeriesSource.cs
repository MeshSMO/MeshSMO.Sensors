namespace MeshSMO.Sensors.Forecasting;

public interface IForecastSeriesSource
{
    Task<ForecastSeries> ReadAsync(
        ForecastSeriesQuery query,
        CancellationToken cancellationToken = default);
}
