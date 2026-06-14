# Group OOBE Port Notes

Date: 2026-06-13

This note records the low-risk pieces ported from `group/oobe` into the current
tree, along with the deliberate exclusions. The point is to keep a paper trail
for later review of security, stability, and long-term maintainability.

## Ported In This Pass

- `OpenJibo/src/Directory.Build.props`
  - excludes stale root-owned build artifact directories from compilation
  - intended as a build-safety guard, not a behavior change
- `OpenJibo/src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Infrastructure/Persistence/PersistenceBackendKind.cs`
  - adds `Sqlite` as a persistence backend enum value
- `OpenJibo/src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Infrastructure/Persistence/PersistenceSnapshotStoreFactory.cs`
  - wires the SQLite snapshot store into the existing factory
- `OpenJibo/src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Infrastructure/Persistence/SqliteSnapshotStore.cs`
  - adds a minimal SQLite snapshot backend for JSON snapshot round-tripping
- `OpenJibo/src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Infrastructure/Jibo.Cloud.Infrastructure.csproj`
  - adds the SQLite package dependency
- `OpenJibo/src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/Program.cs`
  - enables permissive CORS for local browser-based tooling
- `OpenJibo/scripts/cloud/start-dotnet-with-node-cert.sh`
  - makes cert-chain handling more robust for local development
  - removes stale root-owned build artifact directories before startup
- `OpenJibo/tests/Jibo.Cloud.Tests/Infrastructure/PersistenceStoreTests.cs`
  - adds a backend wiring test for SQLite

## Deliberately Held Back

- OOBE/account/loop-member protocol behavior
- robot identity auto-alignment rules
- update and backup state machine changes
- machine-specific `appsettings.json` edits
- branch-specific README claims about what is already complete

## Review Notes

- The SQLite backend is intentionally simple and snapshot-oriented.
- The permissive CORS change is useful for local tooling, but it should be
  reviewed before any production deployment.
- The build-directory cleanup is a local development safeguard and should stay
  narrowly scoped to the known stale-artifact patterns.
- The branch-specific OOBE behavior still needs a separate security and
  long-term design review before it is folded into the product path.
