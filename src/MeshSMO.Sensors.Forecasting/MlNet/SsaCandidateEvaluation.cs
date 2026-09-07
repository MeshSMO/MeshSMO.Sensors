using MeshSMO.Sensors.Forecasting.Evaluation;

namespace MeshSMO.Sensors.Forecasting.MlNet;

internal sealed record SsaCandidateEvaluation(
    int WindowSize,
    ForecastMetricValues Metrics,
    double Mase);
