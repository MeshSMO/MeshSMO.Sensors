using System.Net;
using System.Xml.Linq;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Infrastructure;
using MeshSMO.Sensors.Web.Api;
using MeshSMO.Sensors.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MeshSMO.Sensors.UnitTests;

public sealed class SitemapAndFallbackTests : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _databaseName = $"sitemap-{Guid.NewGuid():N}";

    public SitemapAndFallbackTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Sensors"] = "Host=localhost;Database=unused",
            ["Registry:Directory"] = Path.Combine(Path.GetTempPath(), $"registry-{Guid.NewGuid():N}"),
            ["Public:BaseUrl"] = "https://sensors.meshsmo.ru",
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSensorsInfrastructure(builder.Configuration);
        builder.Services.RemoveAll(typeof(DbContextOptions<SensorsDbContext>));
        builder.Services.RemoveAll(typeof(IDbContextOptionsConfiguration<SensorsDbContext>));
        builder.Services.AddDbContext<SensorsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        builder.Services.AddHealthChecks();
        _app = builder.Build();
        _app.MapSitemap();
        _app.MapSensorFallbacks();
        _app.StartAsync().GetAwaiter().GetResult();

        using var scope = _app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SensorsDbContext>();
        dbContext.Sensors.Add(new Sensor(
            new SensorId(Guid.NewGuid()),
            new SensorSlug("smolensk-center"),
            "Смоленск — центр",
            null,
            "pub-key-sitemap",
            "meshsmo-weather-v1",
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(30),
            2,
            enabled: true,
            publicVisible: true,
            publicIndexable: true,
            null,
            null,
            null,
            ["temperature"],
            DateTimeOffset.UtcNow));
        dbContext.SaveChanges();
        _client = (_app.Services.GetRequiredService<IServer>() as TestServer)!.CreateClient();
    }

    [Fact]
    public async Task Sitemap_ContainsStaticRoutes_AndIndexableSensors()
    {
        var response = await _client.GetAsync("/sitemap.xml");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var ns = xml.Root!.Name.Namespace;
        var locs = xml.Root.Descendants(ns + "loc").Select(element => element.Value).ToArray();
        Assert.Contains("https://sensors.meshsmo.ru/sensors/smolensk-center", locs);
        Assert.Contains("https://sensors.meshsmo.ru/sensors", locs);
        Assert.Equal(4, locs.Length);
    }

    [Fact]
    public async Task SensorFallback_UnknownSlug_Returns404()
    {
        var response = await _client.GetAsync("/sensors/definitely-not-registered");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
