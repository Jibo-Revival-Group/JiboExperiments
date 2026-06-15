namespace Jibo.Cloud.Infrastructure.Audio;

public static class BufferedAudioSttPathResolver
{
    private const string LegacyLinuxFfmpegPath = "/usr/bin/ffmpeg";
    private const string LegacyLinuxWhisperCliPath = "/usr/bin/whisper.cpp/build/bin/whisper-cli";
    private const string LegacyLinuxWhisperModelPath = "/usr/bin/whisper.cpp/models/ggml-base.en.bin";

    public static BufferedAudioSttOptions Resolve(BufferedAudioSttOptions source)
    {
        return Resolve(
            source,
            Environment.GetEnvironmentVariable,
            File.Exists,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            OperatingSystem.IsMacOS(),
            OperatingSystem.IsLinux());
    }

    public static BufferedAudioSttOptions Resolve(
        BufferedAudioSttOptions source,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists,
        string? homeDirectory,
        bool isMacOS,
        bool isLinux)
    {
        return new BufferedAudioSttOptions
        {
            EnableLocalWhisperCpp = source.EnableLocalWhisperCpp,
            FfmpegPath = ResolveExecutable(
                source.FfmpegPath,
                ["OPENJIBO_STT_FFMPEG_PATH", "FFMPEG_PATH"],
                LegacyLinuxFfmpegPath,
                BuildFfmpegCandidates(isMacOS, isLinux),
                getEnvironmentVariable,
                fileExists),
            WhisperCliPath = ResolveExecutable(
                source.WhisperCliPath,
                ["OPENJIBO_STT_WHISPER_CLI_PATH", "WHISPER_CLI_PATH"],
                LegacyLinuxWhisperCliPath,
                BuildWhisperCliCandidates(isMacOS, isLinux, homeDirectory),
                getEnvironmentVariable,
                fileExists),
            WhisperModelPath = ResolveRequiredFile(
                source.WhisperModelPath,
                ["OPENJIBO_STT_WHISPER_MODEL_PATH", "WHISPER_MODEL_PATH"],
                LegacyLinuxWhisperModelPath,
                BuildWhisperModelCandidates(isMacOS, isLinux, homeDirectory),
                getEnvironmentVariable,
                fileExists),
            WhisperLanguage = source.WhisperLanguage,
            TempDirectory = source.TempDirectory,
            CleanupTempFiles = source.CleanupTempFiles
        };
    }

    private static string? ResolveExecutable(
        string? configured,
        IReadOnlyList<string> environmentVariableNames,
        string legacyLinuxDefault,
        IReadOnlyList<string> discoveryCandidates,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists)
    {
        if (IsRelativeOrExistingAbsolute(configured, fileExists)) return configured;

        var environmentPath = ResolveEnvironmentPath(environmentVariableNames, getEnvironmentVariable, fileExists);
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
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists)
    {
        if (IsRelativeOrExistingAbsolute(configured, fileExists)) return configured;

        var environmentPath = ResolveEnvironmentPath(environmentVariableNames, getEnvironmentVariable, fileExists);
        if (!string.IsNullOrWhiteSpace(environmentPath)) return environmentPath;

        if (!ShouldDiscover(configured, legacyLinuxDefault)) return configured;

        return discoveryCandidates.FirstOrDefault(candidate => fileExists(candidate)) ?? configured;
    }

    private static string? ResolveEnvironmentPath(
        IReadOnlyList<string> environmentVariableNames,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists)
    {
        foreach (var name in environmentVariableNames)
        {
            var value = getEnvironmentVariable(name);
            if (IsRelativeOrExistingAbsolute(value, fileExists)) return value;
        }

        return null;
    }

    private static bool IsRelativeOrExistingAbsolute(string? path, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return !Path.IsPathRooted(path) || fileExists(path);
    }

    private static bool ShouldDiscover(string? configured, string legacyLinuxDefault)
    {
        return string.IsNullOrWhiteSpace(configured) ||
               string.Equals(configured, legacyLinuxDefault, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> BuildFfmpegCandidates(bool isMacOS, bool isLinux)
    {
        var candidates = new List<string>();
        if (isMacOS)
            candidates.AddRange([
                "/opt/homebrew/bin/ffmpeg",
                "/usr/local/bin/ffmpeg"
            ]);

        if (isLinux)
            candidates.AddRange([
                LegacyLinuxFfmpegPath,
                "/usr/local/bin/ffmpeg"
            ]);

        candidates.Add("ffmpeg");
        return candidates;
    }

    private static IReadOnlyList<string> BuildWhisperCliCandidates(bool isMacOS, bool isLinux, string? homeDirectory)
    {
        var candidates = new List<string>();
        if (isMacOS)
            candidates.AddRange([
                "/opt/homebrew/bin/whisper-cli",
                "/usr/local/bin/whisper-cli",
                "/opt/homebrew/opt/whisper-cpp/bin/whisper-cli",
                "/usr/local/opt/whisper-cpp/bin/whisper-cli"
            ]);

        if (isLinux)
            candidates.AddRange([
                LegacyLinuxWhisperCliPath,
                "/usr/local/bin/whisper-cli"
            ]);

        if (!string.IsNullOrWhiteSpace(homeDirectory))
            candidates.AddRange([
                Path.Combine(homeDirectory, "whisper.cpp", "build", "bin", "whisper-cli"),
                Path.Combine(homeDirectory, "src", "whisper.cpp", "build", "bin", "whisper-cli"),
                Path.Combine(homeDirectory, "Code", "whisper.cpp", "build", "bin", "whisper-cli")
            ]);

        candidates.Add("whisper-cli");
        return candidates;
    }

    private static IReadOnlyList<string> BuildWhisperModelCandidates(bool isMacOS, bool isLinux, string? homeDirectory)
    {
        var candidates = new List<string>();
        if (isMacOS)
            candidates.AddRange([
                "/opt/homebrew/share/whisper-cpp/models/ggml-base.en.bin",
                "/opt/homebrew/share/whisper.cpp/models/ggml-base.en.bin",
                "/opt/homebrew/opt/whisper-cpp/share/whisper-cpp/models/ggml-base.en.bin",
                "/opt/homebrew/opt/whisper.cpp/share/whisper.cpp/models/ggml-base.en.bin",
                "/usr/local/share/whisper-cpp/models/ggml-base.en.bin",
                "/usr/local/share/whisper.cpp/models/ggml-base.en.bin"
            ]);

        if (isLinux)
            candidates.AddRange([
                LegacyLinuxWhisperModelPath,
                "/usr/local/share/whisper-cpp/models/ggml-base.en.bin",
                "/usr/local/share/whisper.cpp/models/ggml-base.en.bin"
            ]);

        if (!string.IsNullOrWhiteSpace(homeDirectory))
            candidates.AddRange([
                Path.Combine(homeDirectory, "whisper.cpp", "models", "ggml-base.en.bin"),
                Path.Combine(homeDirectory, "src", "whisper.cpp", "models", "ggml-base.en.bin"),
                Path.Combine(homeDirectory, "Code", "whisper.cpp", "models", "ggml-base.en.bin"),
                Path.Combine(homeDirectory, "Library", "Application Support", "openjibo", "whisper",
                    "ggml-base.en.bin")
            ]);

        return candidates;
    }
}