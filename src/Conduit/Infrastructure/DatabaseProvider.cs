using System;
using Microsoft.Extensions.Configuration;

namespace Conduit.Infrastructure;

public enum DatabaseProvider
{
    Sqlite,
    SqlServer,
    PostgreSql,
}

public record DatabaseProviderSelection(DatabaseProvider Provider, string ConnectionString)
{
    public static DatabaseProviderSelection Resolve(IConfiguration configuration)
    {
        var providerValue =
            Configured(configuration, "Data:Provider")
            ?? Configured(configuration, "Conduit:DatabaseProvider")
            ?? "sqlite";

        var provider = providerValue.ToLowerInvariant().Trim() switch
        {
            "sqlite" => DatabaseProvider.Sqlite,
            "sqlserver" => DatabaseProvider.SqlServer,
            "postgresql" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException(
                $"Database provider '{providerValue}' unknown. Set Data:Provider (or legacy Conduit:DatabaseProvider) to one of: sqlite, sqlserver, postgresql."
            ),
        };

        var connectionString = provider switch
        {
            DatabaseProvider.SqlServer => NonEmpty(
                Configured(configuration, "ConnectionStrings:AppDb_SqlServer")
                    ?? Configured(configuration, "Conduit:ConnectionString"),
                "SQL Server provider selected: set connection string 'AppDb_SqlServer' (or legacy Conduit:ConnectionString)."
            ),
            DatabaseProvider.PostgreSql => NonEmpty(
                Configured(configuration, "ConnectionStrings:AppDb_PostgreSql")
                    ?? Configured(configuration, "Conduit:ConnectionString"),
                "PostgreSQL provider selected: set connection string 'AppDb_PostgreSql' (or legacy Conduit:ConnectionString)."
            ),
            _ => Configured(configuration, "Conduit:ConnectionString") ?? "Filename=realworld.db",
        };

        return new DatabaseProviderSelection(provider, connectionString);
    }

    private static string? Configured(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key]) ? null : configuration[key];

    private static string NonEmpty(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }

        return value;
    }
}
