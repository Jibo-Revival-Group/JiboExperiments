namespace Jibo.Cloud.Infrastructure.Platform;

public static class OperatingSystemPlatformResolver
{
    public static OperatingSystemPlatform Resolve()
    {
        if (OperatingSystem.IsWindows()) return OperatingSystemPlatform.Windows;

        if (OperatingSystem.IsLinux()) return OperatingSystemPlatform.Linux;

        if (OperatingSystem.IsMacOS()) return OperatingSystemPlatform.MacOS;

        return OperatingSystemPlatform.Unknown;
    }
}