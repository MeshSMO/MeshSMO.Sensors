using MeshSMO.Sensors.Gateway.LocalStorage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeshSMO.Sensors.Gateway.MeshCore;

public static class MeshCoreGatewayServiceCollectionExtensions
{
    public static IServiceCollection AddMeshCoreGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<MeshCoreOptions>()
            .Bind(configuration.GetSection(MeshCoreOptions.SectionName))
            .Validate(
                static options => options.ReconnectDelaySeconds > 0,
                "MeshCore:ReconnectDelaySeconds must be greater than zero.")
            .Validate(
                static options => options.TelemetryCollectionIntervalSeconds >= 60,
                "MeshCore:TelemetryCollectionIntervalSeconds must be at least 60 seconds.")
            .Validate(
                static options => options.Mode != MeshCoreConnectionMode.Http ||
                    options.Http.BaseAddress is { IsAbsoluteUri: true },
                "MeshCore:Http:BaseAddress must be an absolute URI in HTTP mode.")
            .Validate(
                static options => options.Mode != MeshCoreConnectionMode.Http ||
                    string.Equals(options.Http.BaseAddress?.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal),
                "MeshCore:Http:BaseAddress must use HTTPS in HTTP mode.")
            .Validate(
                static options => options.Mode != MeshCoreConnectionMode.Http ||
                    !string.IsNullOrWhiteSpace(options.Http.AdminPassword),
                "MeshCore:Http:AdminPassword is required in HTTP mode.")
            .Validate(
                static options => options.Http.TimeoutSeconds > 0,
                "MeshCore:Http:TimeoutSeconds must be greater than zero.")
            .Validate(
                static options => options.Mode != MeshCoreConnectionMode.Serial ||
                    !string.IsNullOrWhiteSpace(options.Serial.PortName),
                "MeshCore:Serial:PortName is required in Serial mode.")
            .Validate(
                static options => options.Serial.BaudRate > 0,
                "MeshCore:Serial:BaudRate must be greater than zero.")
            .Validate(
                static options => options.Serial.CommandTimeoutSeconds > 0,
                "MeshCore:Serial:CommandTimeoutSeconds must be greater than zero.")
            .ValidateOnStart();

        services
            .AddOptions<LocalTelemetryOptions>()
            .Bind(configuration.GetSection(LocalTelemetryOptions.SectionName))
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.DatabasePath),
                "LocalTelemetry:DatabasePath is required.")
            .ValidateOnStart();

        services.AddDbContextFactory<LocalOutboxDbContext>((serviceProvider, options) =>
        {
            var databasePath = Path.GetFullPath(
                serviceProvider.GetRequiredService<IOptions<LocalTelemetryOptions>>().Value.DatabasePath);
            options.UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString());
            options.AddInterceptors(SqliteOutboxConnectionInterceptor.Instance);
        });
        services.AddSingleton<ILocalTelemetryStore, LocalTelemetryStore>();

        // The repeater panel keeps a single global token: Worker and
        // SensorTelemetryPoller must share one login instead of invalidating
        // each other's session on every request.
        services.AddSingleton<MeshCoreTelSession>();

        services
            .AddHttpClient<IMeshCoreTelClient, MeshCoreTelHttpClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MeshCoreOptions>>().Value.Http;
                client.BaseAddress = NormalizeBaseAddress(options.BaseAddress ?? new Uri("https://127.0.0.1/"));
                // HttpClient.Timeout is a per-request cap that individual calls
                // cannot widen; MeshCoreTelHttpClient bounds every call itself
                // (panel: Http:TimeoutSeconds, acquisition: 2.5× radio window,
                // see docs/repeater-firmware-acquisition-spec.md §8.6).
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MeshCoreOptions>>().Value.Http;
                var handler = new SocketsHttpHandler();
                // The MeshCoreTel ESP32 firmware only negotiates TLS 1.2 with
                // the static-RSA cipher below; OpenSSL-based runtimes (Linux
                // containers) do not offer it by default, Windows does.
                handler.SslOptions.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
                if (!OperatingSystem.IsWindows())
                {
                    handler.SslOptions.CipherSuitesPolicy = new(new[]
                    {
                        System.Net.Security.TlsCipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256
                    });
                }

                if (options.AllowInvalidServerCertificate)
#pragma warning disable CA5359
                    handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
#pragma warning restore CA5359

                return handler;
            });

        services.AddTransient<RepeaterSerialClient>();
        services.AddTransient<DisabledRepeaterClient>();
        services.AddTransient<IRepeaterClient>(serviceProvider =>
        {
            var mode = serviceProvider.GetRequiredService<IOptions<MeshCoreOptions>>().Value.Mode;
            return mode switch
            {
                MeshCoreConnectionMode.Http => serviceProvider.GetRequiredService<IMeshCoreTelClient>(),
                MeshCoreConnectionMode.Serial => serviceProvider.GetRequiredService<RepeaterSerialClient>(),
                _ => serviceProvider.GetRequiredService<DisabledRepeaterClient>(),
            };
        });

        return services;
    }

    private static Uri NormalizeBaseAddress(Uri baseAddress) => new Uri($"{baseAddress.AbsoluteUri.TrimEnd('/')}/", UriKind.Absolute);
}
