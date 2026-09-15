namespace Centene.Enterprise.Observability.Environment;

/// <summary>
/// Resolved telemetry backend. Mirrors the Go module's OtelEnvironment enum
/// so the polyglot contract stays consistent across .NET, Go, Python, and Java.
/// </summary>
public enum ObservabilityEnvironment
{
    /// <summary>No-op exporter. Drops all telemetry. Used in 'ut' and fallback scenarios.</summary>
    Noop,

    /// <summary>Local Zipkin exporter (port 9411). Used in 'local' runtime when Zipkin is detected.</summary>
    Zipkin,

    /// <summary>Dynatrace non-prod tenant via OTLP. Used in 'dev'/'test' when OneAgent is unavailable.</summary>
    DynatraceNonProd,

    /// <summary>Dynatrace production tenant via OTLP. Used in 'prod'/'aws' when OneAgent is unavailable.</summary>
    DynatraceProd,

    /// <summary>Dynatrace via OneAgent injection. Preferred path — lowest ingestion cost.</summary>
    OneAgent,

    /// <summary>Prometheus scrape endpoint on :10550/metrics. Used for custom metrics ingestion.</summary>
    Prometheus
}

/// <summary>
/// String constants for ObservabilityEnvironment values, matching the Go module's
/// canonical string form. Used in logs and the self-check endpoint.
/// </summary>
public static class ObservabilityEnvironmentNames
{
    public const string Noop = "noop";
    public const string Zipkin = "zipkin";
    public const string DynatraceNonProd = "dynatraceNonProd";
    public const string DynatraceProd = "dynatraceProd";
    public const string OneAgent = "oneagent";
    public const string Prometheus = "prometheus";

    public static string ToCanonical(ObservabilityEnvironment env) => env switch
    {
        ObservabilityEnvironment.Noop => Noop,
        ObservabilityEnvironment.Zipkin => Zipkin,
        ObservabilityEnvironment.DynatraceNonProd => DynatraceNonProd,
        ObservabilityEnvironment.DynatraceProd => DynatraceProd,
        ObservabilityEnvironment.OneAgent => OneAgent,
        ObservabilityEnvironment.Prometheus => Prometheus,
        _ => "unknown"
    };
}
