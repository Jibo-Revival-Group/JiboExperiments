namespace Jibo.Cloud.Tests.Infrastructure;

internal static class PostgreSqlIsolatedStateMigrationScripts
{
    private const string SharedSchemaMigration = "014_harden_sigv4_replay_observer.state.sql";

    internal static IEnumerable<string> GetPaths(string directory, string? excludedFileName = null) =>
        Directory.GetFiles(directory, "*.sql")
            .Where(path => !path.EndsWith(".personal-memory.sql", StringComparison.OrdinalIgnoreCase))
            // Migration 014 intentionally replaces a function in public. Running it from
            // parallel, schema-isolated tests mutates shared database state and races.
            // PostgreSqlAwsSigV4ReplayObservationStoreTests applies 014 in the
            // nonparallel privileged collection to cover its security-definer behavior.
            .Where(path => !Path.GetFileName(path).Equals(SharedSchemaMigration,
                StringComparison.OrdinalIgnoreCase))
            .Where(path => excludedFileName is null ||
                           !Path.GetFileName(path).Equals(excludedFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
}
