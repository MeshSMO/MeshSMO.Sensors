namespace MeshSMO.Sensors.Forecasting.Models;

public enum ForecastAvailability
{
    Ready,
    InsufficientData,
    SparseData,
    StaleData,
    LowQuality,
    Disabled,
}
