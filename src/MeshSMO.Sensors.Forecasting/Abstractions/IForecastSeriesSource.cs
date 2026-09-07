using MeshSMO.Sensors.Forecasting.Models;

namespace MeshSMO.Sensors.Forecasting.Abstractions;

public interface IForecastSeriesSource
{
    Task<ForecastSeries> ReadAsync(
        ForecastSeriesQuery query,
        CancellationToken cancellationToken = default);
}
