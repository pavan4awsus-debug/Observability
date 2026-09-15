using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Centene.Enterprise.Observability.Configuration;

namespace Centene.Enterprise.Observability.Hosting;

/// <summary>
/// Hooks IHostApplicationLifetime.ApplicationStopping and flushes the
/// TracerProvider and MeterProvider before the host shuts down. Mirrors the
/// Go module's Shutdown(ctx) semantics — consumers do not call it manually.
/// </summary>
internal sealed class ObservabilityShutdownHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly TracerProvider? _tracerProvider;
    private readonly MeterProvider? _meterProvider;
    private readonly ObservabilityOptions _options;
    private readonly ILogger<ObservabilityShutdownHostedService> _logger;

    public ObservabilityShutdownHostedService(
        IHostApplicationLifetime lifetime,
        IOptions<ObservabilityOptions> options,
        ILogger<ObservabilityShutdownHostedService> logger,
        TracerProvider? tracerProvider = null,
        MeterProvider? meterProvider = null)
    {
        _lifetime = lifetime;
        _tracerProvider = tracerProvider;
        _meterProvider = meterProvider;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime.ApplicationStopping.Register(OnStopping);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void OnStopping()
    {
        var timeoutMs = (int)_options.ShutdownTimeout.TotalMilliseconds;
        try
        {
            _tracerProvider?.ForceFlush(timeoutMs);
            _tracerProvider?.Shutdown(timeoutMs);
            _meterProvider?.ForceFlush(timeoutMs);
            _meterProvider?.Shutdown(timeoutMs);
            _logger.LogInformation("Centene Observability: telemetry flushed and providers shut down.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Centene Observability: error flushing telemetry on shutdown.");
        }
    }
}
