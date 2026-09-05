using System;
using Microsoft.Extensions.Configuration;

namespace Conduit.Infrastructure;

public enum DatabaseProvider
{
    Sqlite,
    SqlServer,
    PostgreSql,
}

// resolved once at startup into an immutable (provider, connection string) pair;
// no connection-string sniffing and no runtime provider checks in query paths
public record DatabaseProviderSelection(DatabaseProvider Provider, string ConnectionString)
{
    private const string ProviderKey = "Data:Provider";
    private const string LegacyProviderKey = "Conduit:DatabaseProvider";
    private const string SqlServerConnectionStringKey = "ConnectionStrings:AppDb_SqlServer";
    private const string PostgreSqlConnectionStringKey = "ConnectionStrings:AppDb_PostgreSql";
    private const string LegacyConnectionStringKey = "Conduit:ConnectionString";
    private const string DefaultSqliteConnectionString = "Filename=realworld.db";

    public static DatabaseProviderSelection Resolve(IConfiguration configuration)
    {
        var providerValue =
            configuration[ProviderKey] ?? configuration[LegacyProviderKey] ?? "sqlite"; // preserves the pre-port local/docker default (no default-engine rule waived; documented in the port report)

        var provider = providerValue.ToLowerInvariant().Trim() switch
        {
            "sqlite" => DatabaseProvider.Sqlite,
            "sqlserver" => DatabaseProvider.SqlServer,
            "postgresql" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException(
                $"Database provider '{providerValue}' is unknown. Check configuration key '{ProviderKey}' (or legacy '{LegacyProviderKey}'). Allowed values: sqlite, sqlserver, postgresql."
            ),
        };

        var connectionString = provider switch
        {
            DatabaseProvider.SqlServer => configuration[SqlServerConnectionStringKey]
                ?? configuration[LegacyConnectionStringKey],
            DatabaseProvider.PostgreSql => configuration[PostgreSqlConnectionStringKey]
                ?? configuration[LegacyConnectionStringKey],
            DatabaseProvider.Sqlite => string.IsNullOrWhiteSpace(
                configuration[LegacyConnectionStringKey]
            )
                ? DefaultSqliteConnectionString
                : configuration[LegacyConnectionStringKey],
            _ => throw new InvalidOperationException(
                $"Database provider {provider} is not supported."
            ),
        };

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No connection string is configured for database provider {provider}. Set '{SqlServerConnectionStringKey}' / '{PostgreSqlConnectionStringKey}' (or legacy '{LegacyConnectionStringKey}')."
            );
        }

        return new DatabaseProviderSelection(provider, connectionString);
    }
}
