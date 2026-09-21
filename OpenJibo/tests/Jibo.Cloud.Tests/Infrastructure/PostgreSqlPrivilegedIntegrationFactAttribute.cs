namespace Jibo.Cloud.Tests.Infrastructure;

/// <summary>
/// Marks tests that may create cluster-wide PostgreSQL roles and therefore must
/// only run against an explicitly disposable administrator-owned test cluster.
/// </summary>
internal sealed class PostgreSqlPrivilegedIntegrationFactAttribute : FactAttribute
{
    public PostgreSqlPrivilegedIntegrationFactAttribute()
    {
        const string variable = "OPENJIBO_TEST_POSTGRES_PRIVILEGED_CONNECTION_STRING";
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            Skip = $"Set {variable} to run privileged PostgreSQL integration tests.";
    }
}
