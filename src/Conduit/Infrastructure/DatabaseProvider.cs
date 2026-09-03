using System;

namespace Conduit.Infrastructure;

public enum DatabaseProvider
{
    Sqlite,
    SqlServer,
    PostgreSql,
}

public static class DatabaseProviderParser
{
    public static DatabaseProvider Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // preserve the historical out-of-the-box sqlite run when no switch is configured
            return DatabaseProvider.Sqlite;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "sqlite" => DatabaseProvider.Sqlite,
            "sqlserver" => DatabaseProvider.SqlServer,
            "postgresql" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException(
                "Database provider unknown. Please check configuration: Data:Provider must be one of Sqlite, SqlServer, PostgreSql."
            ),
        };
    }
}
