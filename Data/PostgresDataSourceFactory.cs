using Azure.Core;
using Azure.Identity;
using Npgsql;

namespace Cronus.DeliveryService.Data;

/// <summary>Builds a pool using Entra tokens or the local connection string.</summary>
/// <remarks>
/// Periodic refresh avoids embedding an expiring token in configuration.
/// <see cref="DefaultAzureCredential"/> supports AKS workload identity and local Azure sign-in.
/// See README.md for authentication modes and deployment wiring.
/// </remarks>
internal static class PostgresDataSourceFactory
{
    /// <summary>PostgreSQL requires this token audience; other resource scopes fail authentication.</summary>
    internal const string EntraResourceScope = "https://ossrdbms-aad.database.windows.net/.default";

    /// <summary>Refreshes the pool token every five minutes to stay ahead of normal expiry.</summary>
    /// <remarks><see cref="DefaultAzureCredential"/> caches tokens, so most refreshes avoid an Entra request.</remarks>
    private static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long to wait before asking again after a failed attempt, so a transient failure to reach
    /// Entra ID recovers in seconds rather than in <see cref="TokenRefreshInterval"/>.
    /// </summary>
    private static readonly TimeSpan FailedRefreshRetryInterval = TimeSpan.FromSeconds(10);

    /// <summary>Rejects conflicting authentication settings at start-up.</summary>
    /// <remarks>Entra mode requires a username and forbids stored passwords and password files.</remarks>
    public static void Validate(string connectionString, bool useEntraAuthentication)
    {
        if (!useEntraAuthentication)
        {
            return;
        }

        // Parsing here also catches a malformed connection string at start-up rather than at the
        // first connection attempt.
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);

        RejectStoredCredential(parsed);

        if (string.IsNullOrWhiteSpace(parsed.Username))
        {
            throw new InvalidOperationException(
                "The connection string does not set Username while Database:UseEntraAuthentication is enabled. " +
                "The token is authenticated against the PostgreSQL role whose name matches the identity, so " +
                "Username has to name that role, for example id-delivery-dev.");
        }
    }

    /// <summary>Rejects stored credentials when an Entra token provider is used.</summary>
    /// <remarks>Names the offending keyword before Npgsql emits a generic password-provider error.</remarks>
    private static void RejectStoredCredential(NpgsqlConnectionStringBuilder parsed)
    {
        var keyword = !string.IsNullOrEmpty(parsed.Password) ? "Password"
            : !string.IsNullOrEmpty(parsed.Passfile) ? "Passfile"
            : null;

        if (keyword is null)
        {
            return;
        }

        throw new InvalidOperationException(
            "The connection string must not carry a credential while Microsoft Entra authentication is " +
            $"enabled, and it sets {keyword}. Azure Database for PostgreSQL authenticates with an access " +
            "token, and Npgsql cannot register a token provider while a stored credential is present, " +
            $"because it would not know which one to use. Remove {keyword}, or set " +
            "Database:UseEntraAuthentication to false to connect with a password instead.");
    }

    /// <summary>
    /// Creates the data source the application and its contexts connect through.
    /// </summary>
    public static NpgsqlDataSource Create(
        string connectionString,
        bool useEntraAuthentication,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        Validate(connectionString, useEntraAuthentication);

        return Create(
            connectionString,
            useEntraAuthentication ? new DefaultAzureCredential() : null,
            loggerFactory);
    }

    /// <summary>Accepts a supplied credential so tests can inspect token use without reaching Entra.</summary>
    internal static NpgsqlDataSource Create(
        string connectionString,
        TokenCredential? credential,
        ILoggerFactory? loggerFactory)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);

        if (credential is not null)
        {
            // Direct credential callers bypass Validate, so enforce the stored-credential rule here too.
            RejectStoredCredential(new NpgsqlConnectionStringBuilder(connectionString));

            builder.UsePeriodicPasswordProvider(
                (_, cancellationToken) => FetchAccessTokenAsync(credential, cancellationToken),
                TokenRefreshInterval,
                FailedRefreshRetryInterval);
        }

        if (loggerFactory is not null)
        {
            // Without this the data source would report nothing to the application's logging, so a
            // connection failure would be silent until it surfaced as a failed request.
            builder.UseLoggerFactory(loggerFactory);
        }

        return builder.Build();
    }

    /// <summary>
    /// Exchanges the credential for an access token and returns it as the password PostgreSQL
    /// expects.
    /// </summary>
    internal static async ValueTask<string> FetchAccessTokenAsync(
        TokenCredential credential,
        CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(
            new TokenRequestContext([EntraResourceScope]),
            cancellationToken);

        return token.Token;
    }
}
