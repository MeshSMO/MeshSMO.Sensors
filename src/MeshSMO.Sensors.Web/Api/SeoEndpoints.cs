using System.Text;
using MeshSMO.Sensors.Domain.Sensors;
using MeshSMO.Sensors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeshSMO.Sensors.Web.Api;

public static class SeoEndpoints
{
    public static IEndpointRouteBuilder MapSitemap(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sitemap.xml", async Task<IResult> (
            SensorsDbContext dbContext,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var baseUrl = (configuration["Public:BaseUrl"] ?? "https://sensors.meshsmo.ru").TrimEnd('/');
            var sensors = await dbContext.Sensors
                .AsNoTracking()
                .Where(sensor => sensor.Enabled && sensor.PublicVisible && sensor.PublicIndexable)
                .OrderBy(sensor => sensor.Slug)
                .Select(sensor => new { sensor.Slug, sensor.UpdatedAt })
                .ToListAsync(cancellationToken);

            var builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
            builder.Append($"\n  <url><loc>{baseUrl}/</loc></url>");
            builder.Append($"\n  <url><loc>{baseUrl}/sensors</loc></url>");
            builder.Append($"\n  <url><loc>{baseUrl}/about</loc></url>");
            foreach (var sensor in sensors)
            {
                builder.Append(
                    $"\n  <url><loc>{baseUrl}/sensors/{sensor.Slug.Value}</loc><lastmod>{sensor.UpdatedAt:yyyy-MM-dd}</lastmod></url>");
            }

            builder.Append("\n</urlset>");
            return Results.Content(builder.ToString(), "application/xml; charset=utf-8");
        });

        return app;
    }

    /// <summary>
    /// Route-aware SPA fallback (spec §26-§27): prerendered sensor pages are
    /// served as static files, registered-but-not-prerendered slugs get the
    /// SPA fallback, and anything else gets a real HTTP 404 instead of an
    /// HTML 200.
    /// </summary>
    public static IEndpointRouteBuilder MapSensorFallbacks(this IEndpointRouteBuilder app)
    {
        app.MapFallback("/sensors/{slug}", async Task<IResult> (
            string slug,
            SensorsDbContext dbContext,
            IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            SensorSlug slugValue;
            try
            {
                slugValue = new SensorSlug(slug);
            }
            catch (ArgumentException)
            {
                return Results.NotFound();
            }

            var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
            var prerendered = Path.Combine(webRoot, "sensors", slug, "index.html");
            if (File.Exists(prerendered))
            {
                return Results.File(prerendered, "text/html");
            }

            var known = await dbContext.Sensors
                .AsNoTracking()
                .AnyAsync(sensor => sensor.Slug == slugValue && sensor.Enabled && sensor.PublicVisible, cancellationToken);
            if (known)
            {
                var spaFallback = Path.Combine(webRoot, "__spa-fallback.html");
                return File.Exists(spaFallback) ? Results.File(spaFallback, "text/html") : Results.NotFound();
            }

            var notFound = Path.Combine(webRoot, "__spa-fallback.html");
            return File.Exists(notFound)
                ? Results.Content(await File.ReadAllTextAsync(notFound, cancellationToken), "text/html", statusCode: 404)
                : Results.NotFound();
        });

        app.MapFallback("/sensors", (IWebHostEnvironment environment) =>
        {
            var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
            var prerendered = Path.Combine(webRoot, "sensors", "index.html");
            return File.Exists(prerendered) ? Results.File(prerendered, "text/html") : Results.NotFound();
        });

        return app;
    }
}
