using Cronus.DeliveryService.Data;
using Cronus.DeliveryService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cronus.DeliveryService.Tests.Data;

/// <summary>
/// The migration job decides whether the deployment may continue from this command's exit code, so
/// these tests cover the argument that selects the mode and the exit code it produces.
/// </summary>
[TestClass]
public sealed class MigrationCommandTests
{
    [TestMethod]
    public void IsRequested_WhenTheArgumentIsMigrate_ReturnsTrue() =>
        Assert.IsTrue(MigrationCommand.IsRequested(["migrate"]));

    [TestMethod]
    public void IsRequested_IsCaseInsensitive() =>
        Assert.IsTrue(MigrationCommand.IsRequested(["MIGRATE"]));

    [TestMethod]
    public void IsRequested_WhenMigrateAccompaniesOtherArguments_ReturnsTrue() =>
        Assert.IsTrue(MigrationCommand.IsRequested(["--urls", "http://+:8080", "migrate"]));

    [TestMethod]
    public void IsRequested_WhenNothingSelectsMigration_ReturnsFalse() =>
        Assert.IsFalse(MigrationCommand.IsRequested([]));

    /// <summary>
    /// Only the exact token counts. A prefix or a flag that merely contains the word must start the
    /// service, because mistaking one for the mode would apply the schema instead of serving.
    /// </summary>
    [TestMethod]
    public void IsRequested_WhenAnArgumentMerelyResemblesMigrate_ReturnsFalse() =>
        Assert.IsFalse(MigrationCommand.IsRequested(["migrations", "--migrate-once"]));

    /// <summary>
    /// The shared test database is migrated once for the whole run, so by the time this executes the
    /// schema is already up to date and there is nothing left to apply. That is also the state the
    /// job finds on every sync after the first.
    /// </summary>
    [TestMethod]
    public async Task RunAsync_WhenTheSchemaIsAlreadyUpToDate_ReturnsSuccess()
    {
        await TestDatabase.EnsureCreatedAsync();
        await using var db = TestDatabase.CreateContext();

        var exitCode = await MigrationCommand.RunAsync(db, NullLogger.Instance);

        Assert.AreEqual(0, exitCode);
    }

    /// <summary>
    /// A migration that cannot be applied has to fail the job, or the deployment would proceed
    /// against a schema that was never brought up to date.
    /// </summary>
    [TestMethod]
    public async Task RunAsync_WhenTheDatabaseIsUnreachable_ReturnsFailure()
    {
        await using var db = CreateUnreachableContext();

        var exitCode = await MigrationCommand.RunAsync(db, NullLogger.Instance);

        Assert.AreEqual(1, exitCode);
    }

    private static DeliveryDbContext CreateUnreachableContext() =>
        new(new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseNpgsql(TestDatabase.UnreachableConnectionString)
            .Options);
}
