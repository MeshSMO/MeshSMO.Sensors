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
    public DbSet<GatewayIdentity> GatewayIdentities => Set<GatewayIdentity>();
    public DbSet<OutboxHealthProbe> HealthProbes => Set<OutboxHealthProbe>();
    public DbSet<SensorPollState> SensorPollStates => Set<SensorPollState>();

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

        var gatewayIdentity = modelBuilder.Entity<GatewayIdentity>();
        gatewayIdentity.ToTable("gateway_identity");
        gatewayIdentity.HasKey(entity => entity.Id);
        gatewayIdentity.Property(entity => entity.Id).HasColumnName("id");
        gatewayIdentity.Property(entity => entity.InstanceId).HasColumnName("instance_id");

        var probe = modelBuilder.Entity<OutboxHealthProbe>();
        probe.ToTable("health_probe");
        probe.HasKey(entity => entity.Id);
        probe.Property(entity => entity.Id).HasColumnName("id");
        probe.Property(entity => entity.CheckedAt).HasColumnName("checked_at");

        var pollState = modelBuilder.Entity<SensorPollState>();
        pollState.ToTable("sensor_poll_states");
        pollState.HasKey(entity => entity.SensorSlug);
        pollState.Property(entity => entity.SensorSlug).HasColumnName("sensor_slug").HasMaxLength(128);
        pollState.Property(entity => entity.LastPollStartedAt).HasColumnName("last_poll_started_at");
    }
}
