using System;

namespace Conduit.Infrastructure;

public enum DatabaseProvider
{
    Sqlite,
    SqlServer,
    PostgreSql,
}

// The provider is resolved exactly once at startup into this typed value (no connection-string
// sniffing and no provider re-parsing in query paths). Unknown values fail fast naming the
// configuration key and the allowed values; there is no auto-detect fallback.
public static class DatabaseProviderResolver
{
    public static DatabaseProvider Resolve(string? providerName) =>
        providerName?.Trim().ToLowerInvariant() switch
        {
            "sqlite" => DatabaseProvider.Sqlite,
            "sqlserver" => DatabaseProvider.SqlServer,
            "postgresql" or "postgres" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException(
                $"Database provider '{providerName}' is unknown. Allowed values: sqlite, sqlserver, postgresql (configuration key 'Conduit:DatabaseProvider')."
            ),
        };
}
