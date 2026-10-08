using Cronus.DeliveryService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cronus.DeliveryService.Tests.Infrastructure;

/// <summary>Boots the real host against the test database.</summary>
/// <remarks>
/// Program reads settings before WebApplicationFactory contributes configuration sources, so use environment variables.
/// The Testing environment skips Development-only database initialization, leaving the suite in control.
/// </remarks>
internal sealed class DeliveryApiFactory(string? databaseConnectionString = null)
    : WebApplicationFactory<Program>
{
    /// <summary>Creates a host whose database cannot be reached, to simulate an outage.</summary>
    public static DeliveryApiFactory WithUnreachableDatabase() =>
        new(TestDatabase.UnreachableConnectionString);

    static DeliveryApiFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__CronusDelivery", TestDatabase.ConnectionString);

        // Testing has no Development override, so disable the default Entra mode for local PostgreSQL.
        Environment.SetEnvironmentVariable("Database__UseEntraAuthentication", "false");

        // Clear inherited telemetry configuration so test traffic cannot reach a real Azure resource.
        Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", null);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (databaseConnectionString is null)
        {
            return;
        }

        // Remove existing options first: AddDbContext uses TryAdd and would retain the original registration.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<DeliveryDbContext>>();
            services.AddDbContext<DeliveryDbContext>(options => options.UseNpgsql(databaseConnectionString));
        });
    }
}
