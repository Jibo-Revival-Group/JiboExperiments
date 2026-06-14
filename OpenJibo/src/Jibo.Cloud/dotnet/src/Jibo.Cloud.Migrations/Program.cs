using System.Security.Cryptography;
using System.Text;
using Npgsql;

var options = MigrationOptions.Parse(args);
if (options.ShowHelp)
{
    Console.WriteLine(MigrationOptions.HelpText);
    return 0;
}

MigrationTarget[] targets = options.Target switch
{
    MigrationTarget.All => [MigrationTarget.State, MigrationTarget.PersonalMemory],
    _ => [options.Target]
};

var scriptsDirectory = ResolveScriptsDirectory(options.ScriptsDirectory);
var scripts = Directory.EnumerateFiles(scriptsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (scripts.Length == 0)
{
    Console.Error.WriteLine($"No PostgreSQL migration scripts found in '{scriptsDirectory}'.");
    return 1;
}

foreach (var target in targets)
{
    var connectionString = options.ResolveConnectionString(target);
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine($"No connection string was provided for target '{target}'.");
        return 1;
    }

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await EnsureTrackingTableAsync(connection);

    var applied = await LoadAppliedScriptsAsync(connection);

    foreach (var scriptPath in scripts)
    {
        var scriptName = Path.GetFileName(scriptPath);
        var scriptText = await File.ReadAllTextAsync(scriptPath);
        var checksum = ComputeChecksum(scriptText);

        if (applied.TryGetValue(scriptName, out var knownChecksum))
        {
            if (!string.Equals(knownChecksum, checksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Migration script '{scriptName}' has changed after being applied to '{target}'.");

            if (options.Verbose) Console.WriteLine($"[{target}] already applied: {scriptName}");
            continue;
        }

        if (options.PreviewOnly)
        {
            Console.WriteLine($"[{target}] would apply: {scriptName}");
            continue;
        }

        Console.WriteLine($"[{target}] applying: {scriptName}");
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand(scriptText, connection, transaction))
        {
            command.CommandTimeout = 0;
            await command.ExecuteNonQueryAsync();
        }

        await using (var insert = new NpgsqlCommand("""
            INSERT INTO OpenJiboSchemaMigrations (ScriptName, Checksum, AppliedUtc)
            VALUES (@scriptName, @checksum, NOW())
            ON CONFLICT (ScriptName) DO UPDATE SET
                Checksum = EXCLUDED.Checksum,
                AppliedUtc = EXCLUDED.AppliedUtc
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("@scriptName", scriptName);
            insert.Parameters.AddWithValue("@checksum", checksum);
            await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}

return 0;

static string ResolveScriptsDirectory(string? configured)
{
    var candidate = string.IsNullOrWhiteSpace(configured)
        ? Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql")
        : configured;

    return Path.GetFullPath(candidate);
}

static string ComputeChecksum(string value)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(hash).ToLowerInvariant();
}

static async Task EnsureTrackingTableAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand("""
        CREATE TABLE IF NOT EXISTS OpenJiboSchemaMigrations (
            ScriptName TEXT NOT NULL PRIMARY KEY,
            Checksum TEXT NOT NULL,
            AppliedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )
        """, connection);
    await command.ExecuteNonQueryAsync();
}

static async Task<Dictionary<string, string>> LoadAppliedScriptsAsync(NpgsqlConnection connection)
{
    var applied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    await using var command = new NpgsqlCommand("""
        SELECT ScriptName, Checksum
        FROM OpenJiboSchemaMigrations
        ORDER BY ScriptName
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var scriptName = reader.GetString(0);
        var checksum = reader.GetString(1);
        applied[scriptName] = checksum;
    }

    return applied;
}

internal enum MigrationTarget
{
    State,
    PersonalMemory,
    All
}

internal sealed record MigrationOptions(
    MigrationTarget Target,
    bool PreviewOnly,
    bool Verbose,
    string? ScriptsDirectory,
    string? StateConnectionString,
    string? PersonalMemoryConnectionString,
    bool ShowHelp)
{
    public static MigrationOptions Parse(string[] args)
    {
        var target = MigrationTarget.All;
        var previewOnly = false;
        var verbose = false;
        string? scriptsDirectory = null;
        string? stateConnectionString = Environment.GetEnvironmentVariable("OpenJibo__State__ConnectionString")
                                        ?? Environment.GetEnvironmentVariable("OPENJIBO_STATE_STORAGE_CONNECTION_STRING")
                                        ?? BuildPostgreSqlConnectionString("openjibo_state");
        string? personalMemoryConnectionString = Environment.GetEnvironmentVariable(
                                                     "OpenJibo__PersonalMemory__ConnectionString")
                                                 ?? Environment.GetEnvironmentVariable(
                                                     "OPENJIBO_PERSONAL_MEMORY_STORAGE_CONNECTION_STRING")
                                                 ?? BuildPostgreSqlConnectionString("openjibo_memory");
        var showHelp = false;

        for (var index = 0; index < args.Length; index += 1)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;
                case "--preview":
                case "--dry-run":
                    previewOnly = true;
                    break;
                case "--apply":
                    previewOnly = false;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--target":
                    target = ParseTarget(GetValue(args, ref index, "--target"));
                    break;
                case "--scripts":
                    scriptsDirectory = GetValue(args, ref index, "--scripts");
                    break;
                case "--state-connection":
                    stateConnectionString = GetValue(args, ref index, "--state-connection");
                    break;
                case "--memory-connection":
                    personalMemoryConnectionString = GetValue(args, ref index, "--memory-connection");
                    break;
            }
        }

        return new MigrationOptions(
            target,
            previewOnly,
            verbose,
            scriptsDirectory,
            stateConnectionString,
            personalMemoryConnectionString,
            showHelp);
    }

    private static string BuildPostgreSqlConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("OPENJIBO_POSTGRES_HOST") ?? "postgres",
            Port = int.TryParse(Environment.GetEnvironmentVariable("OPENJIBO_POSTGRES_PORT"), out var port)
                ? port
                : 5432,
            Database = databaseName,
            Username = Environment.GetEnvironmentVariable("OPENJIBO_POSTGRES_USER") ?? "openjibo"
        };

        var password = Environment.GetEnvironmentVariable("OPENJIBO_POSTGRES_PASSWORD");
        if (!string.IsNullOrWhiteSpace(password))
            builder.Password = password;

        return builder.ConnectionString;
    }

    public string? ResolveConnectionString(MigrationTarget target)
    {
        return target switch
        {
            MigrationTarget.State => StateConnectionString,
            MigrationTarget.PersonalMemory => PersonalMemoryConnectionString,
            _ => null
        };
    }

    private static MigrationTarget ParseTarget(string? value)
    {
        return string.Equals(value, "state", StringComparison.OrdinalIgnoreCase)
            ? MigrationTarget.State
            : string.Equals(value, "personal-memory", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(value, "memory", StringComparison.OrdinalIgnoreCase)
                ? MigrationTarget.PersonalMemory
                : MigrationTarget.All;
    }

    private static string GetValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {optionName}.");

        index += 1;
        return args[index];
    }

    public static string HelpText =>
        """
        Open Jibo migration runner

        Usage:
          dotnet Jibo.Cloud.Migrations.dll --apply
          dotnet Jibo.Cloud.Migrations.dll --preview
          dotnet Jibo.Cloud.Migrations.dll --target state|personal-memory|all

        Options:
          --apply                 Apply pending SQL migrations
          --preview, --dry-run    List pending migrations without applying them
          --target                Choose a target database
          --scripts               Override the SQL script directory
          --state-connection      Override the state database connection string
          --memory-connection     Override the personal memory connection string
          --verbose               Print already-applied scripts too
          --help                  Show this help
        """;
}
