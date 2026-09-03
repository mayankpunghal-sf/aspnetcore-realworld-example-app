# DUAL_DB_PORT_REPORT.md

## 1. Summary
Ported the Conduit RealWorld API (EF Core 10) to run on SQL Server **and** PostgreSQL behind one config switch (`Data:Provider`), keeping the existing Sqlite engine as a third selectable value. The codebase contained zero raw SQL, ADO.NET, or migrations — the port is a provider-seam + configuration port plus a case-sensitivity (B6) parity fix across LINQ comparisons. Build green (0 warnings), 16/16 integration tests pass (InMemory). Live SQL Server / PostgreSQL verification was **not** possible in this environment (no docker daemon) — listed as unverified paths per R11.

## 2. File sweep
Queue: all `*.cs`, `*.json`, `*.csproj`, `*.props`, `*.yml`, CI workflows, Makefile, docker files (no `.sql`, `.edmx`, `.hbm.xml`, or migration folders exist). Classification via one combined data-access grep (`SqlConnection|SqlCommand|ExecuteReader|ExecuteScalar|ExecuteNonQuery|ConnectionString|IDbConnection|FromSqlRaw|SqlException|InMemory|UseSqlServer|UseNpgsql`) + `graphify affected ConduitContext` cross-check (19 handler files + pipeline behavior + SliceFixture confirmed as the full ConduitContext consumer set).

