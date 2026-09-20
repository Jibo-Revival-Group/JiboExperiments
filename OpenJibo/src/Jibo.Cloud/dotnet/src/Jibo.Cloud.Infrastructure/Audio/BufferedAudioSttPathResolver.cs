using Jibo.Cloud.Infrastructure.Platform;

namespace Jibo.Cloud.Infrastructure.Audio;

public static class BufferedAudioSttPathResolver
{
    private const string LegacyLinuxFfmpegPath = "/usr/bin/ffmpeg";
    private const string LegacyLinuxWhisperCliPath = "/usr/bin/whisper.cpp/build/bin/whisper-cli";
    private const string LegacyLinuxWhisperServerPath = "/usr/bin/whisper.cpp/build/bin/whisper-server";
    private const string LegacyLinuxWhisperModelPath = "/usr/bin/whisper.cpp/models/ggml-base.en.bin";

    public static BufferedAudioSttOptions Resolve(BufferedAudioSttOptions source)
    {
        return Resolve(
            source,
            Environment.GetEnvironmentVariable,
            File.Exists,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            OperatingSystemPlatformResolver.Resolve());
    }

    public static BufferedAudioSttOptions Resolve(
        BufferedAudioSttOptions source,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists,
        string? homeDirectory,
        OperatingSystemPlatform platform)
    {
        var whisperCliPath = ResolveExecutable(
            source.WhisperCliPath,
            ["OPENJIBO_STT_WHISPER_CLI_PATH", "WHISPER_CLI_PATH"],
            LegacyLinuxWhisperCliPath,
            BuildWhisperCliCandidates(platform, homeDirectory),
            homeDirectory,
            getEnvironmentVariable,
            fileExists);

        var whisperServerEnableEnv = getEnvironmentVariable("OPENJIBO_STT_ENABLE_WHISPER_SERVER");
        // Warm server is preferred whenever local whisper.cpp STT is on (dotnet run, published
        // binary, container). Explicit false via env still disables it.
        var enableWhisperServer = !IsFalsy(whisperServerEnableEnv) &&
                                  (source.EnableWhisperServer ||
                                   IsTruthy(whisperServerEnableEnv) ||
                                   (source.AutoStartWhisperServer && source.EnableLocalWhisperCpp));

        return new BufferedAudioSttOptions
        {
            EnableLocalWhisperCpp = source.EnableLocalWhisperCpp,
            EnableAzureSpeech = source.EnableAzureSpeech,
            EnableWhisperServer = enableWhisperServer,
            AutoStartWhisperServer = source.AutoStartWhisperServer &&
                                     !IsFalsy(getEnvironmentVariable("OPENJIBO_STT_AUTOSTART_WHISPER_SERVER")),
            FfmpegPath = ResolveExecutable(
                source.FfmpegPath,
                ["OPENJIBO_STT_FFMPEG_PATH", "FFMPEG_PATH"],
                LegacyLinuxFfmpegPath,
                BuildFfmpegCandidates(platform),
                homeDirectory,
                getEnvironmentVariable,
                fileExists),
            WhisperCliPath = whisperCliPath,
            WhisperModelPath = ResolveRequiredFile(
                source.WhisperModelPath,
                ["OPENJIBO_STT_WHISPER_MODEL_PATH", "WHISPER_MODEL_PATH"],
                LegacyLinuxWhisperModelPath,
                BuildWhisperModelCandidates(platform, homeDirectory),
                homeDirectory,
                getEnvironmentVariable,
                fileExists),
            WhisperServerBinPath = ResolveWhisperServerBinPath(
                source.WhisperServerBinPath,
                whisperCliPath,
                getEnvironmentVariable,
                fileExists,
                platform,
                homeDirectory),
            WhisperServerUrl = ResolveOptionalString(
                source.WhisperServerUrl,
                ["OPENJIBO_STT_WHISPER_SERVER_URL", "WHISPER_SERVER_URL"],
                getEnvironmentVariable) ?? "http://127.0.0.1:8090",
            AzureSpeechRegion = source.AzureSpeechRegion,
            AzureSpeechSubscriptionKey = source.AzureSpeechSubscriptionKey,
            AzureSpeechEndpoint = source.AzureSpeechEndpoint,
            AzureSpeechRequestTimeout = source.AzureSpeechRequestTimeout,
            WhisperLanguage = source.WhisperLanguage,
            WhisperAudioContext = ResolvePositiveInt(
                source.WhisperAudioContext,
                ["OPENJIBO_STT_WHISPER_AUDIO_CTX", "WHISPER_AUDIO_CTX"],
                getEnvironmentVariable,
                512),
            WhisperThreads = ResolveNonNegativeInt(
                source.WhisperThreads,
                ["OPENJIBO_STT_WHISPER_THREADS", "WHISPER_THREADS"],
                getEnvironmentVariable,
                0),
            WhisperBeamSize = ResolvePositiveInt(
                source.WhisperBeamSize,
                ["OPENJIBO_STT_WHISPER_BEAM_SIZE", "WHISPER_BEAM_SIZE"],
                getEnvironmentVariable,
                1),
            TempDirectory = source.TempDirectory,
            CleanupTempFiles = source.CleanupTempFiles
        };
    }

