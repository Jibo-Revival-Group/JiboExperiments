using Jibo.Cloud.Infrastructure.LaunchRules;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class FileRobotLaunchRuleStoreTests
{
    [Fact]
    public void Save_List_Get_And_Delete_PersistGlobalLaunchRules()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-launch-rules-{Guid.NewGuid():N}");
        var store = new FileRobotLaunchRuleStore(new RobotLaunchRuleOptions { DirectoryPath = root });
        const string content = "TopRule = ($* open my skill {%skill='@be/custom-skill'%} $*);";

        var saved = store.Save("custom.launch.rule", content);

        Assert.Equal("custom.launch.rule", saved.FileName);
        Assert.Equal(content, saved.Content);
        Assert.Equal(FileRobotLaunchRuleStore.GlobalScopeName, saved.RobotFriendlyName);

        var listed = store.List();
        Assert.Single(listed);
        Assert.Equal("custom.launch.rule", listed[0].FileName);

        var fetched = store.Get("custom.launch.rule");
        Assert.NotNull(fetched);
        Assert.Equal(content, fetched.Content);

        Assert.True(store.Delete("custom.launch.rule"));
        Assert.Empty(store.List());
    }

    [Fact]
    public void List_MigratesLegacyRobotDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-launch-rules-{Guid.NewGuid():N}");
        var legacyDirectory = Path.Combine(root, "Royal-Current-Sage-Canvas");
        Directory.CreateDirectory(legacyDirectory);
        const string content = "TopRule = ($* open gallery {%skill='@be/gallery'%} $*);";
        File.WriteAllText(Path.Combine(legacyDirectory, "gallery.launch.rule"), content);

        var store = new FileRobotLaunchRuleStore(new RobotLaunchRuleOptions { DirectoryPath = root });
        var listed = store.List();

        Assert.Single(listed);
        Assert.Equal("gallery.launch.rule", listed[0].FileName);
        Assert.Equal(content, listed[0].Content);
    }

    [Fact]
    public void Save_RejectsNonRuleExtension()
    {
        var store = CreateStore();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            store.Save("launch.txt", "TopRule = ($* hi $*);"));

        Assert.Contains(".rule", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static FileRobotLaunchRuleStore CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-launch-rules-{Guid.NewGuid():N}");
        return new FileRobotLaunchRuleStore(new RobotLaunchRuleOptions { DirectoryPath = root });
    }
}
