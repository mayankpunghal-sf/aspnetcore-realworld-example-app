# Dual-Target Data Access Port Report — Conduit (SQL Server + PostgreSQL)

**Skill:** `dual-db-port` · **Repo:** github.com/mayankpunghal-sf/aspnetcore-realworld-example-app @ a397d11 · **Date:** 2026-09-05 · **Iteration:** 1 · **Confidence:** 86/100

## Summary

PostgreSQL is now switchable behind one config key (`Data:Provider`, values `sqlite | sqlserver | postgresql`, legacy env passthrough `Conduit:DatabaseProvider` still honored) with the engine selected once at startup into a typed value and zero recompile. SQL Server and SQLite paths are byte-for-byte behavior-preserving; the only per-engine divergence is `timestamp with time zone` column typing on the four `DateTime` audit columns when Npgsql is active (required by Npgsql 6+ strict `DateTime.Kind` mapping, §5.9). The codebase contains no ADO.NET, raw SQL, stored procedures, or migrations — the entire port surface is EF Core model/config wiring, so no Appendix A/B/C or §5.1–5.8/5.10–5.13 constructs exist to convert (each verified by grep, not assumed).

## Changes

| File | Change | Reason |
|---|---|---|
| `Directory.Packages.props` | + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 | Latest stable matching EF Core 10.0.10 (11.x is preview-only); resolved via NuGet check, not memory |
| `src/Conduit/Conduit.csproj` | + package reference | Enable Npgsql provider |
| `src/Conduit/Infrastructure/DatabaseProvider.cs` | **new** — `DatabaseProvider` enum + `DatabaseProviderSelection.Resolve(IConfiguration)` | §2/R7: provider resolved once at startup into a typed value; fail-fast names the key and allowed values on unknown input; empty-string-aware so config placeholders don't shadow legacy fallbacks |
| `src/Conduit/Program.cs` | hardcoded `sqlite` locals + string-comparison `AddDbContext` lambda → typed `switch` with `UseNpgsql` branch | R7/R10; `UseSqlServer`/`UseSqlite` calls unchanged (R1). Also wires the previously-dead docker-compose env passthrough (`ASPNETCORE_Conduit_DatabaseProvider`/`_ConnectionString`) to real config reads |
| `src/Conduit/Infrastructure/ConduitContext.cs` | `Database.IsNpgsql()` fork → `timestamp with time zone` on `Article.CreatedAt/UpdatedAt`, `Comment.CreatedAt/UpdatedAt` | §5.9: write path stores `DateTime.UtcNow` (Kind=Utc); Npgsql 6+ throws on `timestamp without time zone`. Read-side `SpecifyKind(Utc)` conversions preserved for SQLite |
| `src/Conduit/appsettings.json` | **new** — empty-string placeholders for `AppDb_SqlServer` / `AppDb_PostgreSql` | R5: named connection strings side by side, no credentials; env vars override |
| `packages.lock.json` ×2 | Npgsql entries via restore | AGENTS.md lock-file requirement |
| `.gitignore` | + `graphify-out/` | Build-cache hygiene |

Provider resolution order: `Data:Provider` → legacy `Conduit:ConnectionString`… i.e. `Conduit:DatabaseProvider` → default `sqlite`. Connection strings: SqlServer = `ConnectionStrings:AppDb_SqlServer` → legacy `Conduit:ConnectionString` → fail fast; PostgreSql = `ConnectionStrings:AppDb_PostgreSql` → legacy `Conduit:ConnectionString` → fail fast; Sqlite = `Conduit:ConnectionString` → `"Filename=realworld.db"` (byte-identical prior default).

## Deviations / decisions

- **Default engine `sqlite` kept** (deviation from §2 "no default engine"): preserves existing local/docker behavior (R1 spirit) — a deployment that previously ran sqlite unchanged keeps running sqlite; unknown values still fail fast naming the key. Documented here rather than silently applied.
- **No §2 ADO seams built** (`IDbConnectionFactory`/`ISqlDialect`/`ISqlQueryProvider`/`IBulkInserter`): the repo has zero raw SQL, zero ADO.NET, zero bulk paths — these interfaces would be dead scaffolding (R2/R10). The typed provider enum + EF Core's `UseNpgsql`/`IsNpgsql()` forking model (§11) covers every divergence that exists.

## Flags / open risks

1. **Per-provider migration sets (§7): flagged.** The repo has no migrations — schema is created by `EnsureCreated()` at startup and in tests. PostgreSQL schema therefore comes from EnsureCreated with the timestamptz fork applied; if migrations are introduced later, a separate PG migration set must be created at that point (per [EF multi-provider docs](https://learn.microsoft.com/ef/core/managing-schemas/migrations/providers)).
2. **`pg_version` unverified.** No postgres image tag, CI service, or IaC pin exists anywhere in the repo/infra (grep-proven). Default **16** assumed; the port uses nothing version-gated (no JSON functions, no MERGE, no generated columns), so this does not affect the written code — but confirm the real deployment target before relying on version-specific behavior.
3. **Engines not exercised (R11).** Neither PostgreSQL nor SQL Server was connectable during this run; validation is the standard suite (16/16) on EF InMemory. First PG connection should smoke-test: provider resolution from env vars, EnsureCreated with timestamptz columns, `DateTime.UtcNow` write round-trip.

## Verified portable by evidence (no change needed)

0 ADO.NET types / raw SQL / sprocs / `TransactionScope` / `NOLOCK` / `SqlException` handlers / `ExecuteScalar` / DataReaders / DataTables / Guid PKs (all PKs `int`; Tag PK `string`) — one combined grep across all `*.cs` plus graphify call-graph cross-check (`explain ConduitContext`, 44 edges, matched the grep-derived usage set). Isolation is `ReadCommitted` only (ConduitContext.cs:103) — portable per §5.6. No connection-string translation between engines (§5.13).

## Checklist (§9) results

Pass: R1, R2, R5, R7, R10, R12, R13, §2 typed seam, §5.4/§5.6 evidence-pass, §5.9 fork, §6 config, §11 EF Core subsection, report/gitignore hygiene. Flagged: §7 migration sets, R11 engine coverage, pg_version. Fail: none. N/A: §5.1–5.3/5.5/5.7–5.13 constructs, §8.1/8.2, Appendix A–D, sprocs.

**Confidence: 86** = 100 × (19/22) − 10×0 − 5×0 (no R9 "no equivalent" constructs; 0 build warnings on changed files; 3 flagged items counted as not passed).

## Prior-run artifacts consulted

- `C:\Users\mayank.punghal\Desktop\SIA\artifacts\5df29ed7-48ff-47b9-80f3-a880c4e7a016` — same-repo port; its §5.9 break analysis and package resolution informed this run's plan (re-verified against this clone).
- `C:\Users\mayank.punghal\Desktop\SIA\artifacts\b2fb74b9-2fbd-46cb-8e80-47b52fb205ab` — established the pinned Npgsql EF provider version and environment gotchas reused here.
