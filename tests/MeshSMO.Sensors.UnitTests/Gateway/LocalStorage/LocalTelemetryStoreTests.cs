using System.Text.Json;
using MeshSMO.Sensors.Gateway.LocalStorage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeshSMO.Sensors.UnitTests.Gateway.LocalStorage;

public sealed class LocalTelemetryStoreTests
{
    [Fact]
    public async Task SnapshotsSurviveStoreRecreationUntilAcknowledged()
    {
        using var database = new TemporarySqliteDatabase();
        var capturedAt = new DateTimeOffset(2026, 9, 5, 12, 30, 0, TimeSpan.Zero);
        var firstServices = CreateServices(database.Path);
        await using var _ = firstServices;
        var firstStore = CreateStore(firstServices);
        await MigrateAsync(firstServices, database.Path);
        var firstId = await firstStore.AppendAsync(
            capturedAt,
            "http",
            "{\"sensors\":{\"temperature\":21.5}}",
            CancellationToken.None);
        var secondId = await firstStore.AppendAsync(
            capturedAt.AddMinutes(1),
            "serial",
            "{\"core\":\"battery:4100\"}",
            CancellationToken.None);

        // A fresh service provider over the same file models a gateway restart.
        var reopenedServices = CreateServices(database.Path);
        await using var __ = reopenedServices;
        var reopenedStore = CreateStore(reopenedServices);
        await MigrateAsync(reopenedServices, database.Path);
        var pending = await reopenedStore.ReadPendingAsync(10, CancellationToken.None);

        Assert.Collection(
            pending,
            first =>
            {
                Assert.Equal(firstId, first.Id);
                Assert.Equal(capturedAt, first.CapturedAt);
                Assert.Equal("http", first.Transport);
                using var payload = JsonDocument.Parse(first.PayloadJson);
                Assert.Equal(21.5, payload.RootElement.GetProperty("sensors").GetProperty("temperature").GetDouble());
            },
            second =>
            {
                Assert.Equal(secondId, second.Id);
                Assert.Equal("serial", second.Transport);
            });

        var temperature = Assert.Single(
            pending[0].Readings,
            reading => string.Equals(reading.MetricKey, "sensors.temperature", StringComparison.Ordinal));
        Assert.Equal(21.5, temperature.NumericValue);
        Assert.Null(temperature.TextValue);

        await reopenedStore.AcknowledgeAsync([firstId], CancellationToken.None);

        Assert.Equal(1, await reopenedStore.CountPendingAsync(CancellationToken.None));
        await using var db = await reopenedServices
            .GetRequiredService<IDbContextFactory<LocalOutboxDbContext>>()
            .CreateDbContextAsync();
        Assert.False(await db.Readings.AnyAsync(reading => reading.SnapshotId == firstId));
        var remaining = await reopenedStore.ReadPendingAsync(10, CancellationToken.None);
        Assert.Equal(secondId, Assert.Single(remaining).Id);
    }

    [Fact]
    public async Task InvalidJsonIsRejectedBeforeWriting()
    {
        using var database = new TemporarySqliteDatabase();
        var services = CreateServices(database.Path);
        await using var _ = services;
        var store = CreateStore(services);
        await MigrateAsync(services, database.Path);

        await Assert.ThrowsAnyAsync<JsonException>(
            () => store.AppendAsync(DateTimeOffset.UtcNow, "http", "not-json", CancellationToken.None));

        Assert.Equal(0, await store.CountPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadingsAreSortedByMetricKey()
    {
        using var database = new TemporarySqliteDatabase();
        var services = CreateServices(database.Path);
        await using var _ = services;
        var store = CreateStore(services);
        await MigrateAsync(services, database.Path);
        await store.AppendAsync(
            DateTimeOffset.UtcNow,
            "http",
            """{"radio":{"rssi":-92},"core":{"battery_mv":4100}}""",
            CancellationToken.None);

        var snapshot = Assert.Single(await store.ReadPendingAsync(10, CancellationToken.None));

        Assert.Equal(
            ["core.battery_mv", "radio.rssi"],
            snapshot.Readings.Select(reading => reading.MetricKey).ToArray());
        Assert.Equal(4100, snapshot.Readings[0].NumericValue);
        Assert.Equal(-92, snapshot.Readings[1].NumericValue);
    }

    private static ServiceProvider CreateServices(string path) =>
        new ServiceCollection()
            .AddDbContextFactory<LocalOutboxDbContext>(options => options.UseSqlite($"Data Source={path}"))
            .BuildServiceProvider();

    private static LocalTelemetryStore CreateStore(ServiceProvider services) =>
        new(services.GetRequiredService<IDbContextFactory<LocalOutboxDbContext>>());

    private static Task MigrateAsync(ServiceProvider services, string path) =>
        LocalOutboxDatabase.MigrateAsync(
            services.GetRequiredService<IDbContextFactory<LocalOutboxDbContext>>(),
            path,
            CancellationToken.None);

    private sealed class TemporarySqliteDatabase : IDisposable
    {
        public TemporarySqliteDatabase() => Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"meshsmo-gateway-{Guid.NewGuid():N}.db");

        public string Path { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(Path);
            DeleteIfExists($"{Path}-wal");
            DeleteIfExists($"{Path}-shm");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
