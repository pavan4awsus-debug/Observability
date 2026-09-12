namespace Centene.Enterprise.Observability.Configuration;

/// <summary>
/// Options for configuring the Centene Observability SDK.
/// Most consumers should not need to set anything here — env vars drive behavior.
/// </summary>
public sealed class ObservabilityOptions
{
    /// <summary>
    /// How long to wait for telemetry flush during application shutdown.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When true, the OTEL_SERVICE_NAME will be prepended to every custom metric name.
    /// Mirrors NAMESPACE_METRICS env var in the Go module. Default: false.
    /// </summary>
    public bool NamespaceMetrics { get; set; }
        = bool.TryParse(System.Environment.GetEnvironmentVariable("NAMESPACE_METRICS"), out var v) && v;

    /// <summary>
    /// When true, structured logs will be enriched with PHI masking.
    /// Required for services handling Medicaid/Medicare data (OBS-08). Default: true.
    /// </summary>
    public bool EnablePhiMasking { get; set; } = true;

    /// <summary>
    /// When true, ASP.NET Core, HttpClient, and runtime instrumentation are auto-registered.
    /// Default: true.
    /// </summary>
    public bool EnableAutoInstrumentation { get; set; } = true;

    /// <summary>
    /// Additional resource attributes attached to all telemetry.
    /// </summary>
    public IDictionary<string, object> ResourceAttributes { get; set; }
        = new Dictionary<string, object>();

    /// <summary>
    /// Path for the self-check endpoint. Default: /observability/selfcheck.
    /// </summary>
    public string SelfCheckPath { get; set; } = "/observability/selfcheck";

    /// <summary>
    /// Port for the Prometheus scrape endpoint. Default: 10550 (matches Go module).
    /// </summary>
    public int PrometheusScrapePort { get; set; } = 10550;
}
