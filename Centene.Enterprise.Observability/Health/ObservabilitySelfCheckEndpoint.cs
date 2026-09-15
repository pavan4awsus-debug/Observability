using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Centene.Enterprise.Observability.Configuration;
using Centene.Enterprise.Observability.Environment;
using Microsoft.AspNetCore.Routing;

namespace Centene.Enterprise.Observability.Health;

/// <summary>
/// Maps the /observability/selfcheck endpoint, returning the resolved
/// ConfiguredEnvironment as JSON. Lets operators verify which backend
/// was picked at startup without re-reading env vars.
/// </summary>
public static class ObservabilitySelfCheckEndpoint
{
    public static IEndpointConventionBuilder MapCenteneObservabilitySelfCheck(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
        var result = endpoints.MapGet(options.SelfCheckPath, (HttpContext ctx) =>
        {
            var env = ctx.RequestServices.GetRequiredService<ConfiguredEnvironment>();
            Console.WriteLine($"[ObservabilitySelfCheck] {env.ServiceName} {env.RuntimeEnvironment} {env.TraceEnvironment} {env.MeterEnvironment} {env.Reason}");
            var result = Results.Json(new
            {
                serviceName = env.ServiceName,
                runtimeEnvironment = env.RuntimeEnvironment,
                traceBackend = ObservabilityEnvironmentNames.ToCanonical(env.TraceEnvironment),
                meterBackend = ObservabilityEnvironmentNames.ToCanonical(env.MeterEnvironment),
                reason = env.Reason,
                otlpFallbackWarning = env.OtlpFallbackWarning,
                sdkVersion = typeof(ObservabilitySelfCheckEndpoint).Assembly.GetName().Version?.ToString()
            });
            Console.WriteLine($"[ObservabilitySelfCheck] {env.ServiceName} {env.RuntimeEnvironment} {env.TraceEnvironment} {env.MeterEnvironment} {env.Reason}");
            return result;
        });
        Console.WriteLine(result.ToString());
        return result;
    }
}
