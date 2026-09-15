// using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Options;
// using OpenTelemetry.Metrics;
// using OpenTelemetry.Trace;
// using Centene.Enterprise.Observability.Configuration;

namespace Centene.Enterprise.Observability.Middleware;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
// using ObservabilityLib.Logging;

// namespace ObservabilityLib.Middleware;

public class RequestLoggingMiddleware1
{
    private readonly RequestDelegate _next;

    public RequestLoggingMiddleware1(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<RequestLoggingMiddleware1> logger)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            var path = context.Request.Path.Value;
            var method = context.Request.Method;
            var statusCode = context.Response.StatusCode;
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            // Structured log template so Dynatrace parses fields into separate columns
            logger.LogInformation(
                "HTTP {HttpMethod} {ApiPath} responded {StatusCode} in {ResponseTimeMs} ms",
                method, path, statusCode, elapsedMs);
        }
    }
}