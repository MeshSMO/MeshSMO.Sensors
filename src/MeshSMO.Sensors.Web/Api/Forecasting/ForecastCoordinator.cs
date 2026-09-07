using System.Collections.Concurrent;
using MeshSMO.Sensors.Application.Abstractions;
using MeshSMO.Sensors.Forecasting.Abstractions;
using MeshSMO.Sensors.Forecasting.Configuration;
using MeshSMO.Sensors.Forecasting.Models;
using MeshSMO.Sensors.Web.Resilience;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;

namespace MeshSMO.Sensors.Web.Api.Forecasting;

public sealed class ForecastCoordinator : IDisposable
{
    private readonly ConcurrentDictionary<ForecastCacheKey, Lazy<Task<ForecastResult>>> _inflight = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ForecastingOptions _options;
    private readonly ILogger<ForecastCoordinator> _logger;
    private readonly MemoryCache _cache;
    private readonly ResiliencePipeline _calculationPipeline;
    private readonly SemaphoreSlim _trainingSlots;

    public ForecastCoordinator(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<ForecastingOptions> options,
        [FromKeyedServices(WebResiliencePipelines.ForecastCalculationKey)] ResiliencePipeline calculationPipeline,
        ILogger<ForecastCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value;
        _calculationPipeline = calculationPipeline;
        _logger = logger;
        _cache = new(new MemoryCacheOptions { SizeLimit = _options.MaximumCacheEntries });
        _trainingSlots = new(_options.MaximumConcurrentTrainings, _options.MaximumConcurrentTrainings);
    }

    public async Task<ForecastResult> GetAsync(
        ForecastDescriptor descriptor,
        TimeSpan horizon,
        CancellationToken cancellationToken)
    {
        var key = new ForecastCacheKey(descriptor.SensorId, descriptor.MetricKey, horizon);
        if (_cache.TryGetValue(key, out ForecastResult? cached) && cached is not null)
            return cached;

        var created = new Lazy<Task<ForecastResult>>(
            () => CalculateAsync(key, descriptor, horizon),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var calculation = _inflight.GetOrAdd(key, created);
        var task = calculation.Value;
        if (ReferenceEquals(created, calculation))
            _ = RemoveWhenCompleteAsync(key, calculation, task);

        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ForecastResult> CalculateAsync(
        ForecastCacheKey key,
        ForecastDescriptor descriptor,
        TimeSpan horizon) =>
        await _calculationPipeline.ExecuteAsync(
            async token => await CalculateCoreAsync(key, descriptor, horizon, token).ConfigureAwait(false),
            CancellationToken.None).ConfigureAwait(false);

    private async ValueTask<ForecastResult> CalculateCoreAsync(
        ForecastCacheKey key,
        ForecastDescriptor descriptor,
        TimeSpan horizon,
        CancellationToken cancellationToken)
    {
        var generatedAt = _clock.UtcNow;
        if (!_options.Enabled)
        {
            return ForecastResult.Unavailable(
                ForecastAvailability.Disabled,
                "Forecasting is disabled.",
                generatedAt,
                null,
                TimeSpan.FromMinutes(_options.StepMinutes));
        }

        await _trainingSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var source = scope.ServiceProvider.GetRequiredService<IForecastSeriesSource>();
            var service = scope.ServiceProvider.GetRequiredService<IForecastService>();
            var step = TimeSpan.FromMinutes(_options.StepMinutes);
            var query = new ForecastSeriesQuery(
                descriptor.SensorId,
                descriptor.MetricKey,
                descriptor.Unit,
                descriptor.PollInterval,
                generatedAt - TimeSpan.FromDays(_options.TrainingWindowDays),
                generatedAt,
                step,
                descriptor.Minimum,
                descriptor.Maximum,
                descriptor.MaximumMae);
            var series = await source.ReadAsync(query, cancellationToken).ConfigureAwait(false);
            var result = await Task.Run(
                () => service.Forecast(series, horizon, generatedAt, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            _cache.Set(
                key,
                result,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.ResultCacheMinutes),
                    Size = 1,
                });
            _logger.LogInformation(
                "Forecast {Availability} for {SensorSlug}/{MetricKey}, horizon {Horizon}, points {PointCount}",
                result.Availability,
                descriptor.SensorSlug,
                descriptor.MetricKey,
                horizon,
                result.Points.Count);
            return result;
        }
        finally
        {
            _trainingSlots.Release();
        }
    }

    private async Task RemoveWhenCompleteAsync(
        ForecastCacheKey key,
        Lazy<Task<ForecastResult>> calculation,
        Task<ForecastResult> task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Forecast calculation failed for sensor {SensorId}, metric {MetricKey}",
                key.SensorId,
                key.MetricKey);
        }
        finally
        {
            if (_inflight.TryGetValue(key, out var current) && ReferenceEquals(current, calculation))
                _inflight.TryRemove(key, out _);
        }
    }

    public void Dispose()
    {
        _trainingSlots.Dispose();
        _cache.Dispose();
    }

    private readonly record struct ForecastCacheKey(Guid SensorId, string MetricKey, TimeSpan Horizon);
}
