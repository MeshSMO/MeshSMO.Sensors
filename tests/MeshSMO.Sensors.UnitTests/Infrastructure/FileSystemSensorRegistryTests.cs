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
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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

    private static FileSystemSensorRegistry CreateRegistry(string directory) =>
        new(
            Options.Create(new SensorRegistryOptions { Directory = directory }),
            new TestHostEnvironment { ContentRootPath = directory });

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
