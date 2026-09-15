using System.Diagnostics.Metrics;

namespace Centene.Enterprise.Observability.Metrics;

/// <summary>
/// Emits the four Golden Signals defined by Google SRE — Latency, Traffic,
/// Errors, Saturation — for every service. Implements OBS-04.
/// </summary>
/// <remarks>
/// Metric names follow the canonical schema:
///   centene.service.requests          (counter — Traffic)
///   centene.service.errors            (counter — Errors)
///   centene.service.latency_ms        (histogram — Latency)
///   centene.service.saturation        (observable gauge — Saturation)
///
/// Python and Java SDK owners must emit metrics with these exact names so
/// SLO dashboards in Dynatrace work across services regardless of language.
/// </remarks>
public sealed class GoldenSignalMeter : IDisposable
{
    public const string MeterName = "Centene.Observability.GoldenSignals";

    private readonly Meter _meter;
    private readonly Counter<long> _requests;
    private readonly Counter<long> _errors;
    private readonly Histogram<double> _latencyMs;

    public GoldenSignalMeter(string serviceName, bool namespacePrefix)
    {
        _meter = new Meter(MeterName);

        string Name(string suffix) => namespacePrefix
            ? $"{serviceName}.centene.service.{suffix}"
            : $"centene.service.{suffix}";

        _requests = _meter.CreateCounter<long>(
            Name("requests"),
            unit: "{request}",
            description: "Golden Signal: Traffic. Total inbound requests handled.");

        _errors = _meter.CreateCounter<long>(
            Name("errors"),
            unit: "{error}",
            description: "Golden Signal: Errors. Total errored requests.");

        _latencyMs = _meter.CreateHistogram<double>(
            Name("latency_ms"),
            unit: "ms",
            description: "Golden Signal: Latency. Request duration distribution.");

        // Saturation is observable — registered by consumers via RegisterSaturation().
    }

    public void RecordRequest(string route, int statusCode, double durationMs)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("route", route),
            new("status_code", statusCode)
        };
        _requests.Add(1, tags);
        _latencyMs.Record(durationMs, tags);
        if (statusCode >= 500) _errors.Add(1, tags);
    }

    /// <summary>
    /// Registers a saturation gauge fed by the supplied observer.
    /// Typical sources: CPU %, memory %, queue depth, thread pool utilization.
    /// </summary>
    public void RegisterSaturation(string saturationKind, Func<double> observer)
    {
        _meter.CreateObservableGauge(
            $"centene.service.saturation.{saturationKind}",
            observer,
            unit: "1",
            description: $"Golden Signal: Saturation ({saturationKind}).");
    }

    public void Dispose() => _meter.Dispose();
}