    private static string? ResolveExecutable(
        string? configured,
        IReadOnlyList<string> environmentVariableNames,
        string legacyLinuxDefault,
        IReadOnlyList<string> discoveryCandidates,
        string? homeDirectory,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists)
    {
        configured = NormalizeConfiguredPath(configured, homeDirectory);
        if (IsRelativeOrExistingAbsolute(configured, fileExists)) return configured;

        var environmentPath = ResolveEnvironmentPath(environmentVariableNames, homeDirectory, getEnvironmentVariable, fileExists);
        if (!string.IsNullOrWhiteSpace(environmentPath)) return environmentPath;

        if (!ShouldDiscover(configured, legacyLinuxDefault)) return configured;

        return discoveryCandidates.FirstOrDefault(candidate => IsRelativeOrExistingAbsolute(candidate, fileExists)) ??
               configured;
    }

    private static string? ResolveRequiredFile(
        string? configured,
        IReadOnlyList<string> environmentVariableNames,
        string legacyLinuxDefault,
        IReadOnlyList<string> discoveryCandidates,
        string? homeDirectory,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists)
    {
        configured = NormalizeConfiguredPath(configured, homeDirectory);
        if (IsRelativeOrExistingAbsolute(configured, fileExists)) return configured;

        var environmentPath = ResolveEnvironmentPath(environmentVariableNames, homeDirectory, getEnvironmentVariable, fileExists);
        if (!string.IsNullOrWhiteSpace(environmentPath)) return environmentPath;

        if (!ShouldDiscover(configured, legacyLinuxDefault)) return configured;

        return discoveryCandidates.FirstOrDefault(fileExists) ?? configured;
    }

    private static string? ResolveEnvironmentPath(
        IReadOnlyList<string> environmentVariableNames,
        string? homeDirectory,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists)
    {
        return environmentVariableNames.Select(getEnvironmentVariable)
            .Select(value => NormalizeConfiguredPath(value, homeDirectory))
            .FirstOrDefault(value => IsRelativeOrExistingAbsolute(value, fileExists));
    }

    private static bool IsRelativeOrExistingAbsolute(string? path, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return !Path.IsPathRooted(path) || fileExists(path);
    }

    public static void ValidateResolvedDependencies(BufferedAudioSttOptions source)
    {
        ValidateResolvedDependencies(
            source,
            Environment.GetEnvironmentVariable,
            File.Exists,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            OperatingSystemPlatformResolver.Resolve());
    }

    public static void ValidateResolvedDependencies(
        BufferedAudioSttOptions source,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists,
        string? homeDirectory,
        OperatingSystemPlatform platform)
    {
        ValidateResolvedDependencies(
            source,
            getEnvironmentVariable,
            fileExists,
            homeDirectory,
            platform,
            ProbeWhisperCppBinary);
    }

