using Jibo.Cloud.Infrastructure.Persistence;
using System.Text.Json;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class DeviceRecoveryPlannerTests
{
    [Fact]
    public void Build_CaseInsensitiveCollisionPlansOnlyMissingDevicesWithoutOverwriting()
    {
        var existing = Device("ROBOT-1", "existing-source", "old-name");
        var missing = Device("robot-2", "physical", "restored-name");

        var plan = RecoveryPlanner.Build(
            [existing, missing], [], [], ["robot-1"], [], [], []);

        var restored = Assert.Single(plan.DevicesToInsert);
        Assert.Equal("robot-2", restored.DeviceId);
        Assert.Equal("restored-name", restored.FriendlyName);
        Assert.Equal(1, plan.AlreadyPresentDevices);
    }

    [Fact]
    public void Build_SkipsMissingAccountsAndDeduplicatesLinksAndMappings()
    {
        var device = Device("robot-1", "physical", "Jibo");
        var links = new[]
        {
            new RecoveryAccountDeviceLink("account-present", device.DeviceId, "owner", DateTimeOffset.UtcNow),
            new RecoveryAccountDeviceLink("account-present", device.DeviceId, "owner", DateTimeOffset.UtcNow),
            new RecoveryAccountDeviceLink("account-missing", device.DeviceId, "owner", DateTimeOffset.UtcNow)
        };
        var mappings = new[]
        {
            new RecoveryDeviceMapping(device.DeviceId, "host", "api.example", DateTimeOffset.UtcNow),
            new RecoveryDeviceMapping(device.DeviceId, "host", "api.example", DateTimeOffset.UtcNow)
        };

        var plan = RecoveryPlanner.Build(
            [device], links, mappings, [], ["account-present"], [], []);

        Assert.Single(plan.DevicesToInsert);
        Assert.Equal(2, plan.SourceAccountDeviceLinks.Count);
        Assert.Single(plan.AccountDeviceLinksToInsert);
        Assert.Equal(1, plan.LinksMissingTargetAccounts);
        Assert.Single(plan.SourceDeviceHostMappings);
        Assert.Single(plan.DeviceHostMappingsToInsert);
    }

    [Fact]
    public void Build_IsIdempotentWhenSecondPlanSeesFirstPlanResults()
    {
        var device = Device("robot-1", "physical", "Jibo");
        var link = new RecoveryAccountDeviceLink("account-1", device.DeviceId, "owner", DateTimeOffset.UtcNow);
        var mapping = new RecoveryDeviceMapping(device.DeviceId, "host", "api.example", DateTimeOffset.UtcNow);
        var first = RecoveryPlanner.Build([device], [link], [mapping], [], ["account-1"], [], []);

        var second = RecoveryPlanner.Build([device], [link], [mapping], [device.DeviceId], ["account-1"],
            [(link.AccountId, link.DeviceId)], [(mapping.DeviceId, mapping.MappingKey)]);

        Assert.Single(first.DevicesToInsert);
        Assert.Single(first.AccountDeviceLinksToInsert);
        Assert.Single(first.DeviceHostMappingsToInsert);
        Assert.Empty(second.DevicesToInsert);
        Assert.Empty(second.AccountDeviceLinksToInsert);
        Assert.Empty(second.DeviceHostMappingsToInsert);
    }

    [Fact]
    public void RecoveryReport_SerializesAggregateFieldsWithoutIdentifiers()
    {
        var report = new RecoveryReport(2, 1, 1, 0, 1, 1, 1, 0, 1, 1,
            false, 0, 0, 0, false);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.Contains("sourceDevices", json, StringComparison.Ordinal);
        Assert.DoesNotContain("robot-1", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account-1", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void RevisionUpdateRequiresExactlyOneAffectedRow(int affectedRows, bool expected)
    {
        Assert.Equal(expected, RecoveryPlanner.IsRevisionUpdateSuccessful(affectedRows));
    }
    private static RecoveryDevice Device(string id, string source, string friendlyName) => new(
        id, id, friendlyName, null, null, true, null, null, null, null, null, null, null,
        source, false, false, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}