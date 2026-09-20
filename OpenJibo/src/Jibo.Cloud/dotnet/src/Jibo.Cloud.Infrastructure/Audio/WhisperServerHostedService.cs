using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Infrastructure.Audio;

/// <summary>
/// Starts a local whisper.cpp <c>whisper-server</c> for loopback WhisperServerUrl values
/// so the model stays warm across turns. Shared by <c>dotnet run</c>, published binaries,
/// and containers — not Docker-entrypoint-only.
/// </summary>
public sealed class WhisperServerHostedService(
    BufferedAudioSttOptions options,
    ILogger<WhisperServerHostedService> logger) : IHostedService, IDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(200);

    private Process? _process;
    private bool _ownsProcess;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var resolved = BufferedAudioSttPathResolver.Resolve(options);
        if (!resolved.EnableWhisperServer || !resolved.AutoStartWhisperServer)
        {
            logger.LogDebug(
                "whisper-server autostart skipped (EnableWhisperServer={Enable}, AutoStart={AutoStart})",
                resolved.EnableWhisperServer,
                resolved.AutoStartWhisperServer);
            return;
        }

        if (!TryParseLoopbackEndpoint(resolved.WhisperServerUrl, out var host, out var port))
        {
            logger.LogInformation(
                "whisper-server autostart skipped because WhisperServerUrl is not loopback ({Url})",
                resolved.WhisperServerUrl);
            return;
        }

        if (await IsListeningAsync(host, port, cancellationToken))
        {
            logger.LogInformation(
                "whisper-server already listening on {Host}:{Port}; leaving process alone",
                host,
                port);
            return;
        }

        if (string.IsNullOrWhiteSpace(resolved.WhisperServerBinPath) ||
            !IsUsableExecutable(resolved.WhisperServerBinPath))
        {
            logger.LogWarning(
                "whisper-server autostart skipped: binary not found ({Bin}). Falling back to per-turn whisper-cli when enabled.",
                resolved.WhisperServerBinPath);
            return;
        }

        if (string.IsNullOrWhiteSpace(resolved.WhisperModelPath) || !File.Exists(resolved.WhisperModelPath))
        {
            logger.LogWarning(
                "whisper-server autostart skipped: model not found ({Model}). Falling back to per-turn whisper-cli when enabled.",
                resolved.WhisperModelPath);
            return;
        }

        var arguments = new List<string>
        {
            "-m", resolved.WhisperModelPath!,
            "--host", host,
            "--port", port.ToString()
        };
        if (resolved.WhisperThreads > 0)
        {
            arguments.Add("-t");
            arguments.Add(resolved.WhisperThreads.ToString());
        }

        if (!string.IsNullOrWhiteSpace(resolved.WhisperLanguage))
        {
            arguments.Add("-l");
            arguments.Add(resolved.WhisperLanguage);
        }

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = resolved.WhisperServerBinPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);

            if (!process.Start())
            {
                logger.LogWarning("whisper-server process failed to start");
                process.Dispose();
                return;
            }

            _process = process;
            _ownsProcess = true;
            logger.LogInformation(
                "Started whisper-server on {Host}:{Port} (pid={Pid}, bin={Bin})",
                host,
                port,
                process.Id,
                resolved.WhisperServerBinPath);

            var ready = await WaitUntilListeningAsync(host, port, cancellationToken);
            if (!ready)
            {
                logger.LogWarning(
                    "whisper-server did not become ready on {Host}:{Port} within {Timeout}. STT will fall back to whisper-cli when enabled.",
                    host,
                    port,
                    ReadyTimeout);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "whisper-server autostart failed; STT will fall back to whisper-cli when enabled");
            StopOwnedProcess();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopOwnedProcess();
        return Task.CompletedTask;
    }

    public void Dispose() => StopOwnedProcess();

    internal static bool TryParseLoopbackEndpoint(string? url, out string host, out int port)
    {
        host = "127.0.0.1";
        port = 8090;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!uri.IsLoopback) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        host = uri.Host;
        port = uri.IsDefaultPort
            ? string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80
            : uri.Port;
        return port is > 0 and <= 65535;
    }

    private async Task<bool> WaitUntilListeningAsync(string host, int port, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ReadyTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process is { HasExited: true }) return false;
            if (await IsListeningAsync(host, port, cancellationToken)) return true;
            await Task.Delay(ReadyPollInterval, cancellationToken);
        }

        return false;
    }

    private static async Task<bool> IsListeningAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool IsUsableExecutable(string path)
    {
        if (Path.IsPathRooted(path)) return File.Exists(path);
        // PATH-relative names are left to Process.Start; treat as usable.
        return !string.IsNullOrWhiteSpace(path);
    }

    private void StopOwnedProcess()
    {
        if (!_ownsProcess || _process is null) return;

        try
        {
            if (!_process.HasExited)
            {
                logger.LogInformation("Stopping owned whisper-server (pid={Pid})", _process.Id);
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to stop owned whisper-server cleanly");
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _ownsProcess = false;
        }
    }
}
