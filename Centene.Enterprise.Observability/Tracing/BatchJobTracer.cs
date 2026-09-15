using System.Diagnostics;

namespace Centene.Enterprise.Observability.Tracing;

/// <summary>
/// Creates root spans for batch jobs and scheduled tasks that have no
/// inbound HTTP request to anchor a trace. Implements OBS-07 (distributed
/// tracing standard) for asynchronous flows.
/// </summary>
/// <remarks>
/// Use for: nightly ETL jobs, queue consumers, cron tasks, message handlers.
/// Sets SpanKind.Server so Dynatrace ingests it (SpanKind.Internal is dropped).
/// </remarks>
public sealed class BatchJobTracer
{
    public const string ActivitySourceName = "Centene.Observability.BatchJobs";
    private static readonly ActivitySource Source = new(ActivitySourceName);

    /// <summary>
    /// Starts a root span for a batch job execution. Dispose to end.
    /// </summary>
    /// <param name="jobName">Stable identifier for the job (e.g. 'nightly-claims-rollup').</param>
    /// <param name="correlationId">Optional correlation id to attach as an attribute.</param>
    public Activity? StartBatch(string jobName, string? correlationId = null)
    {
        var activity = Source.StartActivity(
            name: $"batch.{jobName}",
            kind: ActivityKind.Server);

        if (activity is null) return null;

        activity.SetTag("job.name", jobName);
        activity.SetTag("job.kind", "batch");
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            activity.SetTag("correlation_id", correlationId);
        }
        return activity;
    }

    /// <summary>
    /// Records a successful completion on the active span.
    /// </summary>
    public void MarkSuccess(Activity? activity, long itemsProcessed)
    {
        if (activity is null) return;
        activity.SetTag("job.items_processed", itemsProcessed);
        activity.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// Records a failure on the active span. Adds an OTel-spec-compliant
    /// 'exception' event with type, message, and stacktrace tags, and sets
    /// the activity status to Error.
    /// </summary>
    /// <remarks>
    /// Inlined exception recording instead of the obsolete RecordException
    /// extension. Activity.AddException is .NET 9+; this SDK targets .NET 8.
    /// When upgrading to .NET 9, replace the manual ActivityEvent with
    /// activity.AddException(ex).
    /// </remarks>
    public void MarkFailure(Activity? activity, Exception ex)
    {
        if (activity is null) return;

        activity.SetStatus(ActivityStatusCode.Error, ex.Message);

        var tags = new ActivityTagsCollection
        {
            { "exception.type", ex.GetType().FullName },
            { "exception.message", ex.Message },
            { "exception.stacktrace", ex.ToString() },
        };
        activity.AddEvent(new ActivityEvent("exception", default, tags));
    }
}