    public static void ValidateResolvedDependencies(
        BufferedAudioSttOptions source,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists,
        string? homeDirectory,
        OperatingSystemPlatform platform,
        Func<string, WhisperCppProbeResult> probeWhisperCpp)
    {
        var resolved = Resolve(source, getEnvironmentVariable, fileExists, homeDirectory, platform);
        if (!resolved.EnableLocalWhisperCpp && !resolved.EnableAzureSpeech && !resolved.EnableWhisperServer)
            return;

        var issues = new List<string>();

        if ((resolved.EnableLocalWhisperCpp || resolved.EnableAzureSpeech || resolved.EnableWhisperServer) &&
            !IsExecutableAvailable(resolved.FfmpegPath, getEnvironmentVariable, fileExists, platform))
            issues.Add(DescribeExecutableIssue("ffmpeg", resolved.FfmpegPath, "OpenJibo:Stt:FfmpegPath"));

        if (resolved.EnableLocalWhisperCpp)
        {
            if (!IsExecutableAvailable(resolved.WhisperCliPath, getEnvironmentVariable, fileExists, platform))
                issues.Add(DescribeExecutableIssue("whisper-cli", resolved.WhisperCliPath,
                    "OpenJibo:Stt:WhisperCliPath"));
            else
            {
                var probe = probeWhisperCpp(resolved.WhisperCliPath!);
                if (!probe.IsWhisperCpp)
                    issues.Add(
                        $"OpenJibo:Stt:WhisperCliPath points to '{resolved.WhisperCliPath}', but that binary is not whisper.cpp " +
                        $"(expected help text containing '--audio-ctx'). {probe.Detail} " +
                        "OpenAI's Python `whisper` package is incompatible — point this at whisper-cli from whisper.cpp.");
            }

            if (!IsFileAvailable(resolved.WhisperModelPath, fileExists))
                issues.Add(DescribeFileIssue(resolved.WhisperModelPath, "OpenJibo:Stt:WhisperModelPath"));
        }

        if (issues.Count == 0) return;

        throw new InvalidOperationException(
            "OpenJibo is configured to use buffered-audio STT, but one or more required dependencies could not be resolved. " +
            "This often happens when the server is started under a different user account than the one that owns the tools, " +
            "for example via sudo. " +
            string.Join(" ", issues) +
            " Fix the configured paths or disable OpenJibo:Stt:EnableLocalWhisperCpp.");
    }

    public readonly record struct WhisperCppProbeResult(bool IsWhisperCpp, string Detail);

    public static WhisperCppProbeResult ProbeWhisperCppBinary(string fileName)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("-h");
            process.Start();
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(8_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return new WhisperCppProbeResult(false, "Timed out waiting for -h output.");
            }

            var combined = (stdOutTask.GetAwaiter().GetResult() + "\n" + stdErrTask.GetAwaiter().GetResult())
                .ToLowerInvariant();
            if (combined.Contains("--audio-ctx", StringComparison.Ordinal) ||
                combined.Contains("-ac n", StringComparison.Ordinal) ||
                combined.Contains("whisper.cpp", StringComparison.Ordinal))
                return new WhisperCppProbeResult(true, "Help text matches whisper.cpp.");

            if (combined.Contains("--output_format", StringComparison.Ordinal) ||
                combined.Contains("from whisper.transcribe", StringComparison.Ordinal) ||
                combined.Contains("openai", StringComparison.Ordinal))
                return new WhisperCppProbeResult(false,
                    "Help text looks like OpenAI Python whisper, not whisper.cpp.");

