using Microsoft.EntityFrameworkCore;

namespace Cronus.DeliveryService.Data;

/// <summary>Applies pending migrations for the deployment job.</summary>
/// <remarks>
/// The job uses the tested service image under a separate identity with DDL privileges.
/// A non-zero exit stops the Argo CD sync before workloads roll out against an unapplied schema.
/// </remarks>
internal static class MigrationCommand
{
    /// <summary>The argument that selects this mode instead of serving requests.</summary>
    internal const string Name = "migrate";

    /// <summary>Category its log lines appear under, so they are recognisable in a job's output.</summary>
    internal const string LogCategory = "Cronus.DeliveryService.Migration";

    /// <summary>Recognises the exact migration token, case-insensitively, anywhere in the arguments.</summary>
    public static bool IsRequested(string[] args) =>
        args.Any(argument => string.Equals(argument, Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Applies any migrations that have not been applied yet.
    /// </summary>
    /// <returns>0 when the schema is up to date, 1 when it could not be brought up to date.</returns>
    public static async Task<int> RunAsync(
        DeliveryDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

            if (pending.Length == 0)
            {
                // The job runs on every sync, so this is the usual outcome and not worth an error.
                logger.LogInformation("No pending migrations; the schema is up to date.");
                return 0;
            }

            // Named before they are applied, so a job that fails says what it was attempting.
            logger.LogInformation(
                "Applying {Count} migration(s): {Migrations}",
                pending.Length,
                string.Join(", ", pending));

            await db.Database.MigrateAsync(cancellationToken);

            logger.LogInformation("Schema is up to date.");
            return 0;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Applying migrations failed.");
            return 1;
        }
    }
}
