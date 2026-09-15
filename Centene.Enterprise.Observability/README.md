# Centene.Enterprise.Observability (.NET)

Centene Enterprise Observability SDK for .NET. Auto-bootstrapped OpenTelemetry pipeline with Dynatrace, Splunk, Prometheus, and Zipkin support.

This is the **.NET reference implementation** of the Centene Observability standards (OBS-04, OBS-05, OBS-07, OBS-08). The Go module at `gitlab.centene.com/ea/application/app/observability/golang/observability` is the canonical pattern; this package matches its env-var contract and behavior so the polyglot experience is consistent across .NET, Go, Python, and Java services.

> **WARNING** Check that the .NET version you use is supported by OneAgent. This SDK targets **.NET 8 (LTS)**.

---

## Quick Start (Local)

### 1. Set environment variables

```bash
OTEL_SERVICE_NAME=your-service-name      # e.g. claims-api
CURRENT_RUNTIME_ENVIRONMENT=local
```

### 2. Install the package

```bash
dotnet add package Centene.Enterprise.Observability
```

### 3. Bootstrap in `Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCenteneObservability();   // ONE call. Everything wired.

var app = builder.Build();
app.MapCenteneObservabilitySelfCheck();        // GET /observability/selfcheck
app.Run();
```

### 4. Start

Run with Zipkin on localhost:9411 to see local spans. In deployed environments, traces flow to Dynatrace automatically (OneAgent or OTLP). Custom metrics scrape at `:10550/metrics`.

---

## Local Zipkin Setup

The SDK sends traces to Zipkin using **OTLP** (the OpenTelemetry wire protocol), not Zipkin's legacy JSON v2 format. This is because the OpenTelemetry project deprecated its Zipkin-specific exporter in December 2025 — Zipkin now accepts OTLP natively on its standard port 9411.

For developers, the experience is unchanged: run Zipkin locally, open the Zipkin UI at `http://localhost:9411`, see your spans. Only the wire format changed.

### Recommended local setup

```bash
docker run -d -p 9411:9411 openzipkin/zipkin-slim
```

Recent `openzipkin/zipkin-slim` images include the OTLP receiver out of the box. If you're running an older Zipkin Docker image and traces stop arriving after upgrading the SDK, update to a current image.

### What the SDK sends and where

| What | Endpoint | Wire protocol |
|---|---|---|
| Traces in local runtime | `http://localhost:9411/v1/traces` | OTLP (HTTP/protobuf) |
| Traces in dev/test/prod (OTLP fallback) | `$DYNATRACE_OTLP_ENDPOINT` | OTLP (HTTP/protobuf) |
| Traces in dev/test/prod (OneAgent) | Captured by OneAgent agent | OneAgent native |
| Metrics scrape | `http://localhost:10550/metrics` | Prometheus |

The `/v1/traces` path is the OTLP HTTP standard. Port 9411 is Zipkin's traditional port. Zipkin accepts both formats on this port.

---

## Building with Azure DevOps

Configure `nuget.config` to point at the Centene Azure Artifacts feed, then run `NuGetAuthenticate@1` before `dotnet restore` in your pipeline. A reference `azure-pipelines.yml` is included in this repo.

---

## AutoBootstrap Details

`AddCenteneObservability` requires `CURRENT_RUNTIME_ENVIRONMENT` and `OTEL_SERVICE_NAME`. If they are missing, noop versions of the exporters are used. The SDK probes for OneAgent injection by checking `DT_HOME` / `DT_TENANT` (set by the OneAgent operator). If detected, it uses the OneAgent ingestion technique. If not, it falls back to OTLP with `DYNATRACE_APITOKEN`.

> **NOTE:** OTLP ingestion costs more than OneAgent. A warning log is emitted when OTLP is selected. The PPM team monitors these and will request a restart once the OneAgent injection failure reason is figured out.

`CURRENT_RUNTIME_ENVIRONMENT` **MUST** be one of:

| Value | Trace backend | Metric backend |
|---|---|---|
| `local` | Zipkin via OTLP if reachable on :9411, else noop | Prometheus or noop |
| `ut` | Noop (always) | Noop (always) |
| `dev` | OneAgent if injected, else OTLP non-prod, else noop | Prometheus |
| `test` | OneAgent if injected, else OTLP non-prod, else noop | Prometheus |
| `prod` | OneAgent if injected, else OTLP prod, else noop | Prometheus |
| `aws` | Experimental — same as prod | Prometheus |

### If `ONE_AGENT_ENABLED=false`

