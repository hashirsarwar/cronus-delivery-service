using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cronus.DeliveryService.Health;

public static class HealthEndpoints
{
    /// <summary>
    /// Tag identifying the checks that gate readiness rather than liveness. Shared with the
    /// registration in <c>Program</c> so the two cannot drift apart.
    /// </summary>
    public const string ReadyTag = "ready";

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Keep dependencies out of liveness: restarting the process cannot fix a database outage.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteJsonAsync,
        })
        .WithTags("Health")
        .WithName("GetLiveness")
        .WithSummary("Reports that the process is running. Runs no dependency checks.");

        // Readiness answers whether traffic should be routed here, so it adds the dependency checks.
        // Failing readiness removes the instance from rotation; it does not restart it.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteJsonAsync,
        })
        .WithTags("Health")
        .WithName("GetReadiness")
        .WithSummary("Reports whether the service can serve traffic, including database reachability.");

        return app;
    }

    /// <summary>
    /// Writes the report as JSON. A probe only reads the status code, but a person checking an
    /// endpoint by hand gets the per-check detail.
    /// </summary>
    private static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                }),
        };

        return context.Response.WriteAsJsonAsync(payload);
    }
}
