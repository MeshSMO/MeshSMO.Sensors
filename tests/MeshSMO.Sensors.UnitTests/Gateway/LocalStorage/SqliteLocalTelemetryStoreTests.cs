using System.Text.Json;
using MeshSMO.Sensors.Gateway.LocalStorage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.UnitTests.Gateway.LocalStorage;

public sealed class SqliteLocalTelemetryStoreTests
{
    [Fact]
    public async Task SnapshotsSurviveStoreRecreationUntilAcknowledged()
    {
        using var database = new TemporarySqliteDatabase();
        var capturedAt = new DateTimeOffset(2026, 9, 5, 12, 30, 0, TimeSpan.Zero);
        var firstStore = CreateStore(database.Path);
        await firstStore.InitializeAsync(CancellationToken.None);
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

        var reopenedStore = CreateStore(database.Path);
        await reopenedStore.InitializeAsync(CancellationToken.None);
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

        var readings = await reopenedStore.ReadReadingsAsync(firstId, CancellationToken.None);
        var temperature = Assert.Single(readings, reading => reading.MetricKey == "sensors.temperature");
        Assert.Equal(21.5, temperature.NumericValue);
        Assert.Null(temperature.TextValue);

        await reopenedStore.AcknowledgeAsync([firstId], CancellationToken.None);

        Assert.Equal(1, await reopenedStore.CountPendingAsync(CancellationToken.None));
        Assert.Empty(await reopenedStore.ReadReadingsAsync(firstId, CancellationToken.None));
        var remaining = await reopenedStore.ReadPendingAsync(10, CancellationToken.None);
        Assert.Equal(secondId, Assert.Single(remaining).Id);
    }

    [Fact]
    public async Task InvalidJsonIsRejectedBeforeWriting()
    {
        using var database = new TemporarySqliteDatabase();
        var store = CreateStore(database.Path);
        await store.InitializeAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<JsonException>(
            () => store.AppendAsync(DateTimeOffset.UtcNow, "http", "not-json", CancellationToken.None));

        Assert.Equal(0, await store.CountPendingAsync(CancellationToken.None));
    }

    private static SqliteLocalTelemetryStore CreateStore(string path) =>
        new(Options.Create(new LocalTelemetryOptions { DatabasePath = path }));

    private sealed class TemporarySqliteDatabase : IDisposable
    {
        public TemporarySqliteDatabase()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"meshsmo-gateway-{Guid.NewGuid():N}.db");
        }

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
            {
                File.Delete(path);
            }
        }
    }
}
