using Jibo.Cloud.Infrastructure.LaunchRules;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class FileRobotLaunchRuleStoreTests
{
    [Fact]
    public void Save_List_Get_And_Delete_PersistLaunchRuleForRobot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-launch-rules-{Guid.NewGuid():N}");
        var store = new FileRobotLaunchRuleStore(new RobotLaunchRuleOptions { DirectoryPath = root });
        const string content = "TopRule = ($* open my skill {%skill='@be/custom-skill'%} $*);";

        var saved = store.Save("Royal-Current-Sage-Canvas", "custom.launch.rule", content);

        Assert.Equal("custom.launch.rule", saved.FileName);
        Assert.Equal(content, saved.Content);

        var listed = store.List("Royal-Current-Sage-Canvas");
        Assert.Single(listed);
        Assert.Equal("custom.launch.rule", listed[0].FileName);

        var fetched = store.Get("Royal-Current-Sage-Canvas", "custom.launch.rule");
        Assert.NotNull(fetched);
        Assert.Equal(content, fetched.Content);

        Assert.True(store.Delete("Royal-Current-Sage-Canvas", "custom.launch.rule"));
        Assert.Empty(store.List("Royal-Current-Sage-Canvas"));
    }

    [Fact]
    public void Save_RejectsInvalidRobotName()
    {
        var store = CreateStore();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            store.Save("../evil", "launch.rule", "TopRule = ($* hi $*);"));

        Assert.Contains("friendly name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_RejectsNonRuleExtension()
    {
        var store = CreateStore();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            store.Save("Royal-Current-Sage-Canvas", "launch.txt", "TopRule = ($* hi $*);"));

        Assert.Contains(".rule", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static FileRobotLaunchRuleStore CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-launch-rules-{Guid.NewGuid():N}");
        return new FileRobotLaunchRuleStore(new RobotLaunchRuleOptions { DirectoryPath = root });
    }
}
