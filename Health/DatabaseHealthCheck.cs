using Cronus.DeliveryService.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cronus.DeliveryService.Health;

/// <summary>Reports PostgreSQL reachability for readiness.</summary>
/// <remarks>A database outage must withhold traffic, not trigger a restart loop.</remarks>
internal sealed class DatabaseHealthCheck(
    DeliveryDbContext db,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    /// <summary>Kept short so an unresponsive database fails the probe instead of hanging it.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>Returned to the caller, so it stays coarse and names no host.</summary>
    private const string Unreachable = "PostgreSQL is unreachable.";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            return await db.Database.CanConnectAsync(timeout.Token)
                ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy(Unreachable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The probe itself gave up waiting, which says nothing about the database, so the
            // cancellation is left to propagate rather than being reported as a failed check.
            throw;
        }
        catch (Exception exception)
        {
            // The coarse description is what the caller sees; the detail, which may name an internal
            // host or port, is only logged.
            logger.LogError(exception, "The PostgreSQL readiness check failed.");

            return HealthCheckResult.Unhealthy(Unreachable);
        }
    }
}
