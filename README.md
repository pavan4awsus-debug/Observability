# Centene Enterprise Observability — .NET SDK Project

**Purpose:** the .NET reference implementation of the Centene Observability standards. One NuGet package that any .NET service can install to get logging, metrics, distributed tracing, PHI masking, and a self-check endpoint wired up correctly for the local, dev, test, prod, and AWS runtime environments.

This README is for **anyone who wants to know what this package is, what it does, where it fits in the EA Governance picture, and how to use it.** Engineers integrating the SDK should read `Centene.Enterprise.Observability/README.md`. Architects and EA reviewers should read `docs/SDK-overview-for-teams.md`.

---

## What's in this repo

```
Centene.Enterprise.Observability.sln          ← solution
│
├── Centene.Enterprise.Observability/         ← the SDK (NuGet package)
│   ├── Configuration/                        ← ObservabilityOptions
│   ├── Environment/                          ← Resolver + ConfiguredEnvironment + enum
│   ├── Enrichers/                            ← PhiMaskingEnricher (OBS-08)
│   ├── Extensions/                           ← AddCenteneObservability() entry point
│   ├── Health/                               ← /observability/selfcheck endpoint
│   ├── Hosting/                              ← Graceful shutdown hosted service
│   ├── Logging/                              ← W3CLogEnricher (OBS-05)
│   ├── Metrics/                              ← GoldenSignalMeter (OBS-04)
│   ├── Tracing/                              ← BatchJobTracer (OBS-07)
│   ├── README.md                             ← engineer-facing docs
│   ├── METADATA.yaml                         ← EA governance metadata
│   └── Centene.Enterprise.Observability.csproj
│
├── Centene.Observability.SampleApi/          ← reference consuming app
│   ├── Program.cs
│   ├── appsettings.json
│   └── Properties/launchSettings.json        ← profiles for local/ut/dev/test
│
├── docs/                                     ← governance + team docs
│   ├── SDK-overview-for-teams.md             ← give this to teams asking what we built
│   ├── ADR-001-adopt-autobootstrap.md        ← architecture decision record
│   ├── observability-standards-coverage.md   ← OBS-XX standard → SDK component map
│   └── purpose-where-how.md                  ← short FAQ for stakeholders
│
└── pipelines/
    ├── azure-pipelines.yml                   ← NuGet publish pipeline
    └── nuget.config                          ← Azure Artifacts feed config
```

---

## What it does, in one paragraph

When a .NET service starts up, it calls one method — `services.AddCenteneObservability()`. The SDK reads two env vars (`CURRENT_RUNTIME_ENVIRONMENT`, `OTEL_SERVICE_NAME`), probes the runtime to see if Dynatrace OneAgent has been injected, picks the right backend (Zipkin locally, OneAgent in CKP, OTLP fallback if needed, noop if nothing fits), wires up OpenTelemetry tracing + metrics + logging with the correct exporters, registers a PHI masking helper, exposes a `/observability/selfcheck` endpoint, and hooks application shutdown so telemetry flushes cleanly. The contract — env var names, backend selection rules, log field names, metric names — is **identical to the Go module** at `gitlab.centene.com/ea/application/app/observability/golang/observability`. That's the point: a service in any language gets the same telemetry experience and Dynatrace operators don't have to know what language wrote the spans.

---

## Where this fits in the EA Governance picture

Per the EA Governance Artifact Organization Guide:

- **Tier:** 3 — Accelerator
- **Type:** Reusable Library → Shared SDK
- **Repository location:** `/reusable-libraries/`
- **CODEOWNERS:** `@ea-team`
- **Lifecycle:** DRAFT → PROPOSED → APPROVED → ACTIVE
- **Standards implemented:** OBS-04 (Golden Signals), OBS-05 (Structured Logging), OBS-07 (Distributed Tracing), OBS-08 (PHI Masking)
- **Governing ADR:** ADR-001 in `docs/`

---

## Quick links

- **I'm an engineer adding observability to my .NET service** → [SDK README](Centene.Enterprise.Observability/README.md)
- **I'm a team lead and someone forwarded me screenshots** → [SDK overview for teams](docs/SDK-overview-for-teams.md)
- **I want the standards-to-code map** → [Standards coverage](docs/observability-standards-coverage.md)
- **I want the architecture decision** → [ADR-001](docs/ADR-001-adopt-autobootstrap.md)
- **I want a one-pager for a stakeholder** → [Purpose, Where, How](docs/purpose-where-how.md)
