using System.Data.Common;
using System.Globalization;
using MeshSMO.Sensors.Forecasting;
using MeshSMO.Sensors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeshSMO.Sensors.Web.Api.Forecasting;

public sealed class PostgresForecastSeriesSource(SensorsDbContext dbContext) : IForecastSeriesSource
{
    public async Task<ForecastSeries> ReadAsync(
        ForecastSeriesQuery query,
        CancellationToken cancellationToken = default)
    {
        var useNpgsql = string.Equals(
            dbContext.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.OrdinalIgnoreCase);
        var stepSeconds = checked((int)query.Step.TotalSeconds);
        var bucketExpression = useNpgsql
            ? FormattableString.Invariant(
                $"CAST(extract(epoch from date_bin(make_interval(secs => {stepSeconds}), v.timestamp, TIMESTAMPTZ '1970-01-01 00:00:00+00')) AS bigint)")
            : FormattableString.Invariant(
                $"(CAST(strftime('%s', v.timestamp) AS INTEGER) / {stepSeconds}) * {stepSeconds}");
        var observationEpochExpression = useNpgsql
            ? "CAST(extract(epoch from max(v.timestamp)) AS bigint)"
            : "CAST(strftime('%s', max(v.timestamp)) AS INTEGER)";
        var sql = $"""
            SELECT {bucketExpression} AS "EpochSeconds",
                   avg(v.numeric_value) AS "Value",
                   {observationEpochExpression} AS "LastObservationEpochSeconds"
            FROM measurement_values AS v
            WHERE v.sensor_id = @sensorId
              AND v.metric_key = @metric
              AND v.numeric_value IS NOT NULL
              AND v.timestamp >= @from
              AND v.timestamp < @to
            GROUP BY {bucketExpression}
            ORDER BY "EpochSeconds"
            """;
        var rows = await dbContext.Database.SqlQueryRaw<ForecastRow>(
                sql,
                Parameter("@sensorId", query.SensorId),
                Parameter("@metric", query.MetricKey),
                Parameter("@from", query.From.ToUniversalTime()),
                Parameter("@to", query.To.ToUniversalTime()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var lastObservationAt = rows.Count == 0
            ? (DateTimeOffset?)null
            : DateTimeOffset.FromUnixTimeSeconds(rows.Max(static row => row.LastObservationEpochSeconds));
        var observations = rows
            .Select(static row => new ForecastObservation(
                DateTimeOffset.FromUnixTimeSeconds(row.EpochSeconds),
                row.Value))
            .ToArray();
        return new(
            query.SensorId,
            query.MetricKey,
            query.Unit,
            query.PollInterval,
            lastObservationAt,
            observations,
            query.Minimum,
            query.Maximum,
            query.MaximumMae);
    }

    private DbParameter Parameter(string name, object value)
    {
        var parameter = dbContext.Database.GetDbConnection().CreateCommand().CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }

    private sealed record ForecastRow(long EpochSeconds, double Value, long LastObservationEpochSeconds);
}
