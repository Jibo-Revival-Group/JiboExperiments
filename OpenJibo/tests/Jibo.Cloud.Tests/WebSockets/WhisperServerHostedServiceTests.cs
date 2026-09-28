using System.Diagnostics;
using Jibo.Cloud.Infrastructure.Audio;

namespace Jibo.Cloud.Tests.WebSockets;

public sealed class WhisperServerHostedServiceTests
{
    [Fact]
    public async Task DrainOutputAsync_AllowsChattyChildToExit()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(
                "$chunk='x'*1024; 1..256 | ForEach-Object { [Console]::Out.WriteLine($chunk); [Console]::Error.WriteLine($chunk) }");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(
                "i=0; while [ \"$i\" -lt 256 ]; do printf '%1024s\\n' x; printf '%1024s\\n' x >&2; i=$((i+1)); done");
        }

        using var process = Process.Start(startInfo)!;
        try
        {
            var drain = WhisperServerHostedService.DrainOutputAsync(process);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await drain.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
