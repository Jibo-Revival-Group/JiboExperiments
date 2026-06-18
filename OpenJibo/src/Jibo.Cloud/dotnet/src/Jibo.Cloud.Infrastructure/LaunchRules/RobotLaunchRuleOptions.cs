namespace Jibo.Cloud.Infrastructure.LaunchRules;

public sealed class RobotLaunchRuleOptions
{
    public string DirectoryPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "App_Data", "robot-launch-rules");
}
