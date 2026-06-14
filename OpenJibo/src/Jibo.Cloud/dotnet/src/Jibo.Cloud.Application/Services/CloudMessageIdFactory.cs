namespace Jibo.Cloud.Application.Services;

internal static class CloudMessageIdFactory
{
    internal static string CreateHubMessageId()
    {
        return $"mid-{Guid.NewGuid():N}";
    }

    internal static string CreateProtocolId()
    {
        return Guid.NewGuid().ToString("N");
    }
}