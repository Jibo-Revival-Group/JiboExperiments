namespace Jibo.Cloud.Infrastructure.Platform;

public static class OperatingSystemPlatformResolver
{
    public static OperatingSystemPlatform Resolve()
    {
        if (OperatingSystem.IsWindows())
        {
            return OperatingSystemPlatform.Windows;
        }
        else if (OperatingSystem.IsLinux())
        {
            return OperatingSystemPlatform.Linux;
        }
        else if (OperatingSystem.IsMacOS())
        {
            return OperatingSystemPlatform.MacOS;
        }
        else
        {
            return OperatingSystemPlatform.Unknown;
        }
    }
}