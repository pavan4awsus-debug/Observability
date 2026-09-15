using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Centene.Enterprise.Observability.Configuration;
using Centene.Enterprise.Observability.Enrichers;
using Centene.Enterprise.Observability.Environment;
using Centene.Enterprise.Observability.Hosting;
using Centene.Enterprise.Observability.Logging;
using Centene.Enterprise.Observability.Metrics;
using Centene.Enterprise.Observability.Tracing;

namespace Centene.Enterprise.Observability.Extensions;

/// <summary>
/// One-call bootstrap for Centene Observability. Mirrors the Go module's
/// observability.AutoBootstrap(ctx) — reads env vars, picks the backend,
/// wires logging + metrics + tracing + PHI masking + self-check + shutdown.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Centene Observability SDK with default options.
    /// </summary>
    public static IServiceCollection AddCenteneObservability(this IServiceCollection services)
        => services.AddCenteneObservability(_ => { });

    /// <summary>
    /// Adds the Centene Observability SDK with custom options.
    /// </summary>
    public static IServiceCollection AddCenteneObservability(
        this IServiceCollection services,
        Action<ObservabilityOptions> configure)
    {
        services.Configure(configure);

        var configured = ObservabilityEnvironmentResolver.Resolve();
        services.AddSingleton(configured);

        var optionsBuilder = new ObservabilityOptions();
        configure(optionsBuilder);

        // Resource attributes shared by traces, metrics, AND logs.
        // This is how service.name and runtime.environment flow through to
        // Splunk and Dynatrace for trace-log correlation (OBS-05).
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: configured.ServiceName, serviceVersion: GetSdkVersion())
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("centene.runtime.environment", configured.RuntimeEnvironment),
                new("centene.observability.sdk", "dotnet"),
                new("centene.observability.sdk.version", GetSdkVersion()),
            });
        foreach (var kvp in optionsBuilder.ResourceAttributes)
        {
            resourceBuilder.AddAttributes(new[] { new KeyValuePair<string, object>(kvp.Key, kvp.Value) });
        }

        // ---- Tracing + Metrics ----
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resourceBuilder);
                tracing.AddSource(BatchJobTracer.ActivitySourceName);
                tracing.AddSource("Centene.*"); // all centene libraries

                if (optionsBuilder.EnableAutoInstrumentation)
                {
                    tracing.AddAspNetCoreInstrumentation();
                    tracing.AddHttpClientInstrumentation();
                }

                ConfigureTraceExporter(tracing, configured);
            })
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resourceBuilder);
                metrics.AddMeter(GoldenSignalMeter.MeterName);
                metrics.AddMeter("Centene.*");

                if (optionsBuilder.EnableAutoInstrumentation)
                {
                    metrics.AddAspNetCoreInstrumentation();
                    metrics.AddHttpClientInstrumentation();
                    metrics.AddRuntimeInstrumentation();
                }

                ConfigureMetricExporter(metrics, configured);
            });

        // ---- Logging (OBS-05) ----
        // This is what makes structured logging with trace correlation actually work.
        // OpenTelemetry's logging provider automatically attaches trace_id and
        // span_id from Activity.Current to every log record, and resource
        // attributes (service.name, runtime.environment) flow through too.
        // Consumers' existing ILogger<T> calls just work — no code change.
        services.AddLogging(logging =>
        {
            logging.AddOpenTelemetry(otelLogging =>
            {
                otelLogging.SetResourceBuilder(resourceBuilder);
                otelLogging.IncludeFormattedMessage = true;
                otelLogging.IncludeScopes = true;          // honors W3CLogEnricher.BeginEnrichmentScope
                otelLogging.ParseStateValues = true;       // structured fields from log message templates

                ConfigureLogExporter(otelLogging, configured);
            });
        });

        // ---- Singletons ----
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ObservabilityOptions>>().Value;
            return new GoldenSignalMeter(configured.ServiceName, opts.NamespaceMetrics);
        });
        services.AddSingleton<BatchJobTracer>();
        services.AddSingleton<PhiMaskingEnricher>();

        // ---- Shutdown hook ----
        services.AddHostedService<ObservabilityShutdownHostedService>();

        // ---- Startup log line (for PPM monitoring) ----
        services.AddSingleton<IObservabilityStartupLogger>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Centene.Enterprise.Observability");
            return new ObservabilityStartupLogger(logger, configured);
        });

        return services;
    }

    private static void ConfigureTraceExporter(TracerProviderBuilder tracing, ConfiguredEnvironment env)
    {
        switch (env.TraceEnvironment)
        {
            case ObservabilityEnvironment.Zipkin:
                // The OpenTelemetry Zipkin exporter was deprecated in Dec 2025.
                // Zipkin accepts OTLP natively on its standard port 9411 via zipkin-otel.
                // Ref: https://opentelemetry.io/blog/2025/deprecating-zipkin-exporters/
                tracing.AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri("http://localhost:9411/v1/traces");
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                });
                break;

            case ObservabilityEnvironment.DynatraceNonProd:
            case ObservabilityEnvironment.DynatraceProd:
                tracing.AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                .AddOtlpExporter(o =>
                {
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    var endpoint = System.Environment.GetEnvironmentVariable(
                        ObservabilityEnvironmentResolver.EnvDynatraceOtlpEndpoint);
                    if (!string.IsNullOrWhiteSpace(endpoint)) o.Endpoint = new Uri(endpoint);
                    var token = System.Environment.GetEnvironmentVariable(
                        ObservabilityEnvironmentResolver.EnvDynatraceToken);
                    if (!string.IsNullOrWhiteSpace(token))
                        o.Headers = $"Authorization=Api-Token {token}";
                });
                break;

            case ObservabilityEnvironment.OneAgent:
                // OneAgent intercepts the OTel pipeline automatically; no exporter wiring needed.
                break;

            case ObservabilityEnvironment.Noop:
            default:
                // No exporter — spans are dropped at the SDK boundary.
                break;
        }
    }

    private static void ConfigureMetricExporter(MeterProviderBuilder metrics, ConfiguredEnvironment env)
    {
        switch (env.MeterEnvironment)
        {
            case ObservabilityEnvironment.Prometheus:
                metrics.AddPrometheusExporter();
                break;
            case ObservabilityEnvironment.DynatraceNonProd:
            case ObservabilityEnvironment.DynatraceProd:
                metrics.AddOtlpExporter(o =>
                {
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    var endpoint = System.Environment.GetEnvironmentVariable(
                        ObservabilityEnvironmentResolver.EnvDynatraceOtlpEndpoint);
                    if (!string.IsNullOrWhiteSpace(endpoint)) o.Endpoint = new Uri(endpoint);
                    var token = System.Environment.GetEnvironmentVariable(
                        ObservabilityEnvironmentResolver.EnvDynatraceToken);
                    if (!string.IsNullOrWhiteSpace(token))
                        o.Headers = $"Authorization=Api-Token {token}";
                });
                break;
            case ObservabilityEnvironment.Noop:
            default:
                break;
        }
    }

    private static void ConfigureLogExporter(OpenTelemetryLoggerOptions logging, ConfiguredEnvironment env)
    {
        switch (env.TraceEnvironment)
        {
            case ObservabilityEnvironment.DynatraceNonProd:
            case ObservabilityEnvironment.DynatraceProd:
                logging.AddOtlpExporter(o =>
                {
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    var endpoint = System.Environment.GetEnvironmentVariable(
                        ObservabilityEnvironmentResolver.EnvDynatraceOtlpEndpoint);
                    if (!string.IsNullOrWhiteSpace(endpoint)) o.Endpoint = new Uri(endpoint);
                    var token = System.Environment.GetEnvironmentVariable(
                        ObservabilityEnvironmentResolver.EnvDynatraceToken);
                    if (!string.IsNullOrWhiteSpace(token))
                        o.Headers = $"Authorization=Api-Token {token}";
                });
                break;

            case ObservabilityEnvironment.Zipkin:
                // Local development — write structured logs to console with
                // trace context attached. Splunk forwarder picks them up in
                // deployed environments where OneAgent/OTLP handles it.
                logging.AddConsoleExporter();
                break;

            case ObservabilityEnvironment.OneAgent:
                // OneAgent handles log capture from container stdout.
                // No exporter wiring needed.
                break;

            case ObservabilityEnvironment.Noop:
            default:
                break;
        }
    }

    private static string GetSdkVersion()
        => typeof(ObservabilityServiceCollectionExtensions).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}

internal interface IObservabilityStartupLogger { }

internal sealed class ObservabilityStartupLogger : IObservabilityStartupLogger
{
    public ObservabilityStartupLogger(ILogger logger, ConfiguredEnvironment env)
    {
        var level = env.OtlpFallbackWarning ? LogLevel.Warning : LogLevel.Information;
        logger.Log(level,
            "Centene Observability bootstrapped: service={ServiceName} runtime={Runtime} " +
            "traceBackend={TraceBackend} meterBackend={MeterBackend} reason={Reason}",
            env.ServiceName,
            env.RuntimeEnvironment,
            ObservabilityEnvironmentNames.ToCanonical(env.TraceEnvironment),
            ObservabilityEnvironmentNames.ToCanonical(env.MeterEnvironment),
            env.Reason);
    }
}