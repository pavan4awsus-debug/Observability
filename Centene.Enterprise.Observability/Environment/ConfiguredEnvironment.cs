namespace Centene.Enterprise.Observability.Environment;

/// <summary>
/// Output of the environment resolver — what got picked and why.
/// Surfaced via the self-check endpoint and DI so operators can diagnose
/// without re-reading env vars.
/// </summary>
public sealed record ConfiguredEnvironment(
    ObservabilityEnvironment TraceEnvironment,
    ObservabilityEnvironment MeterEnvironment,
    string Reason,
    string ServiceName,
    string RuntimeEnvironment,
    bool OtlpFallbackWarning = false);