- **Traces:** when `DYNATRACE_APITOKEN` is present, sent via OTLP. When missing, noop.
- **Metrics:** available for scraping at `localhost:10550/metrics`. Sent to noop on port conflict.

`NAMESPACE_METRICS=true` prepends `OTEL_SERVICE_NAME` to every custom metric name.

---

## Example of AutoBootstrap

```bash
CURRENT_RUNTIME_ENVIRONMENT=dev
OTEL_SERVICE_NAME=my-spiffy-application
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCenteneObservability();
var app = builder.Build();
app.MapCenteneObservabilitySelfCheck();
app.Run();
```

Inspect which backend was picked via the self-check endpoint:

```bash
curl http://localhost:5000/observability/selfcheck
```

```json
{
  "serviceName": "my-spiffy-application",
  "runtimeEnvironment": "dev",
  "traceBackend": "oneagent",
  "meterBackend": "prometheus",
  "reason": "OneAgent injection detected (DT_HOME/DT_TENANT present)",
  "otlpFallbackWarning": false,
  "sdkVersion": "0.1.0.0"
}
```

---

## Custom Bootstrapping a TracerProvider / MeterProvider

If you need to override the provider, register OpenTelemetry yourself and skip `AddCenteneObservability`. You can still use `ObservabilityEnvironmentResolver.Resolve()` to honor the same env-var contract.

---

## Use the Tracer

```csharp
private static readonly ActivitySource Tracer = new("Centene.MyService");

using var span = Tracer.StartActivity("operation-name", ActivityKind.Server);
span?.SetTag("attribute.key", "value");
```

The `Centene.*` source prefix is auto-registered by the SDK. SpanKind matters: `Internal` is dropped by Dynatrace.

---

## Tracer Context Propagation

Propagators are wired automatically via the ASP.NET Core and HttpClient instrumentation. Incoming W3C `traceparent` headers are picked up; outbound HttpClient calls inject the header on the way out.

For gRPC, register `OpenTelemetry.Instrumentation.GrpcNetClient` (not bundled — coordinate with the platform team if needed).

---

## Use a Meter

```csharp
public sealed class ClaimsService
{
    private static readonly Meter MyMeter = new("Centene.Claims");
    private readonly Counter<long> _processed = MyMeter.CreateCounter<long>(
        "Practitioner_Count",
        description: "The number of practitioner records that have been processed");

    public void Process() => _processed.Add(1);
}
```

Meters under `Centene.*` are auto-registered.

---

## Dynatrace configuration for metric ingestion

Add to your Helm chart so Dynatrace scrapes the Prometheus endpoint:

```yaml
metadata:
  annotations:
    metrics.dynatrace.com/scrape: 'true'
    metrics.dynatrace.com/port: '10550'
    metrics.dynatrace.com/path: /metrics
    metrics.dynatrace.com/secure: 'false'
    metrics.dynatrace.com/filter: |
      {
        "mode": "include",
        "names": ["Practitioner_Count"]
      }
```

---

## Golden Signals (OBS-04)

`GoldenSignalMeter` is registered as a singleton. Inject and call:

```csharp
app.MapGet("/foo", (GoldenSignalMeter signals) =>
{
    var sw = Stopwatch.StartNew();
    // ... work ...
    sw.Stop();
    signals.RecordRequest("/foo", 200, sw.Elapsed.TotalMilliseconds);
});

// For saturation (CPU, queue depth, etc.):
signals.RegisterSaturation("cpu", () => GetCurrentCpuPercent());
```

Metric names emitted: `centene.service.requests`, `centene.service.errors`, `centene.service.latency_ms`, `centene.service.saturation.<kind>`.

---

## Batch jobs (OBS-07)

For jobs that run without an inbound HTTP request:

```csharp
public class NightlyRollup(BatchJobTracer batch)
{
    public async Task RunAsync()
    {
        using var activity = batch.StartBatch("nightly-claims-rollup");
        try
        {
            var count = await DoWork();
            batch.MarkSuccess(activity, itemsProcessed: count);
        }
        catch (Exception ex)
        {
            batch.MarkFailure(activity, ex);
            throw;
        }
    }
}
```

---

## Structured logging (OBS-05)

`AddCenteneObservability` wires the **OpenTelemetry logging provider** into the host's logging pipeline. Every `ILogger<T>` call automatically carries:

| Field | Source |
|---|---|
| `trace_id` | `Activity.Current.TraceId` |
| `span_id` | `Activity.Current.SpanId` |
| `service.name` | Resource attribute from `OTEL_SERVICE_NAME` |
| `runtime.environment` | Resource attribute from `CURRENT_RUNTIME_ENVIRONMENT` |
| `centene.observability.sdk.version` | Resource attribute |

