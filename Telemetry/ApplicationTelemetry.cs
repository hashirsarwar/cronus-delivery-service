using Azure.Monitor.OpenTelemetry.AspNetCore;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Cronus.DeliveryService.Telemetry;

/// <summary>Configures Azure Monitor telemetry when a connection string is supplied.</summary>
/// <remarks>
/// With no connection string, local runs need no Azure services.
/// The OpenTelemetry logging provider supplements console logging, preserving container logs.
/// </remarks>
internal static class ApplicationTelemetry
{
    /// <summary>Stable <c>cloud_RoleName</c> matching the image and Helm release.</summary>
    /// <remarks>Keeping it constant avoids splitting service history by environment or configuration.</remarks>
    internal const string ServiceName = "cronus-delivery-service";

    /// <summary>Deployment-supplied ingestion connection string; it grants no access to stored data.</summary>
    internal const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>Whether a connection string was supplied, and so whether telemetry will be exported.</summary>
    internal static bool IsConfigured(string? connectionString) => !string.IsNullOrWhiteSpace(connectionString);

    /// <summary>
    /// Adds Azure Monitor telemetry to the service collection when a connection string is configured,
    /// and does nothing at all when one is not.
    /// </summary>
    internal static IServiceCollection AddApplicationTelemetry(this IServiceCollection services, string? connectionString)
    {
        if (!IsConfigured(connectionString))
        {
            return services;
        }

        services
            .AddOpenTelemetry()
            // Include query timing in request traces, but redact SQL text before export.
            .WithTracing(tracing => tracing
                .AddNpgsql()
                .AddProcessor(new DatabaseStatementRedactingProcessor()))
            // Propagate W3C context across inbound requests and outbound HTTP calls.
            .UseAzureMonitor(options => options.ConnectionString = connectionString)
            // Register the service name last so Azure Monitor resource detectors cannot replace it.
            .ConfigureResource(resource => resource.AddService(ServiceName));

        return services;
    }
}
