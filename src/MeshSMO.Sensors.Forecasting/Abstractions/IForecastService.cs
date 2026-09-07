using MeshSMO.Sensors.Forecasting.Models;

namespace MeshSMO.Sensors.Forecasting.Abstractions;

public interface IForecastService
{
    ForecastResult Forecast(
        ForecastSeries series,
        TimeSpan horizon,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default);
}
