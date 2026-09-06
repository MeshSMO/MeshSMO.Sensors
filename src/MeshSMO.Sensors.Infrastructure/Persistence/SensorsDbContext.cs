using MeshSMO.Sensors.Domain.Measurements;
using MeshSMO.Sensors.Domain.Polling;
using MeshSMO.Sensors.Domain.Sensors;
using Microsoft.EntityFrameworkCore;

namespace MeshSMO.Sensors.Infrastructure.Persistence;

public sealed class SensorsDbContext(DbContextOptions<SensorsDbContext> options) : DbContext(options)
{
    public DbSet<Sensor> Sensors => Set<Sensor>();
    public DbSet<SensorMetric> SensorMetrics => Set<SensorMetric>();
    public DbSet<MeasurementSample> MeasurementSamples => Set<MeasurementSample>();
    public DbSet<MeasurementValue> MeasurementValues => Set<MeasurementValue>();
    public DbSet<PollAttempt> PollAttempts => Set<PollAttempt>();
    public DbSet<SensorStatusSnapshot> SensorStatuses => Set<SensorStatusSnapshot>();
    public DbSet<GatewayTelemetrySnapshot> GatewayTelemetrySnapshots => Set<GatewayTelemetrySnapshot>();
    public DbSet<GatewayTelemetryReading> GatewayTelemetryReadings => Set<GatewayTelemetryReading>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureSensor(modelBuilder);
        ConfigureMeasurements(modelBuilder);
        ConfigurePollAttempts(modelBuilder);
        ConfigureSensorStatus(modelBuilder);
        ConfigureGatewayTelemetry(modelBuilder);
    }

    private static void ConfigureSensor(ModelBuilder modelBuilder)
    {
        var sensor = modelBuilder.Entity<Sensor>();
        sensor.ToTable("sensors");
        sensor.HasKey(entity => entity.Id);
        sensor.Property(entity => entity.Id).HasColumnName("id").HasConversion(id => id.Value, value => new SensorId(value));
        sensor.Property(entity => entity.Slug).HasColumnName("slug").HasMaxLength(63).HasConversion(slug => slug.Value, value => new SensorSlug(value));
        sensor.Property(entity => entity.DisplayName).HasColumnName("display_name");
        sensor.Property(entity => entity.Description).HasColumnName("description");
        sensor.Property(entity => entity.MeshPublicKey).HasColumnName("mesh_public_key");
        sensor.Property(entity => entity.ProtocolId).HasColumnName("protocol_id");
        sensor.Property(entity => entity.PollIntervalSeconds).HasColumnName("poll_interval_seconds");
        sensor.Property(entity => entity.PollTimeoutSeconds).HasColumnName("poll_timeout_seconds");
        sensor.Property(entity => entity.PollMaxAttempts).HasColumnName("poll_max_attempts");
        sensor.Property(entity => entity.Enabled).HasColumnName("enabled");
        sensor.Property(entity => entity.PublicVisible).HasColumnName("public_visible");
        sensor.Property(entity => entity.PublicIndexable).HasColumnName("public_indexable");
        sensor.Property(entity => entity.Latitude).HasColumnName("latitude");
        sensor.Property(entity => entity.Longitude).HasColumnName("longitude");
        sensor.Property(entity => entity.LocationPrecision).HasColumnName("location_precision");
        sensor.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        sensor.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        sensor.HasIndex(entity => entity.Slug).IsUnique().HasDatabaseName("ux_sensors_slug");
        sensor.HasIndex(entity => entity.MeshPublicKey).IsUnique().HasDatabaseName("ux_sensors_mesh_public_key");

        var metric = modelBuilder.Entity<SensorMetric>();
        metric.ToTable("sensor_metrics");
        metric.HasKey(entity => new { entity.SensorId, entity.MetricKey });
        metric.Property(entity => entity.SensorId).HasColumnName("sensor_id").HasConversion(id => id.Value, value => new SensorId(value));
        metric.Property(entity => entity.MetricKey).HasColumnName("metric_key").HasMaxLength(64);
        metric.Property(entity => entity.DisplayName).HasColumnName("display_name").HasMaxLength(128);
        metric.Property(entity => entity.Unit).HasColumnName("unit").HasMaxLength(16);
        sensor.HasMany(entity => entity.Metrics)
            .WithOne()
            .HasForeignKey(entity => entity.SensorId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureMeasurements(ModelBuilder modelBuilder)
    {
        var sample = modelBuilder.Entity<MeasurementSample>();
        sample.ToTable("measurement_samples");
        sample.HasKey(entity => entity.Id);
        sample.Property(entity => entity.Id).HasColumnName("id");
        sample.Property(entity => entity.SensorId).HasColumnName("sensor_id").HasConversion(id => id.Value, value => new SensorId(value));
        sample.Property(entity => entity.RequestId).HasColumnName("request_id");
        sample.Property(entity => entity.MeasuredAt).HasColumnName("measured_at");
        sample.Property(entity => entity.ReceivedAt).HasColumnName("received_at");
        sample.Property(entity => entity.Rssi).HasColumnName("rssi");
        sample.Property(entity => entity.Snr).HasColumnName("snr");
        sample.Property(entity => entity.RoundTripMilliseconds).HasColumnName("round_trip_ms");
        sample.Property(entity => entity.ProtocolId).HasColumnName("protocol_id");
        sample.Property(entity => entity.RawPayload).HasColumnName("raw_payload");
        sample.Property(entity => entity.Extra).HasColumnName("extra").HasColumnType("jsonb");
        sample.HasIndex(entity => new { entity.SensorId, entity.ReceivedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_measurement_samples_sensor_received");
        sample.HasIndex(entity => new { entity.SensorId, entity.RequestId })
            .IsUnique()
            .HasDatabaseName("ux_measurement_samples_sensor_request");
        sample.HasOne<Sensor>().WithMany().HasForeignKey(entity => entity.SensorId).OnDelete(DeleteBehavior.Restrict);

        var value = modelBuilder.Entity<MeasurementValue>();
        value.ToTable("measurement_values");
        value.HasKey(entity => new { entity.SampleId, entity.MetricKey });
        value.Property(entity => entity.SampleId).HasColumnName("sample_id");
        value.Property(entity => entity.SensorId).HasColumnName("sensor_id").HasConversion(id => id.Value, id => new SensorId(id));
        value.Property(entity => entity.MetricKey).HasColumnName("metric_key").HasMaxLength(64);
        value.Property(entity => entity.Timestamp).HasColumnName("timestamp");
        value.Property(entity => entity.NumericValue).HasColumnName("numeric_value");
        value.Property(entity => entity.TextValue).HasColumnName("text_value");
        value.Property(entity => entity.Unit).HasColumnName("unit");
        value.Property(entity => entity.Quality).HasColumnName("quality");
        value.HasIndex(entity => new { entity.SensorId, entity.MetricKey, entity.Timestamp })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_measurement_values_sensor_metric_timestamp");
        sample.HasMany(entity => entity.Values)
            .WithOne()
            .HasForeignKey(entity => entity.SampleId)
            .OnDelete(DeleteBehavior.Cascade);
        value.HasOne<Sensor>().WithMany().HasForeignKey(entity => entity.SensorId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePollAttempts(ModelBuilder modelBuilder)
    {
        var attempt = modelBuilder.Entity<PollAttempt>();
        attempt.ToTable("poll_attempts");
        attempt.HasKey(entity => entity.Id);
        attempt.Property(entity => entity.Id).HasColumnName("id");
        attempt.Property(entity => entity.SensorId).HasColumnName("sensor_id").HasConversion(id => id.Value, value => new SensorId(value));
        attempt.Property(entity => entity.RequestId).HasColumnName("request_id");
        attempt.Property(entity => entity.StartedAt).HasColumnName("started_at");
        attempt.Property(entity => entity.CompletedAt).HasColumnName("completed_at");
        attempt.Property(entity => entity.AttemptNumber).HasColumnName("attempt_number");
        attempt.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>();
        attempt.Property(entity => entity.ErrorCode).HasColumnName("error_code");
        attempt.Property(entity => entity.ErrorMessage).HasColumnName("error_message");
        attempt.Property(entity => entity.RoundTripMilliseconds).HasColumnName("round_trip_ms");
        attempt.HasIndex(entity => new { entity.SensorId, entity.StartedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_poll_attempts_sensor_started");
        // Idempotency of gateway attempt import: one row per (poll request, attempt).
        attempt.HasIndex(entity => new { entity.SensorId, entity.RequestId, entity.AttemptNumber })
            .IsUnique()
            .HasDatabaseName("ux_poll_attempts_sensor_request_attempt");
        attempt.HasOne<Sensor>().WithMany().HasForeignKey(entity => entity.SensorId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureSensorStatus(ModelBuilder modelBuilder)
    {
        var status = modelBuilder.Entity<SensorStatusSnapshot>();
        status.ToTable("sensor_status");
        status.HasKey(entity => entity.SensorId);
        status.Property(entity => entity.SensorId).HasColumnName("sensor_id").HasConversion(id => id.Value, value => new SensorId(value));
        status.Property(entity => entity.State).HasColumnName("state").HasConversion<string>();
        status.Property(entity => entity.LastPollAt).HasColumnName("last_poll_at");
        status.Property(entity => entity.LastSuccessAt).HasColumnName("last_success_at");
        status.Property(entity => entity.ConsecutiveFailures).HasColumnName("consecutive_failures");
        status.Property(entity => entity.LastRssi).HasColumnName("last_rssi");
        status.Property(entity => entity.LastSnr).HasColumnName("last_snr");
        status.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        status.HasOne<Sensor>().WithOne().HasForeignKey<SensorStatusSnapshot>(entity => entity.SensorId).OnDelete(DeleteBehavior.Cascade);
    }
    private static void ConfigureGatewayTelemetry(ModelBuilder modelBuilder)
    {
        var snapshot = modelBuilder.Entity<GatewayTelemetrySnapshot>();
        snapshot.ToTable("gateway_telemetry_snapshots");
        snapshot.HasKey(entity => entity.Id);
        snapshot.Property(entity => entity.Id).HasColumnName("id");
        snapshot.Property(entity => entity.GatewaySnapshotId).HasColumnName("gateway_snapshot_id");
        snapshot.Property(entity => entity.CapturedAt).HasColumnName("captured_at");
        snapshot.Property(entity => entity.ImportedAt).HasColumnName("imported_at");
        snapshot.Property(entity => entity.Transport).HasColumnName("transport").HasMaxLength(64);
        snapshot.Property(entity => entity.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        snapshot.HasIndex(entity => entity.GatewaySnapshotId).IsUnique().HasDatabaseName("ux_gateway_telemetry_gateway_snapshot_id");
        snapshot.HasIndex(entity => entity.CapturedAt).HasDatabaseName("ix_gateway_telemetry_snapshots_captured_at");

        var reading = modelBuilder.Entity<GatewayTelemetryReading>();
        reading.ToTable("gateway_telemetry_readings");
        reading.HasKey(entity => new { entity.SnapshotId, entity.MetricKey });
        reading.Property(entity => entity.SnapshotId).HasColumnName("snapshot_id");
        reading.Property(entity => entity.MetricKey).HasColumnName("metric_key").HasMaxLength(128);
        reading.Property(entity => entity.NumericValue).HasColumnName("numeric_value");
        reading.Property(entity => entity.TextValue).HasColumnName("text_value");
        reading.HasIndex(entity => entity.MetricKey).HasDatabaseName("ix_gateway_telemetry_readings_metric_key");
        snapshot.HasMany(entity => entity.Readings)
            .WithOne()
            .HasForeignKey(entity => entity.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
