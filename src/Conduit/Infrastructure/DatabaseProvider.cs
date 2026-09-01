namespace Conduit.Infrastructure;

// the two engines this application supports. The active engine is resolved exactly once at
// startup from the Data:Provider configuration key; there is no default engine and no
// auto-detection, so a missing or unknown value fails fast (see Program.cs).
public enum DatabaseProvider
{
    SqlServer,
    PostgreSql,
}

// immutable per process: read this instead of sniffing connection strings or provider names
// at query time.
public interface IDatabaseProviderAccessor
{
    public DatabaseProvider Provider { get; }
}

public class DatabaseProviderAccessor(DatabaseProvider provider) : IDatabaseProviderAccessor
{
    public DatabaseProvider Provider { get; } = provider;
}
