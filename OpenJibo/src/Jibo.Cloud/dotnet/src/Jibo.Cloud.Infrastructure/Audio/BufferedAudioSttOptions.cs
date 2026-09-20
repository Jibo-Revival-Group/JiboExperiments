namespace Jibo.Cloud.Infrastructure.Audio;

public sealed class BufferedAudioSttOptions
{
    public bool EnableLocalWhisperCpp { get; set; }
    public bool EnableAzureSpeech { get; set; }
    public bool EnableWhisperServer { get; set; }
    /// <summary>
    /// When true (default), the API process starts a local whisper-server for loopback
    /// WhisperServerUrl values if one is not already listening. Applies to dotnet run,
    /// published binaries, and containers — not Docker-only.
    /// </summary>
    public bool AutoStartWhisperServer { get; set; } = true;
    public string? FfmpegPath { get; set; }
    public string? WhisperCliPath { get; set; }
    public string? WhisperModelPath { get; set; }
    /// <summary>Optional path to whisper.cpp whisper-server. Discovered beside whisper-cli when unset.</summary>
    public string? WhisperServerBinPath { get; set; }
    public string? WhisperServerUrl { get; set; } = "http://127.0.0.1:8090";
    public string? AzureSpeechRegion { get; set; }
    public string? AzureSpeechSubscriptionKey { get; set; }
    public string? AzureSpeechEndpoint { get; set; }
    public TimeSpan AzureSpeechRequestTimeout { get; set; } = TimeSpan.FromSeconds(8);
    public string WhisperLanguage { get; set; } = "en";
    /// <summary>whisper.cpp --audio-ctx. 512 covers ~10s of audio and avoids the 30s pad.</summary>
    public int WhisperAudioContext { get; set; } = 512;
    /// <summary>whisper.cpp -t. 0 means Environment.ProcessorCount.</summary>
    public int WhisperThreads { get; set; }
    /// <summary>whisper.cpp -bs beam size.</summary>
    public int WhisperBeamSize { get; set; } = 1;
    public string? TempDirectory { get; set; }
    public bool CleanupTempFiles { get; set; }
}
