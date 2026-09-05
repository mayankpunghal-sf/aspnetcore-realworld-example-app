using System;
using Microsoft.Extensions.Configuration;

namespace Conduit.Infrastructure;

public enum DatabaseProvider
{
    Sqlite,
    SqlServer,
    PostgreSql,
}

/// <summary>
/// The database provider and connection string, resolved exactly once at startup from
/// configuration. No query-path code inspects the provider; it only ever sees this value.
/// </summary>
public record DatabaseProviderSelection(DatabaseProvider Provider, string ConnectionString)
{
    private const string CanonicalSwitchKey = "Data:Provider";
    private const string LegacySwitchKey = "Conduit:DatabaseProvider";
    private const string LegacyConnectionStringKey = "Conduit:ConnectionString";

    public static DatabaseProviderSelection Resolve(IConfiguration configuration)
    {
        var rawProvider =
            configuration[CanonicalSwitchKey] ?? configuration[LegacySwitchKey] ?? "sqlite";

        var provider = rawProvider.Trim().ToLowerInvariant() switch
        {
            "sqlite" => DatabaseProvider.Sqlite,
            "sqlserver" => DatabaseProvider.SqlServer,
            "postgresql" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException(
                $"Invalid database provider '{rawProvider}' (configure '{CanonicalSwitchKey}' or the legacy '{LegacySwitchKey}'). Allowed values: sqlite, sqlserver, postgresql."
            ),
        };

        var connectionString = provider switch
        {
            DatabaseProvider.SqlServer => RequireConnectionString(
                configuration,
                "ConnectionStrings:AppDb_SqlServer"
            ),
            DatabaseProvider.PostgreSql => RequireConnectionString(
                configuration,
                "ConnectionStrings:AppDb_PostgreSql"
            ),
            _ => NotEmpty(configuration[LegacyConnectionStringKey]) ?? "Filename=realworld.db",
        };

        return new DatabaseProviderSelection(provider, connectionString);
    }

    // The per-engine named connection string wins; the legacy shared key is still accepted for
    // backward compatibility with the existing docker-compose wiring. It must be non-empty
    // before the application can start on a relational provider.
    private static string RequireConnectionString(IConfiguration configuration, string namedKey)
    {
        var connectionString =
            NotEmpty(configuration[namedKey]) ?? NotEmpty(configuration[LegacyConnectionStringKey]);

        if (connectionString is null)
        {
            throw new InvalidOperationException(
                $"No connection string configured for the selected database provider. Set '{namedKey}' (or the legacy '{LegacyConnectionStringKey}') to a non-empty value."
            );
        }

        return connectionString;
    }

    private static string? NotEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
