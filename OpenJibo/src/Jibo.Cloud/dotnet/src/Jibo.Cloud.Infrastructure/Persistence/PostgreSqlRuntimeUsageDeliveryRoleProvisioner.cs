using Npgsql;

namespace Jibo.Cloud.Infrastructure.Persistence;

/// <summary>
/// Applies the separately reviewed runtime-usage delivery deployment artifact.
/// This is an administrative operation and must never run in the API host.
/// </summary>
public static class PostgreSqlRuntimeUsageDeliveryRoleProvisioner
{
    public const string OwnerRole = "openjibo_runtime_usage_delivery_owner";
    public const string CapabilityRole = "openjibo_runtime_usage_delivery";
    public const string LoginRole = "openjibo_runtime_usage";
    public const string WrapperSchema = "runtime_usage_delivery";

    private const string StateSchemaIdentifierToken = "{{STATE_SCHEMA_IDENTIFIER}}";
    private const string StateSchemaLiteralToken = "{{STATE_SCHEMA_LITERAL}}";
    private const string SourceLoginIdentifierToken = "{{SOURCE_LOGIN_IDENTIFIER}}";
    private const string SourceLoginLiteralToken = "{{SOURCE_LOGIN_LITERAL}}";

    public static async Task ProvisionAsync(
        string administratorConnectionString,
        string stateSchema,
        string sourceLoginRole,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeIdentifier(stateSchema))
            throw new ArgumentException("The configured state schema is not a safe PostgreSQL identifier.",
                nameof(stateSchema));
        if (string.Equals(stateSchema, WrapperSchema, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stateSchema, "pg_catalog", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stateSchema, "information_schema", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stateSchema, "pg_temp", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The configured state schema crosses a reserved delivery boundary.",
                nameof(stateSchema));
        if (!IsSafeIdentifier(sourceLoginRole))
            throw new ArgumentException("The source login role is not a safe PostgreSQL identifier.",
                nameof(sourceLoginRole));
        if (!string.Equals(sourceLoginRole, LoginRole, StringComparison.Ordinal))
            throw new ArgumentException($"The source login role must be '{LoginRole}'.", nameof(sourceLoginRole));

        if (string.IsNullOrWhiteSpace(administratorConnectionString))
            throw new ArgumentException("An administrator connection string is required.",
                nameof(administratorConnectionString));

        var administrator = new NpgsqlConnectionStringBuilder(administratorConnectionString);
        if (string.IsNullOrWhiteSpace(administrator.Database))
            throw new ArgumentException("The administrator connection must name a database.",
                nameof(administratorConnectionString));

        if (string.Equals(sourceLoginRole, OwnerRole, StringComparison.Ordinal) ||
            string.Equals(sourceLoginRole, CapabilityRole, StringComparison.Ordinal) ||
            string.Equals(sourceLoginRole, WrapperSchema, StringComparison.Ordinal))
            throw new ArgumentException("The source login cannot cross the delivery owner, capability, or wrapper schema boundary.",
                nameof(sourceLoginRole));

        var artifact = await ReadArtifactAsync(cancellationToken);
        artifact = artifact
            .Replace(StateSchemaIdentifierToken, QuoteIdentifier(stateSchema), StringComparison.Ordinal)
            .Replace(StateSchemaLiteralToken, QuoteLiteral(stateSchema), StringComparison.Ordinal)
            .Replace(SourceLoginIdentifierToken, QuoteIdentifier(sourceLoginRole), StringComparison.Ordinal)
            .Replace(SourceLoginLiteralToken, QuoteLiteral(sourceLoginRole), StringComparison.Ordinal);
        if (artifact.Contains("{{", StringComparison.Ordinal) || artifact.Contains("}}", StringComparison.Ordinal))
            throw new InvalidOperationException("The runtime-usage delivery deployment artifact contains an unresolved placeholder.");

        await using var connection = new NpgsqlConnection(administrator.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var schemaCheck = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_catalog.pg_namespace
                WHERE nspname = @schema
            )
            """, connection, transaction))
        {
            schemaCheck.Parameters.AddWithValue("schema", stateSchema);
            if (!Convert.ToBoolean(await schemaCheck.ExecuteScalarAsync(cancellationToken)))
                throw new InvalidOperationException($"Configured state schema '{stateSchema}' does not exist.");
        }

        await using (var command = new NpgsqlCommand(artifact, connection, transaction))
        {
            command.CommandTimeout = 0;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<string> ReadArtifactAsync(CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Deployment", "runtime-usage-delivery-role.sql"),
            Path.Combine(AppContext.BaseDirectory, "runtime-usage-delivery-role.sql"),
            Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), "infra", "postgresql",
                "runtime-usage-delivery-role.sql")
        };
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
                return await File.ReadAllTextAsync(candidate, cancellationToken);
        }

        throw new FileNotFoundException(
            "The runtime-usage delivery deployment artifact was not found.", candidates[0]);
    }

    private static string FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "infra", "postgresql",
                    "runtime-usage-delivery-role.sql")))
                return directory.FullName;
            directory = directory.Parent;
        }

        return start;
    }

    private static bool IsSafeIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 63 &&
        (IsAsciiLetter(value[0]) || value[0] == '_') &&
        value.All(character => IsAsciiLetter(character) ||
                               (character >= '0' && character <= '9') ||
                               character is '_' or '$' or '-');

    private static bool IsAsciiLetter(char value) =>
        (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string QuoteLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}
