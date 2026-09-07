using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using MeshSMO.Sensors.Infrastructure;
using MeshSMO.Sensors.Infrastructure.Persistence;
using MeshSMO.Sensors.Web.Api;
using MeshSMO.Sensors.Web.Api.MeasurementHistory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MeshSMO.Sensors.UnitTests.Web.Api;

/// <summary>Cross-cutting public API surface: rate limiting and the OpenAPI document.</summary>
public sealed class PublicApiSurfaceTests : IDisposable
{
    private readonly string _databaseName = $"public-api-{Guid.NewGuid():N}";
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public PublicApiSurfaceTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Sensors"] = "Host=localhost;Database=unused",
            ["Registry:Directory"] = Path.Combine(Path.GetTempPath(), $"registry-{Guid.NewGuid():N}"),
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSensorsInfrastructure(builder.Configuration);
        builder.Services.RemoveAll<DbContextOptions<SensorsDbContext>>();
        builder.Services.RemoveAll<IDbContextOptionsConfiguration<SensorsDbContext>>();
        builder.Services.AddDbContext<SensorsDbContext>(options =>
            options.UseInMemoryDatabase(_databaseName));
        builder.Services.AddHealthChecks();
        builder.Services.AddScoped<MeasurementHistoryReader>();
        // Tiny limits so a single test can cross the threshold instantly.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("public-api", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new()
                {
                    PermitLimit = 3,
                    Window = TimeSpan.FromMinutes(1),
                }));
        });
        builder.Services.AddOpenApi();
        _app = builder.Build();
        _app.UseRateLimiter();
        _app.MapSensorApi();
        _app.MapMeasurementHistoryApi();
        _app.MapOpenApi();
        _app.StartAsync().GetAwaiter().GetResult();
        _client = (_app.Services.GetRequiredService<IServer>() as TestServer)!.CreateClient();
    }

    [Fact]
    public async Task PublicApi_AppliesPerIpRateLimit()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await _client.GetAsync("/api/v1/sensors/ghost/measurements?metric=temperature&from=2026-09-05T10:00:00Z&to=2026-09-05T10:15:00Z");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var limited = await _client.GetAsync("/api/v1/sensors/ghost/measurements?metric=temperature&from=2026-09-05T10:00:00Z&to=2026-09-05T10:15:00Z");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task OpenApi_ExposesMeasurementsEndpoint()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var path = document.GetProperty("paths").GetProperty("/api/v1/sensors/{slug}/measurements");
        Assert.True(path.TryGetProperty("get", out _));
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
