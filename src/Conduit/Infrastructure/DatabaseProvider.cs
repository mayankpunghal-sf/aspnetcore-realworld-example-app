using System;

namespace Conduit.Infrastructure;

public enum DatabaseProvider
{
    SqlServer,
    PostgreSql,
    Sqlite,
}

/// <summary>
/// Parses the engine switch from configuration. The provider is resolved exactly once at
/// startup; a missing or unknown value fails fast instead of falling back to a default engine.
/// </summary>
public static class DatabaseProviderParser
{
    public const string ConfigKey = "Data:Provider";

    public static DatabaseProvider Parse(string? value)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "sqlserver":
                return DatabaseProvider.SqlServer;
            case "postgresql":
                return DatabaseProvider.PostgreSql;
            case "sqlite":
                return DatabaseProvider.Sqlite;
            default:
                throw new InvalidOperationException(
                    $"Configuration key '{ConfigKey}' is missing or invalid: '{value}'. "
                        + "Allowed values: SqlServer, PostgreSql, Sqlite."
                );
        }
    }
}

/// <summary>
/// Read-only view of the provider resolved at startup; immutable for the process lifetime.
/// Query paths consult this instead of sniffing connection strings or provider types.
/// </summary>
public interface IDatabaseProviderAccessor
{
    public DatabaseProvider Provider { get; }
}

public sealed class DatabaseProviderAccessor(DatabaseProvider provider) : IDatabaseProviderAccessor
{
    public DatabaseProvider Provider { get; } = provider;
}
