using Npgsql;

namespace Jibo.Cloud.Infrastructure.Persistence;

/// <summary>Applies the separately reviewed, read-only shadow-source artifact. Never call from a runtime host.</summary>
public static class PostgreSqlRuntimeUsageShadowSourceRoleProvisioner
{
    public const string OwnerRole = "openjibo_runtime_usage_shadow_source_owner";
    public const string CapabilityRole = "openjibo_runtime_usage_shadow_source_reader";
    public const string LoginRole = "openjibo_runtime_usage_shadow_source";
    public const string WrapperSchema = "runtime_usage_shadow_source";
    private const string SchemaIdentifierToken = "{{STATE_SCHEMA_IDENTIFIER}}";
    private const string SchemaLiteralToken = "{{STATE_SCHEMA_LITERAL}}";

    public static async Task ProvisionAsync(string administratorConnectionString, string stateSchema,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeIdentifier(stateSchema) || IsReserved(stateSchema))
            throw new ArgumentException("The configured state schema crosses the shadow-source boundary.", nameof(stateSchema));
        if (string.IsNullOrWhiteSpace(administratorConnectionString))
            throw new ArgumentException("An administrator connection string is required.", nameof(administratorConnectionString));
        var builder = new NpgsqlConnectionStringBuilder(administratorConnectionString);
        if (string.IsNullOrWhiteSpace(builder.Database))
            throw new ArgumentException("The administrator connection must name a database.", nameof(administratorConnectionString));

        var artifact = (await ReadArtifactAsync(cancellationToken))
            .Replace(SchemaIdentifierToken, QuoteIdentifier(stateSchema), StringComparison.Ordinal)
            .Replace(SchemaLiteralToken, QuoteLiteral(stateSchema), StringComparison.Ordinal);
        if (artifact.Contains("{{", StringComparison.Ordinal) || artifact.Contains("}}", StringComparison.Ordinal))
            throw new InvalidOperationException("The shadow-source deployment artifact contains an unresolved placeholder.");

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var schema = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace WHERE nspname=@schema)", connection, transaction))
        {
            schema.Parameters.AddWithValue("schema", stateSchema);
            if (!Convert.ToBoolean(await schema.ExecuteScalarAsync(cancellationToken)))
                throw new InvalidOperationException($"Configured state schema '{stateSchema}' does not exist.");
        }
        await using (var command = new NpgsqlCommand(artifact, connection, transaction))
        {
            command.CommandTimeout = 0;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool IsReserved(string value) => value.Equals(WrapperSchema, StringComparison.OrdinalIgnoreCase) ||
        value.Equals("pg_catalog", StringComparison.OrdinalIgnoreCase) || value.Equals("information_schema", StringComparison.OrdinalIgnoreCase) || value.Equals("pg_temp", StringComparison.OrdinalIgnoreCase);
    private static bool IsSafeIdentifier(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 63 &&
        (char.IsAsciiLetter(value[0]) || value[0] == '_') && value.All(x => char.IsAsciiLetter(x) || char.IsAsciiDigit(x) || x is '_' or '$' or '-');
    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    private static string QuoteLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static async Task<string> ReadArtifactAsync(CancellationToken cancellationToken)
    {
        var candidates = new[] { Path.Combine(AppContext.BaseDirectory, "Deployment", "runtime-usage-shadow-source-role.sql"), Path.Combine(AppContext.BaseDirectory, "runtime-usage-shadow-source-role.sql"), Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), "infra", "postgresql", "runtime-usage-shadow-source-role.sql") };
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            if (File.Exists(candidate)) return await File.ReadAllTextAsync(candidate, cancellationToken);
        throw new FileNotFoundException("The runtime usage shadow-source deployment artifact was not found.", candidates[0]);
    }
    private static string FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "infra", "postgresql", "runtime-usage-shadow-source-role.sql"))) return directory.FullName;
        return start;
    }
}
