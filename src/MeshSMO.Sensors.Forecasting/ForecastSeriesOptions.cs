namespace MeshSMO.Sensors.Forecasting;

public sealed class ForecastSeriesOptions
{
    public bool Enabled { get; set; } = true;
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public double? MaximumMae { get; set; }
}