            return new WhisperCppProbeResult(false, "Help text did not include whisper.cpp markers.");
        }
        catch (Exception ex)
        {
            return new WhisperCppProbeResult(false, $"Failed to probe binary: {ex.Message}");
        }
    }

    private static bool ShouldDiscover(string? configured, string legacyLinuxDefault)
    {
        return string.IsNullOrWhiteSpace(configured) ||
               string.Equals(configured, legacyLinuxDefault, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> BuildFfmpegCandidates(OperatingSystemPlatform platform)
    {
        var candidates = new List<string>();
        switch (platform)
        {
            case OperatingSystemPlatform.Windows:
                candidates.AddRange([
                    @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                    @"C:\Program Files\ffmpeg\ffmpeg.exe",
                    @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                    @"C:\Program Files\Gyan\ffmpeg\bin\ffmpeg.exe"
                ]);
                break;
            case OperatingSystemPlatform.MacOS:
                candidates.AddRange([
                    "/opt/homebrew/bin/ffmpeg",
                    "/usr/local/bin/ffmpeg"
                ]);
                break;
            case OperatingSystemPlatform.Linux:
                candidates.AddRange([
                    LegacyLinuxFfmpegPath,
                    "/usr/local/bin/ffmpeg"
                ]);
                break;
        }

        candidates.Add("ffmpeg");
        return candidates;
    }

    private static IReadOnlyList<string> BuildWhisperCliCandidates(OperatingSystemPlatform platform,
        string? homeDirectory)
    {
        var candidates = new List<string>();
        switch (platform)
        {
            case OperatingSystemPlatform.Windows:
                candidates.AddRange([
                    @"C:\Program Files\whisper.cpp\build\bin\Release\whisper-cli.exe",
                    @"C:\Program Files\whisper.cpp\build\bin\whisper-cli.exe",
                    @"C:\Program Files\whisper-cpp\build\bin\Release\whisper-cli.exe",
                    @"C:\Program Files\whisper-cpp\build\bin\whisper-cli.exe"
                ]);
                break;
            case OperatingSystemPlatform.MacOS:
                candidates.AddRange([
                    "/opt/homebrew/bin/whisper-cli",
                    "/usr/local/bin/whisper-cli",
                    "/opt/homebrew/opt/whisper-cpp/bin/whisper-cli",
                    "/usr/local/opt/whisper-cpp/bin/whisper-cli"
                ]);
                break;
            case OperatingSystemPlatform.Linux:
                candidates.AddRange([
                    LegacyLinuxWhisperCliPath,
                    "/usr/local/bin/whisper-cli",
                    "/usr/bin/whisper.cpp/build/bin/whisper-cli"
                ]);
                break;
        }

        if (!string.IsNullOrWhiteSpace(homeDirectory))
            candidates.AddRange([
                Path.Combine(homeDirectory, "whisper.cpp", "build", "bin", "whisper-cli"),
                Path.Combine(homeDirectory, "whisper.cpp", "build", "bin", "Release", "whisper-cli.exe"),
                Path.Combine(homeDirectory, "src", "whisper.cpp", "build", "bin", "whisper-cli"),
                Path.Combine(homeDirectory, "Code", "whisper.cpp", "build", "bin", "whisper-cli"),
                Path.Combine(homeDirectory, "EZJiboServer", "whisper.cpp", "build", "bin", "whisper-cli"),
                Path.Combine(homeDirectory, "EZOpenJibo", "whisper.cpp", "build", "bin", "whisper-cli")
            ]);

        candidates.Add("whisper-cli");
        return candidates;
    }

    private static IReadOnlyList<string> BuildWhisperModelCandidates(OperatingSystemPlatform platform,
        string? homeDirectory)
    {
        var candidates = new List<string>();
        switch (platform)
        {
            case OperatingSystemPlatform.Windows:
                candidates.AddRange([
                    @"C:\Program Files\whisper.cpp\models\ggml-base.en.bin",
                    @"C:\Program Files\whisper-cpp\models\ggml-base.en.bin",
                    @"C:\Program Files\whisper.cpp\share\whisper-cpp\models\ggml-base.en.bin",
                    @"C:\Program Files\whisper-cpp\share\whisper-cpp\models\ggml-base.en.bin"
                ]);
                break;
            case OperatingSystemPlatform.MacOS:
                candidates.AddRange([
                    "/opt/homebrew/share/whisper-cpp/models/ggml-base.en.bin",
                    "/opt/homebrew/share/whisper.cpp/models/ggml-base.en.bin",
                    "/opt/homebrew/opt/whisper-cpp/share/whisper-cpp/models/ggml-base.en.bin",
                    "/opt/homebrew/opt/whisper.cpp/share/whisper.cpp/models/ggml-base.en.bin",
                    "/usr/local/share/whisper-cpp/models/ggml-base.en.bin",
                    "/usr/local/share/whisper.cpp/models/ggml-base.en.bin"
                ]);
                break;
            case OperatingSystemPlatform.Linux:
                candidates.AddRange([
                    LegacyLinuxWhisperModelPath,
                    "/usr/local/share/whisper-cpp/models/ggml-base.en.bin",
                    "/usr/local/share/whisper.cpp/models/ggml-base.en.bin",
                    "/usr/bin/whisper.cpp/models/ggml-base.en.bin"
                ]);
                break;
        }

        if (!string.IsNullOrWhiteSpace(homeDirectory))
            candidates.AddRange([
                Path.Combine(homeDirectory, "whisper.cpp", "models", "ggml-base.en.bin"),
                Path.Combine(homeDirectory, "src", "whisper.cpp", "models", "ggml-base.en.bin"),
                Path.Combine(homeDirectory, "Code", "whisper.cpp", "models", "ggml-base.en.bin"),
                Path.Combine(homeDirectory, "EZJiboServer", "whisper.cpp", "models", "ggml-base.en.bin"),
                Path.Combine(homeDirectory, "EZOpenJibo", "whisper.cpp", "models", "ggml-base.en.bin"),
                Path.Combine(homeDirectory, "ggml-base.en.bin"),
                Path.Combine(homeDirectory, "Library", "Application Support", "openjibo", "whisper",
                    "ggml-base.en.bin")
            ]);

        return candidates;
    }

    private static bool IsExecutableAvailable(
        string? path,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists,
        OperatingSystemPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var normalizedPath = NormalizeConfiguredPath(path, null);
        if (string.IsNullOrWhiteSpace(normalizedPath)) return false;

        if (Path.IsPathRooted(normalizedPath) || ContainsDirectorySeparator(normalizedPath))
            return fileExists(normalizedPath);

        return FindOnPath(normalizedPath, getEnvironmentVariable, fileExists, platform);
    }

    private static bool IsFileAvailable(string? path, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var normalizedPath = NormalizeConfiguredPath(path, null);
        if (string.IsNullOrWhiteSpace(normalizedPath)) return false;

        return fileExists(normalizedPath);
    }

    private static bool FindOnPath(
        string executableName,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists,
        OperatingSystemPlatform platform)
    {
        var pathValue = getEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue)) return false;

        var directories = pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extensions = GetExecutableExtensions(executableName, getEnvironmentVariable, platform);

        foreach (var directory in directories)
        foreach (var extension in extensions)
        {
            var candidate = Path.Combine(directory, executableName + extension);
            if (fileExists(candidate)) return true;
        }

        return false;
    }

    private static IReadOnlyList<string> GetExecutableExtensions(
        string executableName,
        Func<string, string?> getEnvironmentVariable,
        OperatingSystemPlatform platform)
    {
        if (Path.GetExtension(executableName).Length > 0) return [string.Empty];

        if (platform != OperatingSystemPlatform.Windows) return [string.Empty];

        var pathext = getEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathext))
            return [".EXE", ".BAT", ".CMD", ".COM"];

        return pathext
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.StartsWith('.') ? extension : "." + extension)
            .ToArray();
    }

    private static string DescribeExecutableIssue(string executableName, string? path, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(path))
            return $"{configurationKey} is not configured for {executableName}.";

        if (Path.IsPathRooted(path) || ContainsDirectorySeparator(path))
            return $"{configurationKey} points to '{path}', but that file was not found.";

        return $"{configurationKey} points to '{path}', but that command was not found on PATH.";
    }

    private static string DescribeFileIssue(string? path, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(path))
            return $"{configurationKey} is not configured.";

        return $"{configurationKey} points to '{path}', but that file was not found.";
    }

    private static string? NormalizeConfiguredPath(string? path, string? homeDirectory)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        if (path.Length == 1 && path[0] == '~')
            return homeDirectory;

        if (path.Length > 2 && path[0] == '~' && (path[1] == Path.DirectorySeparatorChar || path[1] == Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrWhiteSpace(homeDirectory)) return path;

            return NormalizeSlashes(Path.Combine(homeDirectory, path[2..]));
        }

        return path;
    }

    private static string? NormalizeSlashes(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !ContainsDirectorySeparator(path)) return path;

        var f = path.First(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);

        path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        path = path.Replace(Path.DirectorySeparatorChar, f);

        return path;
    }

    private static bool ContainsDirectorySeparator(string path)
    {
        return path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar);
    }

    private static bool IsTruthy(string? value)
    {
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFalsy(string? value)
    {
        return string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveWhisperServerBinPath(
        string? configured,
        string? whisperCliPath,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists,
        OperatingSystemPlatform platform,
        string? homeDirectory)
    {
        configured = NormalizeConfiguredPath(configured, homeDirectory);
        if (IsRelativeOrExistingAbsolute(configured, fileExists)) return configured;

        var environmentPath = ResolveEnvironmentPath(
            ["OPENJIBO_STT_WHISPER_SERVER_BIN", "WHISPER_SERVER_BIN"],
            homeDirectory,
            getEnvironmentVariable,
            fileExists);
        if (!string.IsNullOrWhiteSpace(environmentPath)) return environmentPath;

        foreach (var sibling in BuildWhisperServerSiblingCandidates(whisperCliPath))
        {
            if (IsRelativeOrExistingAbsolute(sibling, fileExists)) return sibling;
        }

        return BuildWhisperServerCandidates(platform, homeDirectory)
                   .FirstOrDefault(candidate => IsRelativeOrExistingAbsolute(candidate, fileExists)) ??
               configured;
    }

    private static IEnumerable<string> BuildWhisperServerSiblingCandidates(string? whisperCliPath)
    {
        if (string.IsNullOrWhiteSpace(whisperCliPath)) yield break;
        if (!Path.IsPathRooted(whisperCliPath) && !ContainsDirectorySeparator(whisperCliPath)) yield break;

        string? directory;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(whisperCliPath));
        }
        catch (Exception)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(directory)) yield break;

        yield return Path.Combine(directory, "whisper-server");
        yield return Path.Combine(directory, "whisper-server.exe");
    }

    private static IReadOnlyList<string> BuildWhisperServerCandidates(OperatingSystemPlatform platform,
        string? homeDirectory)
    {
        var candidates = new List<string>();
        switch (platform)
        {
            case OperatingSystemPlatform.Windows:
                candidates.AddRange([
                    @"C:\Program Files\whisper.cpp\build\bin\Release\whisper-server.exe",
                    @"C:\Program Files\whisper.cpp\build\bin\whisper-server.exe",
                    @"C:\Program Files\whisper-cpp\build\bin\Release\whisper-server.exe",
                    @"C:\Program Files\whisper-cpp\build\bin\whisper-server.exe"
                ]);
                break;
            case OperatingSystemPlatform.MacOS:
                candidates.AddRange([
                    "/opt/homebrew/bin/whisper-server",
                    "/usr/local/bin/whisper-server",
                    "/opt/homebrew/opt/whisper-cpp/bin/whisper-server",
                    "/usr/local/opt/whisper-cpp/bin/whisper-server"
                ]);
                break;
            case OperatingSystemPlatform.Linux:
                candidates.AddRange([
                    LegacyLinuxWhisperServerPath,
                    "/usr/local/bin/whisper-server",
                    "/usr/bin/whisper.cpp/build/bin/whisper-server"
                ]);
                break;
        }

        if (!string.IsNullOrWhiteSpace(homeDirectory))
            candidates.AddRange([
                Path.Combine(homeDirectory, "whisper.cpp", "build", "bin", "whisper-server"),
                Path.Combine(homeDirectory, "whisper.cpp", "build", "bin", "Release", "whisper-server.exe"),
                Path.Combine(homeDirectory, "src", "whisper.cpp", "build", "bin", "whisper-server"),
                Path.Combine(homeDirectory, "Code", "whisper.cpp", "build", "bin", "whisper-server"),
                Path.Combine(homeDirectory, "EZJiboServer", "whisper.cpp", "build", "bin", "whisper-server"),
                Path.Combine(homeDirectory, "EZOpenJibo", "whisper.cpp", "build", "bin", "whisper-server")
            ]);

        candidates.Add("whisper-server");
        return candidates;
    }

    private static string? ResolveOptionalString(
        string? configured,
        IReadOnlyList<string> environmentVariableNames,
        Func<string, string?> getEnvironmentVariable)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();

        return environmentVariableNames
            .Select(getEnvironmentVariable)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            ?.Trim();
    }

    private static int ResolvePositiveInt(
        int configured,
        IReadOnlyList<string> environmentVariableNames,
        Func<string, string?> getEnvironmentVariable,
        int fallback)
    {
        foreach (var name in environmentVariableNames)
        {
            var raw = getEnvironmentVariable(name);
            if (int.TryParse(raw, out var parsed) && parsed > 0) return parsed;
        }

        return configured > 0 ? configured : fallback;
    }

    private static int ResolveNonNegativeInt(
        int configured,
        IReadOnlyList<string> environmentVariableNames,
        Func<string, string?> getEnvironmentVariable,
        int fallback)
    {
        foreach (var name in environmentVariableNames)
        {
            var raw = getEnvironmentVariable(name);
            if (int.TryParse(raw, out var parsed) && parsed >= 0) return parsed;
        }

        return configured >= 0 ? configured : fallback;
    }
}
