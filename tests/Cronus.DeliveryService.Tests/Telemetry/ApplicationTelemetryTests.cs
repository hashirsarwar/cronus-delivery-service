using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Cronus.DeliveryService.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace Cronus.DeliveryService.Tests.Telemetry;

/// <summary>
/// Covers the one decision the telemetry setup makes: whether to configure Azure Monitor at all.
/// </summary>
/// <remarks>
/// The connection string here is structurally valid but points nowhere. Nothing is exported, because
/// no host is built and no provider is resolved — the assertions are about what was registered, which
/// is what decides whether local runs and the test suite touch Azure.
/// </remarks>
[TestClass]
public sealed class ApplicationTelemetryTests
{
    /// <summary>
    /// A well-formed connection string for a resource that does not exist. The instrumentation key is
    /// the all-zero GUID, so nothing could be ingested even if something did try to export.
    /// </summary>
    private const string PlaceholderConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://localhost/;LiveEndpoint=https://localhost/";

    [TestMethod]
    public void IsConfigured_WithNoConnectionString_IsFalse() =>
        Assert.IsFalse(ApplicationTelemetry.IsConfigured(null));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void IsConfigured_WithABlankConnectionString_IsFalse(string connectionString) =>
        Assert.IsFalse(ApplicationTelemetry.IsConfigured(connectionString));

    [TestMethod]
    public void IsConfigured_WithAConnectionString_IsTrue() =>
        Assert.IsTrue(ApplicationTelemetry.IsConfigured(PlaceholderConnectionString));

    [TestMethod]
    public void AddApplicationTelemetry_WithoutAConnectionString_RegistersNothing()
    {
        var services = new ServiceCollection();
        var before = services.Count;

        services.AddApplicationTelemetry(null);

        // Not merely "no exporter": nothing at all is added, so an application with no connection
        // string carries no OpenTelemetry services and no background export loop.
        Assert.AreEqual(before, services.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void AddApplicationTelemetry_WithABlankConnectionString_RegistersNothing(string connectionString)
    {
        var services = new ServiceCollection();
        var before = services.Count;

        services.AddApplicationTelemetry(connectionString);

        Assert.AreEqual(before, services.Count);
    }

    [TestMethod]
    public void AddApplicationTelemetry_WithAConnectionString_ConfiguresAzureMonitor()
    {
        var services = new ServiceCollection();

        services.AddApplicationTelemetry(PlaceholderConnectionString);

        // IConfigureOptions<AzureMonitorOptions> is registered by UseAzureMonitor, so its presence is
        // what proves the distro was wired rather than merely the OpenTelemetry SDK.
        Assert.IsTrue(
            services.Any(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<AzureMonitorOptions>)),
            "UseAzureMonitor should have registered Azure Monitor options.");
    }

    [TestMethod]
    public void AddApplicationTelemetry_WithAConnectionString_ListensForNpgsqlSpans()
    {
        var services = new ServiceCollection();
        // The distro reads its connection string from configuration as well as from options, and
        // resolves configuration while the tracer provider is being built.
        services.AddSingleton<IConfiguration>(new ConfigurationManager());
        services.AddLogging();
        services.AddApplicationTelemetry(PlaceholderConnectionString);

        using var provider = services.BuildServiceProvider();
        // Resolving the tracer provider is what builds the trace pipeline. No host is started, so the
        // distro never attaches its exporting processor and nothing leaves the process.
        using var tracerProvider = provider.GetRequiredService<TracerProvider>();
        using var npgsqlActivitySource = new ActivitySource("Npgsql");

        // AddNpgsql subscribes the pipeline to this source by name. Asking the source whether anyone
        // is listening is what proves the call reached the pipeline, rather than merely that the
        // package is referenced.
        Assert.IsTrue(
            npgsqlActivitySource.HasListeners(),
            "AddNpgsql should have subscribed the trace pipeline to the Npgsql activity source.");
    }

    [TestMethod]
    public void ServiceName_MatchesTheAssemblyName()
    {
        // Compare with the assembly name so renaming either side alone breaks the identity contract.
        var assemblyName = typeof(ApplicationTelemetry).Assembly.GetName().Name;

        Assert.AreEqual(ApplicationTelemetry.ServiceName, assemblyName);
    }
}
