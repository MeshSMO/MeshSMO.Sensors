using System.Data.Common;
using System.Globalization;
using MeshSMO.Sensors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeshSMO.Sensors.Web.Api.MeasurementHistory;

public sealed record MeasurementHistoryPoint(
    DateTimeOffset Timestamp,
    double Min,
    double Avg,
    double Max,
    int Count);

/// <summary>
/// Reads one numeric metric series of one sensor, either raw or aggregated
/// into fixed UTC-aligned buckets (min/avg/max/count, spec §15). The bucket
/// expression is written twice on purpose: PostgreSQL uses date_bin, while
/// SQLite (unit tests) buckets via unixepoch seconds. Bucket sizes come only
/// from the validated resolution whitelist, so they are inlined as literals;
/// sensor id, metric key and bounds go through real SQL parameters.
/// </summary>
public sealed class MeasurementHistoryReader(SensorsDbContext dbContext)
{
    public async Task<IReadOnlyList<MeasurementHistoryPoint>> ReadAsync(
        Guid sensorId,
        string metricKey,
        DateTimeOffset from,
        DateTimeOffset to,
        MeasurementResolution resolution,
        CancellationToken cancellationToken)
    {
        var useNpgsql = string.Equals(
            dbContext.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.OrdinalIgnoreCase);

        var epochExpression = resolution == MeasurementResolution.Raw
            ? EpochExpression(useNpgsql)
            : BucketedEpochExpression(useNpgsql, MeasurementResolutionPolicy.BucketSeconds(resolution));
        var countExpression = useNpgsql ? "count(*)::int" : "count(*)";
        var sql = $"""
            SELECT {epochExpression} AS "EpochSeconds",
                   min(v.numeric_value) AS "Min",
                   avg(v.numeric_value) AS "Avg",
                   max(v.numeric_value) AS "Max",
                   {countExpression} AS "Count"
            FROM measurement_values AS v
            WHERE v.sensor_id = @sensorId
              AND v.metric_key = @metric
              AND v.numeric_value IS NOT NULL
              AND v.timestamp >= @from
              AND v.timestamp < @to
            GROUP BY {epochExpression}
            ORDER BY "EpochSeconds"
            """;

        var rows = await dbContext.Database
            .SqlQueryRaw<PointRow>(
                sql,
                Parameter("@sensorId", sensorId),
                Parameter("@metric", metricKey),
                Parameter("@from", from.ToUniversalTime()),
                Parameter("@to", to.ToUniversalTime()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => new MeasurementHistoryPoint(
                DateTimeOffset.FromUnixTimeSeconds(row.EpochSeconds),
                row.Min,
                row.Avg,
                row.Max,
                row.Count))
            .ToArray();
    }

    private static string EpochExpression(bool useNpgsql) => useNpgsql
        ? "CAST(extract(epoch from v.timestamp) AS bigint)"
        : "CAST(strftime('%s', v.timestamp) AS INTEGER)";

    private static string BucketedEpochExpression(bool useNpgsql, int bucketSeconds) => useNpgsql
        ? FormattableString.Invariant(
            $"CAST(extract(epoch from date_bin(make_interval(secs => {bucketSeconds}), v.timestamp, TIMESTAMPTZ '1970-01-01 00:00:00+00')) AS bigint)")
        : FormattableString.Invariant(
            $"(CAST(strftime('%s', v.timestamp) AS INTEGER) / {bucketSeconds}) * {bucketSeconds}");

    private DbParameter Parameter(string name, object value)
    {
        var parameter = dbContext.Database.GetDbConnection().CreateCommand().CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }

    private sealed record PointRow(long EpochSeconds, double Min, double Avg, double Max, int Count);
}
