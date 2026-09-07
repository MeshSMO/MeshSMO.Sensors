namespace MeshSMO.Sensors.Forecasting;

public sealed record ForecastDiagnostics(
    string ModelKind,
    int WindowSize,
    int TrainingPoints,
    DateTimeOffset TrainingFrom,
    DateTimeOffset TrainingTo,
    double ObservedCoverage,
    int InterpolatedPoints,
    double Mae,
    double Rmse,
    double Mase,
    double IntervalCoverage,
    double ConfidenceLevel);