No consumer code change required. Your existing logger calls just work:

```csharp
_logger.LogInformation("Processing claim {ClaimId}", claimId);
// → emits a log record with ClaimId AND trace_id/span_id/service.name/runtime.environment
```

Splunk parses these fields out of the JSON log line for trace-log correlation.

### Adding request-scoped enrichment

To attach extra fields to all logs within a scope (e.g. `member_id`, `operation`), use the `W3CLogEnricher` helper:

```csharp
using Centene.Enterprise.Observability.Logging;

using (W3CLogEnricher.BeginEnrichmentScope(_logger, new Dictionary<string, object?>
{
    ["member_id"] = phi.Mask(memberId),
    ["operation"] = "claim-process"
}))
{
    _logger.LogInformation("Processing claim");
    // all log entries inside this scope carry member_id and operation
}
```

### Reading the current trace context

```csharp
var (traceId, spanId) = W3CLogEnricher.GetCurrentTraceContext();
// useful for stamping trace IDs into outbound payloads or audit records
```

---

## PHI masking (OBS-08)

`PhiMaskingEnricher` is registered as a singleton. Use it to defensively mask PHI in any string before sending it to a log or telemetry attribute:

```csharp
public ClaimsHandler(PhiMaskingEnricher phi, ILogger<ClaimsHandler> logger)
{
    _logger.LogInformation("Processing claim for member {Member}", phi.Mask(memberName));
}
```

Patterns covered: SSN, MBI, email, phone, DOB, credit card. This is defense in depth — services SHOULD NOT log PHI directly. The Dynatrace attribute whitelist is the primary control.

---

## Dynatrace

### Finding your traces

Centene uses Dynatrace's SaaS solution at `https://sso.dynatrace.com`. SSO via your corporate email.

Search by `OTEL_SERVICE_NAME` in the search bar. If your workload doesn't show up:

- Exercise the application and wait 15–20 minutes for Dynatrace to index the name.
- Search by Kubernetes namespace.
- Search by AWS account name.
- Search the distributed tracing app for one of your endpoints like `/v1/members`.
- Work with the DT team if you still can't find it.

### Adding attributes

Custom trace attributes go through a Dynatrace whitelist to prevent inadvertent sensitive data leakage. Work with the DT team to register attribute names.

### Can't find your stuff?

1. Shell into the container and check `/var/log/dynatrace/oneagent/log`. Missing logs there usually mean injection failed.
2. Run OneAgent diagnostics on the host and inspect `monitored_entities/process_group_instances.json` for `AGENT_INJECTION_STATUS_NET_UNSUPPORTED_NET_VERSION`.

---

## Appendix

### Graceful shutdown

`AddCenteneObservability` registers an `IHostedService` that hooks `IHostApplicationLifetime.ApplicationStopping` and flushes the providers before the process exits. No manual flush required.

Override the timeout (default 5s):

```csharp
builder.Services.AddCenteneObservability(o => o.ShutdownTimeout = TimeSpan.FromSeconds(10));
```

### Debugging issues

Failed restore against the internal feed → confirm `nuget.config` and `NuGetAuthenticate@1` in the pipeline.

Traces missing in Dynatrace → call `/observability/selfcheck` first to confirm which backend was picked. The `reason` field is the fastest diagnostic.

No spans arriving in local Zipkin → confirm your Zipkin image has the OTLP receiver. See the **Local Zipkin Setup** section.

### Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `traceBackend: noop` in self-check | `CURRENT_RUNTIME_ENVIRONMENT` missing or invalid | Set it in deployment manifest |
| `otlpFallbackWarning: true` in prod | OneAgent injection failed | Check `/var/log/dynatrace/oneagent/log` |
| Duplicate spans in Dynatrace | OneAgent active **and** OTLP firing | Set `ONE_AGENT_ENABLED=false` if OneAgent is handling it |
| Metrics not in Dynatrace | Helm scrape annotations missing | Add annotations from this README |
| `traceparent` not propagated | Custom HTTP client bypassing `IHttpClientFactory` | Use `IHttpClientFactory.CreateClient()` |
| No spans in local Zipkin | Zipkin image lacks OTLP receiver | Use `openzipkin/zipkin-slim` (recent version) |
| Logs in Splunk lack `trace_id` | OBS-05 wiring missing | Confirm `AddCenteneObservability()` is called before `app.Build()` |