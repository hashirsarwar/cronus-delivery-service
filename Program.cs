using System.Text.Json.Serialization;
using Cronus.DeliveryService.Data;
using Cronus.DeliveryService.Endpoints;
using Cronus.DeliveryService.Errors;
using Cronus.DeliveryService.Health;
using Cronus.DeliveryService.Services;
using Cronus.DeliveryService.Telemetry;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Register instrumentation early; no connection string keeps local runs free of Azure.
var applicationInsightsConnectionString =
    builder.Configuration[ApplicationTelemetry.ConnectionStringKey];
builder.Services.AddApplicationTelemetry(applicationInsightsConnectionString);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Fail at start-up with an actionable message rather than on the first request.
var connectionString = builder.Configuration.GetConnectionString("CronusDelivery");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'CronusDelivery' is not configured. Set ConnectionStrings:CronusDelivery in " +
        "appsettings.Development.json, or the ConnectionStrings__CronusDelivery environment variable.");
}

// Default to Entra authentication so deployments cannot use a stored password by omission.
// Development overrides this for local PostgreSQL.
var useEntraAuthentication = builder.Configuration.GetValue("Database:UseEntraAuthentication", true);

// Reject conflicting authentication settings before the first connection attempt.
PostgresDataSourceFactory.Validate(connectionString, useEntraAuthentication);

// Resolve the host logger through DI and let the singleton dispose its connection pool on shutdown.
builder.Services.AddSingleton(serviceProvider => PostgresDataSourceFactory.Create(
    connectionString,
    useEntraAuthentication,
    serviceProvider.GetRequiredService<ILoggerFactory>()));

builder.Services.AddDbContext<DeliveryDbContext>((serviceProvider, options) =>
    options.UseNpgsql(serviceProvider.GetRequiredService<NpgsqlDataSource>()));


if (MigrationCommand.IsRequested(args))
{
    await using var migrationHost = builder.Build();
    await using var migrationScope = migrationHost.Services.CreateAsyncScope();
    var migrationServices = migrationScope.ServiceProvider;

    return await MigrationCommand.RunAsync(
        migrationServices.GetRequiredService<DeliveryDbContext>(),
        migrationServices.GetRequiredService<ILoggerFactory>().CreateLogger(MigrationCommand.LogCategory));
}

builder.Services.AddScoped<DeliveryManager>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgresql", tags: [HealthEndpoints.ReadyTag]);

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Distinguish missing telemetry configuration from configured telemetry that fails to export.
app.Logger.LogInformation(
    "Application telemetry is {TelemetryState} for {ServiceName}.",
    ApplicationTelemetry.IsConfigured(applicationInsightsConnectionString) ? "enabled" : "disabled",
    ApplicationTelemetry.ServiceName);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapDeliveryEndpoints();

// Convenience so a fresh clone runs with one command. Migrations remain the source of truth,
// and this service has no reference data to seed.
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();


return 0;
