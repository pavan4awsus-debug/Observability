using System.Diagnostics;
using Centene.Enterprise.Observability.Environment;
using Centene.Enterprise.Observability.Extensions;
using Centene.Enterprise.Observability.Health;
using Centene.Enterprise.Observability.Metrics;
using Centene.Enterprise.Observability.Middleware;
using Centene.Enterprise.Observability.Tracing;

var builder = WebApplication.CreateBuilder(args);
var resolved = ObservabilityEnvironmentResolver.Resolve();

if (resolved.MeterEnvironment == ObservabilityEnvironment.Prometheus)
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(5000);
        options.ListenLocalhost(10550);
    });
}

// ONE call. Reads env, picks backend, wires everything.
builder.Services.AddCenteneObservability();

var app = builder.Build();
app.UseRouting();

app.UseMiddleware<RequestLoggingMiddleware>();

if (resolved.MeterEnvironment == ObservabilityEnvironment.Prometheus)
{
    app.MapPrometheusScrapingEndpoint();
}

var goldenSignals = app.Services.GetRequiredService<GoldenSignalMeter>();
var currentProcess = Process.GetCurrentProcess();
var lastCpuSample = currentProcess.TotalProcessorTime;
var lastCpuTimestamp = DateTime.UtcNow;

goldenSignals.RegisterSaturation("cpu", () =>
{
    var now = DateTime.UtcNow;
    var deltaCpu = currentProcess.TotalProcessorTime - lastCpuSample;
    var deltaSeconds = (now - lastCpuTimestamp).TotalSeconds;
    lastCpuSample = currentProcess.TotalProcessorTime;
    lastCpuTimestamp = now;

    if (deltaSeconds <= 0)
        return 0d;

    var cpuPercent = (deltaCpu.TotalMilliseconds / (deltaSeconds * Environment.ProcessorCount * 1000d)) * 100d;
    return Math.Clamp(cpuPercent, 0d, 100d);
});

goldenSignals.RegisterSaturation("memory", () =>
{
    var totalAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    if (totalAvailableMemoryBytes <= 0)
        return 0d;

    var usedMemoryPercent = (currentProcess.WorkingSet64 / (double)totalAvailableMemoryBytes) * 100d;
    return Math.Clamp(usedMemoryPercent, 0d, 100d);
});

var result =  app.MapCenteneObservabilitySelfCheck();
Console.WriteLine(result.ToString());

// Sample endpoint — demonstrates Golden Signal recording and tracing
var apiSource = new ActivitySource("Centene.SampleApi");

app.MapGet("/", (GoldenSignalMeter signals, ILogger<Program> logger) =>
{
    var sw = Stopwatch.StartNew();
    logger.LogInformation("Hello World endpoint called");
    logger.LogCritical("Hello World endpoint called");
    logger.LogWarning("Hello World endpoint called");
    logger.LogError("Hello World endpoint called");
    var result = "Hello World!";
    sw.Stop();
    signals.RecordRequest("/", 200, sw.Elapsed.TotalMilliseconds);
    Console.WriteLine($"Request to / took {sw.Elapsed.TotalMilliseconds} ms");
    return result;
});

app.MapGet("/members/{id}", (string id, GoldenSignalMeter signals, ILogger<Program> logger) =>
{
    logger.LogInformation("Member request started for id {MemberId}", id);
    var sw = Stopwatch.StartNew();
    using var span = apiSource.StartActivity("GetMember", ActivityKind.Server);
    span?.SetTag("member.id", id);

    // ... do work ...
    var result = Results.Ok(new { id, name = "Sample Member" });

    sw.Stop();
    signals.RecordRequest("/members/{id}", 200, sw.Elapsed.TotalMilliseconds);
    return result;
});

app.MapGet("/sample/random-outcome", (GoldenSignalMeter signals, ILogger<Program> logger) =>
{
    var sw = Stopwatch.StartNew();
    var outcome = Random.Shared.Next(3);

    IResult result;
    int statusCode;
    string outcomeName;

    switch (outcome)
    {
        case 0:
            statusCode = 200;
            outcomeName = "success";
            result = Results.Ok(new { status = outcomeName });
            break;
        case 1:
            statusCode = 400;
            outcomeName = "client-error";
            result = Results.BadRequest(new { status = outcomeName });
            throw new InvalidOperationException("Simulated client error for testing purposes.");
        default:
            statusCode = 500;
            outcomeName = "server-error";
            result = Results.Problem("Simulated server error.", statusCode: statusCode);
            throw new InvalidOperationException("Simulated server error for testing purposes.");

    }

    sw.Stop();
    string email = "test"+outcome+"@localhost.com";
    logger.LogInformation("User: {email} Random outcome endpoint  returned {Outcome} with status {StatusCode}", email, outcomeName, statusCode);
    signals.RecordRequest("/sample/random-outcome", statusCode, sw.Elapsed.TotalMilliseconds);
    return result;
});

app.MapPost("/claims/process", async (GoldenSignalMeter signals, BatchJobTracer batch) =>
{
    using var activity = batch.StartBatch("claims-process", correlationId: Guid.NewGuid().ToString());
    try
    {
        await Task.Delay(50); // simulated work
        batch.MarkSuccess(activity, itemsProcessed: 42);
        return Results.Accepted();
    }
    catch (Exception ex)
    {
        batch.MarkFailure(activity, ex);
        throw;
    }
});

app.Run();
