using MeshSMO.Sensors.Forecasting.Evaluation;

namespace MeshSMO.Sensors.Forecasting.MlNet;

internal sealed record ForecastCandidateEvaluation(
    string ModelKind,
    int? WindowSize,
    ForecastMetricValues Metrics,
    double Mase,
    IReadOnlyList<double> LowerResiduals,
    IReadOnlyList<double> UpperResiduals);
