using MeshSMO.Sensors.Application.Registry;
using MeshSMO.Sensors.Infrastructure.Registry;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.UnitTests.Infrastructure;

public sealed class FileSystemSensorRegistryTests
{
    [Fact]
    public async Task LoadAsync_reads_valid_sensor_definition()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "sensor.yaml"), ValidYaml());
            var registry = CreateRegistry(directory);

            var definitions = await registry.LoadAsync(CancellationToken.None);

            var sensor = Assert.Single(definitions);
            Assert.Equal("smolensk-center", sensor.Slug.Value);
            Assert.Equal(TimeSpan.FromMinutes(5), sensor.PollInterval);
            Assert.Equal(["temperature", "humidity"], sensor.Metrics);
            Assert.Null(sensor.LoginPassword);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_keeps_explicit_empty_login_password()
    {
        var definitions = await LoadSingleWithLoginPassword(@"loginPassword: """"");

        Assert.Equal(string.Empty, definitions.LoginPassword);
    }

    [Fact]
    public async Task LoadAsync_resolves_login_password_environment_reference()
    {
        var definitions = await LoadSingleWithLoginPassword(
            @"loginPassword: ""${TEST_SENSOR_NODE_PASSWORD}""",
            new(StringComparer.Ordinal) { ["TEST_SENSOR_NODE_PASSWORD"] = "hello" });

        Assert.Equal("hello", definitions.LoginPassword);
    }

    [Fact]
    public async Task LoadAsync_uses_login_password_environment_default()
    {
        var definitions = await LoadSingleWithLoginPassword(
            @"loginPassword: ""${TEST_SENSOR_NODE_PASSWORD:-fallback-pass}""",
            new(StringComparer.Ordinal) { ["TEST_SENSOR_NODE_PASSWORD"] = null });

        Assert.Equal("fallback-pass", definitions.LoginPassword);
    }

    [Fact]
    public async Task LoadAsync_reports_missing_login_password_environment_variable()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "sensor.yaml"),
                WithLoginPassword(@"loginPassword: ""${TEST_SENSOR_NODE_PASSWORD_MISSING}"""));
            var registry = CreateRegistry(directory, new(StringComparer.Ordinal));

            var exception = await Assert.ThrowsAsync<SensorRegistryValidationException>(
                () => registry.LoadAsync(CancellationToken.None));

            Assert.Contains(
                exception.Errors,
                error => error.Contains("environment variable 'TEST_SENSOR_NODE_PASSWORD_MISSING' that is not set", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_reports_login_password_exceeding_wire_limit()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "sensor.yaml"),
                WithLoginPassword(@"loginPassword: ""sixteen-bytes-xy"""));
            var registry = CreateRegistry(directory, new(StringComparer.Ordinal));

            var exception = await Assert.ThrowsAsync<SensorRegistryValidationException>(
                () => registry.LoadAsync(CancellationToken.None));

            Assert.Contains(
                exception.Errors,
                error => error.Contains("must be at most 15 bytes", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_reports_malformed_login_password_reference()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "sensor.yaml"),
                WithLoginPassword(@"loginPassword: ""${unclosed"""));
            var registry = CreateRegistry(directory, new(StringComparer.Ordinal));

            var exception = await Assert.ThrowsAsync<SensorRegistryValidationException>(
                () => registry.LoadAsync(CancellationToken.None));

            Assert.Contains(
                exception.Errors,
                error => error.Contains("malformed or unresolved '${...}'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<SensorDefinition> LoadSingleWithLoginPassword(
        string loginPasswordLine,
        Dictionary<string, string?>? environment = null)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "sensor.yaml"), WithLoginPassword(loginPasswordLine)).ConfigureAwait(false);
            var registry = CreateRegistry(directory, environment ?? new Dictionary<string, string?>(StringComparer.Ordinal));

            return Assert.Single(await registry.LoadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string WithLoginPassword(string loginPasswordLine) =>
        ValidYaml().Replace(
            @"protocol: ""meshsmo-weather-v1""",
            $@"protocol: ""meshsmo-weather-v1""
  {loginPasswordLine}",
            StringComparison.Ordinal);

    [Fact]
    public async Task LoadAsync_reports_duplicate_slug_before_database_write()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "first.yaml"), ValidYaml());
            await File.WriteAllTextAsync(
                Path.Combine(directory, "second.yaml"),
                ValidYaml()
                    .Replace("0198f7b0-1eb1-7b4b-8c8f-dab3d10d8421", "0198f7b0-1eb1-7b4b-8c8f-dab3d10d8422", StringComparison.Ordinal)
                    .Replace("test-public-key", "second-public-key", StringComparison.Ordinal));
            var registry = CreateRegistry(directory);

            var exception = await Assert.ThrowsAsync<SensorRegistryValidationException>(
                () => registry.LoadAsync(CancellationToken.None));

            Assert.Contains(exception.Errors, error => error.Contains("Duplicate slug 'smolensk-center'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_reports_duplicate_public_key_before_database_write()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "first.yaml"), ValidYaml());
            await File.WriteAllTextAsync(
                Path.Combine(directory, "second.yaml"),
                ValidYaml()
                    .Replace("0198f7b0-1eb1-7b4b-8c8f-dab3d10d8421", "0198f7b0-1eb1-7b4b-8c8f-dab3d10d8422", StringComparison.Ordinal)
                    .Replace("smolensk-center", "smolensk-west", StringComparison.Ordinal));
            var registry = CreateRegistry(directory);

            var exception = await Assert.ThrowsAsync<SensorRegistryValidationException>(
                () => registry.LoadAsync(CancellationToken.None));

            Assert.Contains(exception.Errors, error => error.Contains("Duplicate mesh.publicKey 'test-public-key'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_reads_polling_schedule()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "sensor.yaml"),
                WithPollingSchedule("""
                    schedule:
                      timeZone: "Europe/Moscow"
                      windows:
                        - from: "08:00"
                          to: "12:00"
                          interval: "15m"
                        - from: "12:00"
                          to: "22:00"
                          interval: "30m"
                        - from: "22:00"
                          to: "06:00"
                          interval: "1h"
                  """));
            var registry = CreateRegistry(directory);

            var definitions = await registry.LoadAsync(CancellationToken.None);

            var sensor = Assert.Single(definitions);
            var schedule = sensor.PollingSchedule;
            Assert.NotNull(schedule);
            Assert.Equal("Europe/Moscow", schedule.TimeZone.Id);
            Assert.Equal(
                TimeSpan.FromMinutes(15),
                schedule.ResolveInterval(new DateTimeOffset(2026, 6, 15, 5, 30, 0, TimeSpan.Zero)));
            // Touching windows are not overlapping: 12:00 local belongs to the second window.
            Assert.Equal(
                TimeSpan.FromMinutes(30),
                schedule.ResolveInterval(new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero)));
            // 23:30 local inside the wrap window; 07:00 local in the gap → null (base interval).
            Assert.Equal(
                TimeSpan.FromHours(1),
                schedule.ResolveInterval(new DateTimeOffset(2026, 6, 15, 20, 30, 0, TimeSpan.Zero)));
            Assert.Null(schedule.ResolveInterval(new DateTimeOffset(2026, 6, 15, 4, 0, 0, TimeSpan.Zero)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_reports_overlapping_schedule_windows()
    {
        await LoadScheduleExpectingError(
            """
                schedule:
                  timeZone: "Europe/Moscow"
                  windows:
                    - from: "08:00"
                      to: "12:00"
                      interval: "15m"
                    - from: "11:00"
                      to: "18:00"
                      interval: "30m"
              """,
            "polling.schedule windows overlap");
    }

    [Fact]
    public async Task LoadAsync_reports_duplicate_schedule_windows()
    {
        await LoadScheduleExpectingError(
            """
                schedule:
                  timeZone: "Europe/Moscow"
                  windows:
                    - from: "08:00"
                      to: "12:00"
                      interval: "15m"
                    - from: "08:00"
                      to: "12:00"
                      interval: "30m"
              """,
            "polling.schedule windows overlap");
    }

    [Fact]
    public async Task LoadAsync_reports_zero_length_schedule_window()
    {
        await LoadScheduleExpectingError(
            """
                schedule:
                  timeZone: "Europe/Moscow"
                  windows:
                    - from: "08:00"
                      to: "08:00"
                      interval: "15m"
              """,
            "must not be zero-length");
    }

    [Fact]
    public async Task LoadAsync_reports_missing_schedule_timezone()
    {
        await LoadScheduleExpectingError(
            """
                schedule:
                  windows:
                    - from: "08:00"
                      to: "12:00"
                      interval: "15m"
              """,
            "polling.schedule.timeZone is required");
    }

    [Fact]
    public async Task LoadAsync_reports_unknown_schedule_timezone()
    {
        await LoadScheduleExpectingError(
            """
                schedule:
                  timeZone: "Europe/NonExisting"
                  windows:
                    - from: "08:00"
                      to: "12:00"
                      interval: "15m"
              """,
            "was not found on this machine");
    }

    [Fact]
    public async Task LoadAsync_reports_invalid_schedule_window_time()
    {
        await LoadScheduleExpectingError(
            """
                schedule:
                  timeZone: "Europe/Moscow"
                  windows:
                    - from: "8:5"
                      to: "12:00"
                      interval: "15m"
              """,
            "must be a local time in 'HH:mm' format");
    }

    [Fact]
    public async Task LoadAsync_reports_schedule_window_interval_shorter_than_timeout()
    {
        await LoadScheduleExpectingError(
            """
                schedule:
                  timeZone: "Europe/Moscow"
                  windows:
                    - from: "08:00"
                      to: "12:00"
                      interval: "10s"
              """,
            "polling.timeout cannot exceed a polling.schedule window interval");
    }

    [Fact]
    public async Task LoadAsync_reports_empty_schedule_windows()
    {
        await LoadScheduleExpectingError(
            """
                schedule:
                  timeZone: "Europe/Moscow"
                  windows: []
              """,
            "must contain at least one window");
    }

    private static async Task LoadScheduleExpectingError(string scheduleBlock, string expectedErrorFragment)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "sensor.yaml"),
                WithPollingSchedule(scheduleBlock));
            var registry = CreateRegistry(directory);

            var exception = await Assert.ThrowsAsync<SensorRegistryValidationException>(
                () => registry.LoadAsync(CancellationToken.None));

            Assert.Contains(exception.Errors, error => error.Contains(expectedErrorFragment, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string WithPollingSchedule(string scheduleBlock) =>
        ValidYaml().Replace(
            "  enabled: true",
            $"  enabled: true\n{scheduleBlock}",
            StringComparison.Ordinal);

    private static FileSystemSensorRegistry CreateRegistry(string directory, Dictionary<string, string?>? environment = null) =>
        new(
            Options.Create(new SensorRegistryOptions { Directory = directory }),
            new TestHostEnvironment { ContentRootPath = directory },
            environment is null ? null : name => environment.GetValueOrDefault(name));

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meshsmo-sensors-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string ValidYaml() =>
        """
        id: "0198f7b0-1eb1-7b4b-8c8f-dab3d10d8421"
        slug: "smolensk-center"
        displayName: "Смоленск — центр"
        mesh:
          publicKey: "test-public-key"
          protocol: "meshsmo-weather-v1"
        polling:
          interval: "5m"
          timeout: "30s"
          maxAttempts: 2
          enabled: true
        public:
          visible: true
          indexable: true
        location:
          latitude: 55.0000
          longitude: 33.0000
          precision: "approximate"
        metrics:
          - temperature
          - humidity
        """;

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "MeshSMO.Sensors.UnitTests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
