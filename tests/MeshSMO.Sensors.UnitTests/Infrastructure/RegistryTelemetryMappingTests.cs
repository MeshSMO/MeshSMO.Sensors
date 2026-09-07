using MeshSMO.Sensors.Application.Registry;
using MeshSMO.Sensors.Infrastructure.Registry;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.UnitTests.Infrastructure;

public sealed class RegistryTelemetryMappingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"registry-{Guid.NewGuid():N}");

    [Fact]
    public async Task Load_ParsesTelemetryChannelMappings()
    {
        WriteSensor("sensor.yaml", """
            id: "0198f7b0-1eb1-7b4b-8c8f-dab3d10d8421"
            slug: "mapped-node"
            displayName: "Mapped node"
            mesh:
              publicKey: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
              protocol: "meshcore-req-lpp"
            polling:
              interval: "5m"
              timeout: "30s"
              maxAttempts: 2
              enabled: true
            public:
              visible: true
              indexable: false
            metrics:
              - temperature

            telemetry:
              channels:
                - channel: 1
                  type: voltage
                  metric: battery_voltage
                  displayName: "Напряжение батареи"
                  unit: "В"
                - channel: 2
                  type: voltage
                  metric: solar_panel_voltage
                  displayName: "Напряжение солнечной панели"
                - channel: 4
                  metric: custom_channel
            """);

        var registry = new FileSystemSensorRegistry(
            Options.Create(new SensorRegistryOptions { Directory = _directory }),
            TestHostEnvironment);

        var sensors = await registry.LoadAsync(CancellationToken.None);

        var channels = Assert.Single(sensors).Channels;
        Assert.Collection(
            channels,
            channel =>
            {
                Assert.Equal(1, channel.Channel);
                Assert.Equal("voltage", channel.Type);
                Assert.Equal("battery_voltage", channel.Metric);
                Assert.Equal("Напряжение батареи", channel.DisplayName);
                Assert.Equal("В", channel.Unit);
            },
            channel =>
            {
                Assert.Equal(2, channel.Channel);
                Assert.Equal("solar_panel_voltage", channel.Metric);
                Assert.Null(channel.Unit);
            },
            channel =>
            {
                Assert.Equal(4, channel.Channel);
                Assert.Null(channel.Type);
                Assert.Equal("custom_channel", channel.Metric);
            });
    }

    [Fact]
    public async Task Load_RejectsUnknownType_AndDuplicateMappings()
    {
        WriteSensor("sensor.yaml", """
            id: "0198f7b0-1eb1-7b4b-8c8f-dab3d10d8421"
            slug: "bad-node"
            displayName: "Bad node"
            mesh:
              publicKey: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
              protocol: "meshcore-req-lpp"
            polling:
              interval: "5m"
              timeout: "30s"
              maxAttempts: 2
              enabled: true
            public:
              visible: true
              indexable: false
            metrics:
              - temperature

            telemetry:
              channels:
                - channel: 1
                  type: radiation
                  metric: cpm
                - channel: 1
                  type: voltage
                  metric: battery_voltage
                - channel: 1
                  type: voltage
                  metric: solar_panel_voltage
            """);

        var registry = new FileSystemSensorRegistry(
            Options.Create(new SensorRegistryOptions { Directory = _directory }),
            TestHostEnvironment);

        var exception = await Assert.ThrowsAsync<SensorRegistryValidationException>(
            () => registry.LoadAsync(CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Contains("unknown type 'radiation'", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("is mapped more than once", StringComparison.Ordinal));
    }

    private IHostEnvironment TestHostEnvironment { get; } = new TestEnvironment();

    private void WriteSensor(string fileName, string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, fileName), content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(Directory.GetCurrentDirectory());
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public string EnvironmentName { get; set; } = "Development";
    }
}