| File | Class | Changes / reason |
|---|---|---|
| src/Conduit/Program.cs | data-access | provider switch restructured: typed enum, named conn strings, fail-fast validation |
| src/Conduit/Infrastructure/DatabaseProvider.cs | data-access | **new** — enum, parser, IDatabaseProviderAccessor seam |
| src/Conduit/appsettings.json | config | **new** — named conn strings as placeholders |
| src/Conduit/Conduit.csproj | config | Npgsql EF provider ref + scoped NoWarn (see §6) |
| Directory.Packages.props | config | Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 |
| src/Conduit/packages.lock.json, tests/.../packages.lock.json | config | lock updates from restore |
| src/Conduit/Features/** (12 files) | data-access | B6 ToLower() parity at all Username/Email/Tag comparison sites |
| src/Conduit/Infrastructure/ConduitContext.cs | data-access | reviewed, no change needed (model portable as-is) |
| src/Conduit/Infrastructure/DBContextTransactionPipelineBehavior.cs | data-access | reviewed — rollback+rethrow, no §5.5 violation |
| tests/Conduit.IntegrationTests/SliceFixture.cs | data-access | unaffected — InMemory direct, engine-agnostic |
| docker-compose.yml, launchSettings.json, Makefile | config | explicit `Data__Provider`/conn-string env vars (fail-fast compatible) |
| .github/workflows/*.yml | config | reviewed — CI flows run via Makefile targets (covered) |
| all remaining .cs (controllers, envelopes, mappers, security, errors, domain, build) | unaffected | zero data-access signature hits |

## 3. Inventory
- `Program.cs:15-45` (old) string-sniffed provider env var, hardcoded `Filename=realworld.db` → **Redesign** (rewired via typed enum; R7).
- 22 LINQ `Username/Email/TagId ==` comparison sites across 12 feature files → **Portable** (B6: `lower(col) = lower(@p)` translated on all three providers).
- No `NOLOCK`, no `ExecuteScalar`, no readers/DataTables, no `SqlException` catches, no MARS, no `rowversion`, no Guid PKs, no decimals, no bulk paths, no stored procs, no in-transaction catch-and-continue. All verified absent by grep + graphify cross-check.

## 4. Config changes
- Switch: `Data:Provider` = `SqlServer` | `PostgreSql` | `Sqlite` (case-insensitive). Missing/invalid → `InvalidOperationException` naming key + allowed values. No default engine in code.
- Connection strings (placeholders, no secrets): `AppDb_SqlServer`, `AppDb_PostgreSql`, `AppDb_Sqlite` side by side in `src/Conduit/appsettings.json`; startup asserts the selected one is present and non-empty.
- Env mapping: `Data__Provider`, `ConnectionStrings__AppDb_*` (docker-compose, launchSettings, Makefile).

## 5. Seams introduced
| Type | Responsibility | Registration site |
|---|---|---|
| `DatabaseProvider` (enum) | typed engine identity | — |
| `DatabaseProviderParser` | config key `Data:Provider` → enum, fail fast | Program.cs (composition root) |
| `IDatabaseProviderAccessor` / impl | immutable resolved provider for query paths | Program.cs, singleton |

ISqlDialect / ISqlQueryProvider / IBulkInserter / IDbConnectionFactory deliberately **not** introduced: no raw SQL, ADO.NET, or bulk paths exist (would be dead code; §11 EF Core recipe applies instead).

## 6. SQL changes
No SQL statements existed to fork or promote. The only SQL-affecting change: EF-generated `WHERE Username = @p` → `WHERE LOWER(Username) = LOWER(@p)` at the 22 B6 sites — one portable statement per R8 (LOWER() exists on SQL Server, PostgreSQL, and SQLite). Analyzer conflict resolved with a scoped `<NoWarn>CA1304;CA1311;CA1862` + justification comment in Conduit.csproj: `ToLower()` inside LINQ-to-SQL translates to `LOWER()` (culture is irrelevant in the DB); `ToLowerInvariant()` is not translatable by any provider.

## 7. Type mapping
Unchanged from EF defaults — all portable: int PKs → `int`/`integer`, string PKs → `nvarchar`/`varchar`, `byte[] Hash/Salt` → `varbinary`/`bytea`, `DateTime` UTC → `datetime2`/`timestamptz` (Npgsql maps `Kind=Utc` to `timestamptz`; all writes are `DateTime.UtcNow`, reads restored to Utc by the existing model converter). No lossy mappings, no `EnableLegacyTimestampBehavior`.

## 8. R9 flags (no-equivalent constructs)
None blocking. Minor deviations logged as flags in `PORTING_AGENT_REPORT.json`:
1. **Sqlite kept as third enum value** — skill enum names SqlServer|PostgreSql only; existing default engine preserved (R1-analog: don't break current run-local flows) and documented here. Dropping it would break `make run-local` and the hurl/bruno CI flows.
2. **PG string-equality case sensitivity** — SQL Server CI unique semantics now replicated at the app layer (B6 lower() everywhere); PG DB-level unique indexes would remain CS, but the model defines none (uniqueness is app-enforced). Tag/Slug `==` comparisons left case-sensitive: slugs are generated lowercase in .NET; tags are matched as-authored. Cross-case tag lookups on PG will not match (on SQL Server they would) — residual B8 divergence, judged negligible for the RealWorld spec flows.
3. **Live-engine verification gap** — see §10.

## 9. Reserved words
Entity/table/column names (Persons, Articles, Username, Email, Hash, Salt, Slug, CreatedAt, …) checked against the PG reserved list — no collisions. EF Core quotes PascalCase identifiers consistently on PG (identifier_style=preserve).

## 10. Verification
- `dotnet run --project build/build.csproj -- test` (format + Release build + tests): **pass**, 0 warnings, 16/16 tests (InMemory provider).
- SDK note: pinned SDK 10.0.302 was not installed; installed locally via dotnet-install (repo `global.json` untouched). The SDK was installed outside the repo tree so CSharpier would not walk it.
- **Unverified paths (R11):** no live SQL Server or PostgreSQL instance was reachable (docker daemon not running in this environment). PG smoke of startup + `EnsureCreated` + Npgsql DDL must be run before cutover: `docker compose up` with `Data__Provider=PostgreSql` + `ConnectionStrings__AppDb_PostgreSql`.
- Sequence re-sync runbook item: N/A (no identity-override usage; EnsureCreated + EF identity handled per provider).

## 11. Rollout prerequisites
- PostgreSQL ≥ 16 (target pg_version 16); no extensions required (no uuid/citext/postgis usage).
- Auth model: user/password in `AppDb_PostgreSql` (SCRAM) — no Windows auth in use.
- Set `Search Path` not required (default `public` matches pg_schema; pooled sessions untouched by session-level GUCs here).
- Pooling: Npgsql defaults; tune `Maximum Pool Size` per environment if needed.
- Retry-on-`40001`: not required (READ COMMITTED only).
- Confidence: **93** — formula: `100 × (26 pass / 28 applicable) − 10 × 0 no-equivalent − 5 × 0 warnings = 92.86 ≈ 93`; flagged items: per-provider migration sets (N/A — none exist), live-engine test execution (gap stated).
