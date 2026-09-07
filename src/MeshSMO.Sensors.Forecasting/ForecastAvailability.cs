namespace MeshSMO.Sensors.Forecasting;

public enum ForecastAvailability
{
    Ready,
    InsufficientData,
    SparseData,
    StaleData,
    LowQuality,
    Disabled,
}
