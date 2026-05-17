using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Media;

internal sealed class MediaContentStoreFactory : IMediaContentStoreFactory
{
    public IMediaContentStore Create(string? directoryPath, MediaContentStoreKind backendKind, string containerName,
        string? connectionString)
    {
        return backendKind switch
        {
            MediaContentStoreKind.AzureBlob => new AzureBlobMediaContentStore(connectionString, containerName),
            _ => new FileMediaContentStore(directoryPath)
        };
    }
}
