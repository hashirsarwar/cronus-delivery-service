using Cronus.DeliveryService.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cronus.DeliveryService.Tests.Infrastructure;

/// <summary>Creates and migrates the test database once, then empties it between tests.</summary>
/// <remarks>The default name differs from Development; keep overrides pointed at a dedicated test database.</remarks>
internal static class TestDatabase
{
    private const string AdministrativeDatabase = "postgres";
    private const string MigrationsHistoryTable = "__EFMigrationsHistory";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _initialised;

    /// <summary>
    /// Points at the local PostgreSQL server by default, matching how the application runs in
    /// Development. Override <c>CRONUS_TEST_CONNECTION_STRING</c> to run the suite elsewhere.
    /// </summary>
    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("CRONUS_TEST_CONNECTION_STRING")
        ?? "Host=localhost;Port=5432;Database=cronus_delivery_tests";

    /// <summary>Uses localhost port 1 and a short timeout to simulate an unreachable database.</summary>
    public const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=none;Timeout=1;Pooling=false";

    public static DbContextOptions<DeliveryDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

    public static DeliveryDbContext CreateContext() => new(CreateOptions());

    /// <summary>Empties the schema, then returns a context ready for the test to arrange its own data.</summary>
    public static async Task<DeliveryDbContext> CreateCleanContextAsync()
    {
        await ResetAsync();
        return CreateContext();
    }

    /// <summary>
    /// Empties every table except the migrations history. The table list is read from the catalog so
    /// that a future migration cannot silently leave stale rows behind.
    /// </summary>
    public static async Task ResetAsync()
    {
        await EnsureCreatedAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var tables = new List<string>();

        await using (var select = new NpgsqlCommand(
            $"SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename <> '{MigrationsHistoryTable}'",
            connection))
        await using (var reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        if (tables.Count == 0)
        {
            return;
        }

        var quoted = string.Join(", ", tables.Select(table => $"\"{table}\""));
        await using var truncate = new NpgsqlCommand($"TRUNCATE TABLE {quoted} RESTART IDENTITY CASCADE", connection);
        await truncate.ExecuteNonQueryAsync();
    }

    /// <summary>Creates the database and applies migrations. Safe to call from every test.</summary>
    public static async Task EnsureCreatedAsync()
    {
        if (_initialised)
        {
            return;
        }

        await Gate.WaitAsync();

        try
        {
            if (_initialised)
            {
                return;
            }

            await CreateDatabaseIfMissingAsync();

            await using var db = CreateContext();
            await db.Database.MigrateAsync();

            _initialised = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task CreateDatabaseIfMissingAsync()
    {
        var databaseName = new NpgsqlConnectionStringBuilder(ConnectionString).Database
            ?? throw new InvalidOperationException($"'{nameof(ConnectionString)}' must include a database name.");

        var administrative = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = AdministrativeDatabase,
        };

        await using var connection = new NpgsqlConnection(administrative.ConnectionString);
        await connection.OpenAsync();

        await using (var exists = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name", connection))
        {
            exists.Parameters.AddWithValue("name", databaseName);

            if (await exists.ExecuteScalarAsync() is not null)
            {
                return;
            }
        }

        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await create.ExecuteNonQueryAsync();
    }
}
