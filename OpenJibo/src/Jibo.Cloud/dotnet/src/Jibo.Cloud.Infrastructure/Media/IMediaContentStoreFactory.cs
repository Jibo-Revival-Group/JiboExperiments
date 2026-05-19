using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Media;

internal interface IMediaContentStoreFactory
{
    IMediaContentStore Create(string? directoryPath, MediaContentStoreKind backendKind, string containerName,
        string? connectionString);
}