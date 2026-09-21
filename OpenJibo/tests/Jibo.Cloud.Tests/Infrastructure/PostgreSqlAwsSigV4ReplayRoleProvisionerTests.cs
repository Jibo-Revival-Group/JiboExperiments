using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PostgreSqlAwsSigV4ReplayRoleProvisionerTests
{
    private const string Administrator =
        "Host=postgres.example;Port=5432;Database=openjibo_state;Username=admin;Password=admin-secret";

    [Fact]
    public async Task ProvisionAsync_RejectsUnexpectedObserverUsernameBeforeConnecting()
    {
        var observer =
            "Host=postgres.example;Port=5432;Database=openjibo_state;Username=broad-runtime;Password=secret";

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            PostgreSqlAwsSigV4ReplayRoleProvisioner.ProvisionAsync(Administrator, observer));

        Assert.Contains(PostgreSqlAwsSigV4ReplayRoleProvisioner.LoginRole, error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionAsync_RejectsDifferentServerOrDatabaseBeforeConnecting()
    {
        var observer =
            $"Host=other.example;Port=5432;Database=openjibo_state;Username={PostgreSqlAwsSigV4ReplayRoleProvisioner.LoginRole};Password=secret";

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            PostgreSqlAwsSigV4ReplayRoleProvisioner.ProvisionAsync(Administrator, observer));

        Assert.Contains("same PostgreSQL server and database", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionAsync_RequiresObserverPasswordBeforeConnecting()
    {
        var observer =
            $"Host=postgres.example;Port=5432;Database=openjibo_state;Username={PostgreSqlAwsSigV4ReplayRoleProvisioner.LoginRole}";

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            PostgreSqlAwsSigV4ReplayRoleProvisioner.ProvisionAsync(Administrator, observer));

        Assert.Contains("password", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvisionAsync_RequiresTlsForRemoteObserverConnection()
    {
        var observer =
            $"Host=postgres.example;Port=5432;Database=openjibo_state;Username={PostgreSqlAwsSigV4ReplayRoleProvisioner.LoginRole};Password=secret;SSL Mode=Disable";

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            PostgreSqlAwsSigV4ReplayRoleProvisioner.ProvisionAsync(Administrator, observer));

        Assert.Contains("TLS", error.Message, StringComparison.Ordinal);
    }
}
