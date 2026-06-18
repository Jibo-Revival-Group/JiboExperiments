namespace Jibo.Cloud.Api.Hosting;

public sealed class LaunchRulesAdminOptions
{
    public string? AdminPassword { get; set; }

    public bool IsConfigured => !string.IsNullOrEmpty(AdminPassword);
}
