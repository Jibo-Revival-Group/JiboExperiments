namespace Jibo.Cloud.Infrastructure.Media;

public sealed class MediaContentStoreOptions
{
    public MediaContentStoreKind Backend { get; set; } = MediaContentStoreKind.File;
    public string? DirectoryPath { get; set; }
    public string? ConnectionString { get; set; }
    public string ContainerName { get; set; } = "openjibo-media";
}
