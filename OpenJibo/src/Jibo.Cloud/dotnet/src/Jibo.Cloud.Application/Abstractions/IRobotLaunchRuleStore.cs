using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Abstractions;

public interface IRobotLaunchRuleStore
{
    IReadOnlyList<RobotLaunchRuleFile> List(string robotFriendlyName);

    IReadOnlyList<string> ListRobotFriendlyNames();

    RobotLaunchRuleFile? Get(string robotFriendlyName, string fileName);

    RobotLaunchRuleFile Save(string robotFriendlyName, string fileName, string content);

    bool Delete(string robotFriendlyName, string fileName);
}
