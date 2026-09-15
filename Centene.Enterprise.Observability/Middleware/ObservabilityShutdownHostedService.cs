namespace Centene.Enterprise.Observability.Middleware;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly string ServerHostName = Environment.MachineName;
    private static readonly int LogicalProcessors = Environment.ProcessorCount;

    public RequestLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<RequestLoggingMiddleware> logger)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;

        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var startingCpuTime = process.TotalProcessorTime;
        Exception? capturedException = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            capturedException = ex;
            
            // Populate OpenTelemetry semantic keys on the active Activity/Span
            var currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                currentActivity.SetStatus(ActivityStatusCode.Error, ex.Message);
                currentActivity.SetTag("error.type", ex.GetType().FullName);
                currentActivity.SetTag("exception.type", ex.GetType().FullName);
                currentActivity.SetTag("exception.message", ex.Message);
                currentActivity.SetTag("exception.stacktrace", ex.ToString());
            }

            throw; // Rethrow for global handler pipeline
        }
        finally
        {
            stopwatch.Stop();

            // FIX: Ensure unhandled exceptions correctly register as 500 in logs
            var statusCode = capturedException != null ? 500 : context.Response.StatusCode;
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;

            // Calculate CPU utilization delta for the process during request handling
            var endingCpuTime = process.TotalProcessorTime;
            var cpuTimeMs = (endingCpuTime - startingCpuTime).TotalMilliseconds;

            double cpuUtilizationPercent = elapsedSeconds <= 0
                ? 0d
                : Math.Clamp(
                    (cpuTimeMs / 1000d) / (elapsedSeconds * LogicalProcessors) * 100d,
                    0d,
                    100d);

            using (logger.BeginScope(new Dictionary<string, object?>
            {
                ["http.method"] = method,
                ["http.route"] = path,
                ["http.status_code"] = statusCode,
                ["http.response_time_ms"] = elapsedMs,
                ["host.name"] = ServerHostName,
                ["process.name"] = process.ProcessName,
                ["process.pid"] = process.Id,
                ["process.cpu.time_ms"] = cpuTimeMs,
                ["process.cpu.utilization_percent"] = Math.Round(cpuUtilizationPercent, 2),
                ["system.cpu.logical_processors"] = LogicalProcessors,
                ["error.type"] = capturedException?.GetType().FullName
            }))
            {
                // FIX: Pass the exception object to LogError so standard providers ingest the stack trace
                if (capturedException != null)
                {
                    logger.LogError(capturedException, "HTTP request failed due to an unhandled exception");
                }
                else if (statusCode >= 500)
                {
                    logger.LogError("HTTP request failed with status code {StatusCode}", statusCode);
                }
                else if (statusCode >= 400)
                {
                    logger.LogWarning("HTTP request returned client error {StatusCode}", statusCode);
                }
                else
                {
                    logger.LogInformation("HTTP request completed with status code {StatusCode}", statusCode);
                }
            }
        }
    }
}