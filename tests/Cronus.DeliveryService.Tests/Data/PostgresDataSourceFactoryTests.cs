using Azure.Core;
using Cronus.DeliveryService.Data;
using Cronus.DeliveryService.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Cronus.DeliveryService.Tests.Data;

[TestClass]
public sealed class PostgresDataSourceFactoryTests
{
    /// <summary>
    /// A connection string with no credential, which is what Azure uses. The username is the
    /// PostgreSQL role, which is named after the managed identity that presents the token.
    /// </summary>
    private const string EntraConnectionString =
        "Host=psql-cronus-nonprod.postgres.database.azure.com;Database=cronus_dev_delivery;" +
        "Username=id-delivery-dev;Ssl Mode=Require";

    /// <summary>Answers with a fixed token and records what it was asked for.</summary>
    private sealed class StubCredential(string token) : TokenCredential
    {
        public List<string> RequestedScopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            Record(requestContext);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Record(requestContext));

        private AccessToken Record(TokenRequestContext requestContext)
        {
            RequestedScopes.AddRange(requestContext.Scopes);
            return new AccessToken(token, DateTimeOffset.MaxValue);
        }
    }

    [TestMethod]
    public async Task FetchAccessTokenAsync_RequestsThePostgreSqlResource()
    {
        var credential = new StubCredential("token");

        await PostgresDataSourceFactory.FetchAccessTokenAsync(credential, CancellationToken.None);

        // Pinned because a token issued for any other resource is refused by the server, and the
        // failure appears as an authentication error rather than as anything naming the scope.
        CollectionAssert.AreEqual(
            new[] { "https://ossrdbms-aad.database.windows.net/.default" },
            credential.RequestedScopes);
    }

    [TestMethod]
    public async Task FetchAccessTokenAsync_ReturnsTheToken_WhichIsUsedAsThePassword()
    {
        var credential = new StubCredential("a-token-from-entra");

        var password = await PostgresDataSourceFactory.FetchAccessTokenAsync(credential, CancellationToken.None);

        Assert.AreEqual("a-token-from-entra", password);
    }

    [TestMethod]
    public void Validate_RejectsAStoredPassword_WhenEntraAuthenticationIsEnabled()
    {
        var withPassword = EntraConnectionString + ";Password=not-needed";

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => PostgresDataSourceFactory.Validate(withPassword, useEntraAuthentication: true));

        StringAssert.Contains(exception.Message, "must not carry a credential");
    }

    [TestMethod]
    public void Validate_RejectsAMissingUsername_WhenEntraAuthenticationIsEnabled()
    {
        const string withoutUsername = "Host=example.postgres.database.azure.com;Database=cronus_dev_delivery";

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => PostgresDataSourceFactory.Validate(withoutUsername, useEntraAuthentication: true));

        StringAssert.Contains(exception.Message, "Username has to name that role");
    }

    [TestMethod]
    public void Validate_AcceptsAConnectionString_WithNoCredential()
    {
        PostgresDataSourceFactory.Validate(EntraConnectionString, useEntraAuthentication: true);
    }

    [TestMethod]
    public void Validate_AllowsAPassword_WhenEntraAuthenticationIsDisabled()
    {
        // The check belongs to the Entra path only. A password is how a server outside Azure is
        // reached, and it stays supported.
        PostgresDataSourceFactory.Validate(
            "Host=localhost;Database=cronus_delivery;Username=me;Password=secret",
            useEntraAuthentication: false);
    }

    [TestMethod]
    public async Task Create_WithoutEntraAuthentication_UsesTheConnectionStringAsGiven()
    {
        // The Development path: no credential, no token, and the local server reached exactly as the
        // connection string describes. The database is created first so that this test does not
        // depend on another having run before it.
        await TestDatabase.EnsureCreatedAsync();

        await using var dataSource = PostgresDataSourceFactory.Create(
            TestDatabase.ConnectionString,
            credential: null,
            NullLoggerFactory.Instance);

        await using var connection = await dataSource.OpenConnectionAsync();

        Assert.AreEqual(System.Data.ConnectionState.Open, connection.State);
    }

    [TestMethod]
    public async Task Create_WithACredential_StillBuildsAUseableDataSource()
    {
        // This checks pool construction without an Azure server; separate tests cover token acquisition.
        // Remove stored credentials first so the token provider is valid on both developer machines and CI.
        await using var dataSource = PostgresDataSourceFactory.Create(
            WithoutStoredCredential(TestDatabase.ConnectionString),
            new StubCredential("token"),
            NullLoggerFactory.Instance);

        Assert.IsInstanceOfType<NpgsqlDataSource>(dataSource);
    }

    /// <summary>
    /// A stored credential and a token contradict each other, and the factory reports that as a
    /// configuration mistake rather than letting Npgsql fail with a message about password providers
    /// that never mentions the connection string.
    /// </summary>
    [TestMethod]
    public void Create_WithACredentialAndAStoredPassword_RejectsTheCombination()
    {
        var withPassword = new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString)
        {
            Password = "never-used",
        }.ConnectionString;

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            PostgresDataSourceFactory.Create(
                withPassword,
                new StubCredential("token"),
                NullLoggerFactory.Instance));

        // The message has to name the keyword that is wrong, since the connection string it refers to
        // is not in it.
        StringAssert.Contains(exception.Message, "must not carry a credential");
        StringAssert.Contains(exception.Message, "Password");
    }

    /// <summary>Removes any stored credential, leaving every other keyword as it was.</summary>
    private static string WithoutStoredCredential(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            Password = null,
            Passfile = null,
        }.ConnectionString;
}
