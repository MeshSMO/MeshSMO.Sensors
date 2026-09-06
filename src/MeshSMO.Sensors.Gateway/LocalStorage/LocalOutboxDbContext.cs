using Microsoft.EntityFrameworkCore;

namespace MeshSMO.Sensors.Gateway.LocalStorage;

/// <summary>
/// EF Core model of the gateway's local SQLite outbox (database file configured
/// via LocalTelemetry:DatabasePath). Schema changes go through EF migrations in
/// Gateway/Migrations, applied at startup by LocalOutboxDatabase.MigrateAsync —
/// the gateway has no PostgreSQL access and no separate migrator.
/// </summary>
public sealed class LocalOutboxDbContext(DbContextOptions<LocalOutboxDbContext> options) : DbContext(options)
{
    public DbSet<OutboxSnapshot> Snapshots => Set<OutboxSnapshot>();
    public DbSet<OutboxReading> Readings => Set<OutboxReading>();
    public DbSet<OutboxHealthProbe> HealthProbes => Set<OutboxHealthProbe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var snapshot = modelBuilder.Entity<OutboxSnapshot>();
        snapshot.ToTable("telemetry_snapshots");
        snapshot.HasKey(entity => entity.Id);
        snapshot.Property(entity => entity.Id).HasColumnName("id");
        snapshot.Property(entity => entity.CapturedAt).HasColumnName("captured_at");
        snapshot.Property(entity => entity.Transport).HasColumnName("transport").HasMaxLength(64);
        snapshot.Property(entity => entity.PayloadJson).HasColumnName("payload_json");
        snapshot.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        snapshot.HasIndex(entity => new { entity.CapturedAt, entity.Id })
            .HasDatabaseName("ix_telemetry_snapshots_captured_at");

        var reading = modelBuilder.Entity<OutboxReading>();
        reading.ToTable("telemetry_readings");
        reading.HasKey(entity => new { entity.SnapshotId, entity.MetricKey });
        reading.Property(entity => entity.SnapshotId).HasColumnName("snapshot_id");
        reading.Property(entity => entity.MetricKey).HasColumnName("metric_key").HasMaxLength(128);
        reading.Property(entity => entity.NumericValue).HasColumnName("numeric_value");
        reading.Property(entity => entity.TextValue).HasColumnName("text_value");
        snapshot.HasMany(entity => entity.Readings)
            .WithOne()
            .HasForeignKey(entity => entity.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        var probe = modelBuilder.Entity<OutboxHealthProbe>();
        probe.ToTable("health_probe");
        probe.HasKey(entity => entity.Id);
        probe.Property(entity => entity.Id).HasColumnName("id");
        probe.Property(entity => entity.CheckedAt).HasColumnName("checked_at");
    }
}
