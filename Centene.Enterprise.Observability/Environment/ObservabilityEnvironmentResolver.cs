using System.Net.Sockets;

namespace Centene.Enterprise.Observability.Environment;

/// <summary>
/// Selects the telemetry backend at startup based on environment variables
/// and runtime probes. Pure function of env + filesystem + network.
///
/// This is THE contract. Python and Java SDK owners must match these semantics.
/// </summary>
public static class ObservabilityEnvironmentResolver
{
    // ---- Env var names — DO NOT rename without coordinating with Python/Java SDK owners ----
    public const string EnvRuntime = "CURRENT_RUNTIME_ENVIRONMENT";
    public const string EnvServiceName = "OTEL_SERVICE_NAME";
    public const string EnvOneAgentEnabled = "ONE_AGENT_ENABLED";
    public const string EnvDynatraceToken = "DYNATRACE_APITOKEN";
    public const string EnvDynatraceOtlpEndpoint = "DYNATRACE_OTLP_ENDPOINT";
    public const string EnvNamespaceMetrics = "NAMESPACE_METRICS";

    // OneAgent injection markers — set by the Dynatrace OneAgent operator on inject.
    private const string EnvDtHome = "DT_HOME";
    private const string EnvDtTenant = "DT_TENANT";

    private const int ZipkinDefaultPort = 9411;
    private const int ZipkinDetectTimeoutMs = 250;

    /// <summary>
    /// Resolves the telemetry backend from the current process environment.
    /// </summary>
    public static ConfiguredEnvironment Resolve()
    {
        var runtime = (System.Environment.GetEnvironmentVariable(EnvRuntime) ?? "").Trim().ToLowerInvariant();
        var serviceName = System.Environment.GetEnvironmentVariable(EnvServiceName) ?? "";

        // OTEL_SERVICE_NAME is required for everything except 'local'.
        if (string.IsNullOrWhiteSpace(serviceName) && runtime != "local")
        {
            return Noop(serviceName, runtime,
                $"{EnvServiceName} not set and runtime is not 'local'");
        }

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            serviceName = "local-development";
        }

        return runtime switch
        {
            "local" => ResolveLocal(serviceName, runtime),
            "ut"    => Noop(serviceName, runtime, "runtime=ut always uses noop"),
            "dev"   => ResolveNonProd(serviceName, runtime),
            "test"  => ResolveNonProd(serviceName, runtime),
            "prod"  => ResolveProd(serviceName, runtime),
            "aws"   => ResolveProd(serviceName, runtime), // experimental — same path as prod
            _       => Noop(serviceName, runtime,
                          $"{EnvRuntime} missing or unrecognized — must be local|ut|dev|test|prod|aws")
        };
    }

    private static ConfiguredEnvironment ResolveLocal(string svc, string rt)
    {
        return IsZipkinReachable()
            ? new ConfiguredEnvironment(
                ObservabilityEnvironment.Zipkin,
                ObservabilityEnvironment.Prometheus,
                $"runtime=local with Zipkin detected on :{ZipkinDefaultPort}",
                svc, rt)
            : Noop(svc, rt, $"runtime=local but no Zipkin on :{ZipkinDefaultPort}");
    }

    private static ConfiguredEnvironment ResolveNonProd(string svc, string rt)
    {
        var oneAgentEnabled = ReadBool(EnvOneAgentEnabled, defaultIfMissing: true);
        if (oneAgentEnabled && IsOneAgentInjected())
        {
            return new ConfiguredEnvironment(
                ObservabilityEnvironment.OneAgent,
                ObservabilityEnvironment.Prometheus,
                "OneAgent injection detected (DT_HOME/DT_TENANT present)",
                svc, rt);
        }

        if (HasDynatraceToken())
        {
            return new ConfiguredEnvironment(
                ObservabilityEnvironment.DynatraceNonProd,
                ObservabilityEnvironment.Prometheus,
                "OneAgent not detected — falling back to OTLP (non-prod). " +
                "OTLP ingestion costs more than OneAgent.",
                svc, rt,
                OtlpFallbackWarning: true);
        }

        return Noop(svc, rt,
            "Non-prod runtime but no OneAgent injection and no DYNATRACE_APITOKEN");
    }

    private static ConfiguredEnvironment ResolveProd(string svc, string rt)
    {
        var oneAgentEnabled = ReadBool(EnvOneAgentEnabled, defaultIfMissing: true);
        if (oneAgentEnabled && IsOneAgentInjected())
        {
            return new ConfiguredEnvironment(
                ObservabilityEnvironment.OneAgent,
                ObservabilityEnvironment.Prometheus,
                "OneAgent injection detected (DT_HOME/DT_TENANT present)",
                svc, rt);
        }

        if (HasDynatraceToken())
        {
            return new ConfiguredEnvironment(
                ObservabilityEnvironment.DynatraceProd,
                ObservabilityEnvironment.Prometheus,
                "OneAgent not detected — falling back to OTLP (prod). " +
                "OTLP ingestion costs more than OneAgent.",
                svc, rt,
                OtlpFallbackWarning: true);
        }

        return Noop(svc, rt,
            "Prod runtime but no OneAgent injection and no DYNATRACE_APITOKEN");
    }

    private static ConfiguredEnvironment Noop(string svc, string rt, string reason)
        => new(ObservabilityEnvironment.Noop, ObservabilityEnvironment.Noop, reason, svc, rt);

    // ---- Probes ----

    private static bool IsOneAgentInjected()
    {
        return !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable(EnvDtHome))
            || !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable(EnvDtTenant));
    }

    private static bool HasDynatraceToken()
        => !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable(EnvDynatraceToken));

    private static bool IsZipkinReachable()
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect("localhost", ZipkinDefaultPort, null, null);
            var connected = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(ZipkinDetectTimeoutMs));
            return connected && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static bool ReadBool(string name, bool defaultIfMissing)
    {
        var raw = System.Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw)) return defaultIfMissing;
        return bool.TryParse(raw, out var v) ? v : defaultIfMissing;
    }
}
