using Microsoft.ML.Data;

namespace MeshSMO.Sensors.Forecasting.MlNet;

internal sealed class SsaModelOutput
{
    [VectorType]
    public float[] Forecast { get; init; } = [];

    [VectorType]
    public float[] Lower { get; init; } = [];

    [VectorType]
    public float[] Upper { get; init; } = [];
}
