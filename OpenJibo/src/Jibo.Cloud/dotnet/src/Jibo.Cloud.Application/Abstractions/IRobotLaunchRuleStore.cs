using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Abstractions;

public interface IRobotLaunchRuleStore
{
    IReadOnlyList<RobotLaunchRuleFile> List();

    RobotLaunchRuleFile? Get(string fileName);

    RobotLaunchRuleFile Save(string fileName, string content);

    bool Delete(string fileName);
}
