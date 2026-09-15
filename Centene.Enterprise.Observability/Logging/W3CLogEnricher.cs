using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Centene.Enterprise.Observability.Logging;

/// <summary>
/// Helper for OBS-05 (structured logging) field names and manual enrichment scopes.
/// </summary>
/// <remarks>
/// In most cases you do NOT need to use this class directly. When the SDK is
/// bootstrapped via AddCenteneObservability(), the OpenTelemetry logging
/// provider is wired in and automatically attaches trace_id, span_id,
/// service.name, and runtime.environment to every log entry via
/// Activity.Current and resource attributes.
///
/// Use this class only when you need to manually push extra enrichment fields
/// into a logging scope — for example, attaching member_id or claim_id to all
/// logs within a request handler:
///
/// <code>
/// using (W3CLogEnricher.BeginEnrichmentScope(_logger, new()
/// {
///     ["member_id"] = phi.Mask(memberId),
///     ["operation"] = "claim-process"
/// }))
/// {
///     _logger.LogInformation("Processing claim");
///     // ...all log entries in this scope carry the extra fields
/// }
/// </code>
///
/// Field names below are the canonical schema; Python and Java SDK owners
/// must emit the same names so Splunk dashboards work across services
/// regardless of language.
/// </remarks>
public static class W3CLogEnricher
{
    /// <summary>Canonical field name for the W3C trace id.</summary>
    public const string TraceIdField = "trace_id";

    /// <summary>Canonical field name for the W3C span id.</summary>
    public const string SpanIdField = "span_id";

    /// <summary>Canonical field name for the service name.</summary>
    public const string ServiceNameField = "service_name";

    /// <summary>Canonical field name for the runtime environment.</summary>
    public const string RuntimeEnvField = "runtime_environment";

    /// <summary>Canonical field name for an operation correlation id.</summary>
    public const string CorrelationIdField = "correlation_id";

    /// <summary>
    /// Pushes additional enrichment fields onto the logger's scope.
    /// Returns an IDisposable that pops the scope when disposed.
    /// </summary>
    public static IDisposable? BeginEnrichmentScope(ILogger logger, IReadOnlyDictionary<string, object?> fields)
        => logger.BeginScope(fields);

    /// <summary>
    /// Returns the current W3C trace context (trace_id, span_id) from
    /// Activity.Current, or empty strings if no activity is in scope.
    /// Useful for stamping fields into outbound payloads or audit records.
    /// </summary>
    public static (string TraceId, string SpanId) GetCurrentTraceContext()
    {
        var activity = Activity.Current;
        if (activity is null) return (string.Empty, string.Empty);
        return (activity.TraceId.ToString(), activity.SpanId.ToString());
    }
}