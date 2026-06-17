using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Infrastructure.Audio;

public sealed class LocalWhisperCppBufferedAudioSttStrategy : ISttStrategy
{
    private const int MinimumBufferedAudioBytes = 64;
    private const int ShortAnswerBufferedAudioBytes = 16;
    private const int MinimumTranscribableWavBytes = 1024;
    private readonly BufferedAudioSttOptions options;
    private readonly IExternalProcessRunner processRunner;
    private readonly ILogger<LocalWhisperCppBufferedAudioSttStrategy> logger;

    public LocalWhisperCppBufferedAudioSttStrategy(
        BufferedAudioSttOptions options,
        IExternalProcessRunner processRunner,
        ILogger<LocalWhisperCppBufferedAudioSttStrategy> logger)
    {
        this.options = BufferedAudioSttPathResolver.Resolve(options);
        this.processRunner = processRunner;
        this.logger = logger;
    }

    public LocalWhisperCppBufferedAudioSttStrategy(
        BufferedAudioSttOptions options,
        IExternalProcessRunner processRunner)
        : this(options, processRunner, NullLogger<LocalWhisperCppBufferedAudioSttStrategy>.Instance)
    {
    }

    public string Name => "local-whispercpp-buffered-audio";

    public bool CanHandle(TurnContext turn)
    {
        logger.LogDebug(
            "STT can-handle check start turnId={TurnId} bufferedBytes={BufferedBytes} frames={FrameCount} enabled={Enabled}",
            turn.TurnId,
            ReadBufferedAudioBytes(turn),
            ReadBufferedAudioFrames(turn).Count,
            options.EnableLocalWhisperCpp);

        return options.EnableLocalWhisperCpp &&
               IsConfiguredPathAvailable(options.FfmpegPath, false) &&
               IsConfiguredPathAvailable(options.WhisperCliPath, true) &&
               IsConfiguredPathAvailable(options.WhisperModelPath, true) &&
               ReadBufferedAudioFrames(turn).Any(ContainsOpusIdentificationHeader) &&
               !IsBelowNoiseFloor(turn, ReadBufferedAudioBytes(turn));
    }

    public async Task<SttResult> TranscribeAsync(TurnContext turn, CancellationToken cancellationToken = default)
    {
        var frames = ReadBufferedAudioFrames(turn);
        logger.LogDebug(
            "STT transcription start turnId={TurnId} bufferedBytes={BufferedBytes} frames={FrameCount} locale={Locale}",
            turn.TurnId,
            ReadBufferedAudioBytes(turn),
            frames.Count,
            turn.Locale);

        if (frames.Count == 0)
            throw new InvalidOperationException("Local whisper.cpp STT requires buffered websocket audio frames.");

        if (!frames.Any(ContainsOpusIdentificationHeader))
            throw new InvalidOperationException(
                "Local whisper.cpp STT requires buffered Ogg/Opus audio with an Opus identification header.");

        if (IsBelowNoiseFloor(turn, ReadBufferedAudioBytes(turn)))
            throw new InvalidOperationException(
                "Local whisper.cpp STT rejected buffered audio as too short or noisy for transcription.");

        var tempDirectory = options.TempDirectory;
        if (string.IsNullOrWhiteSpace(tempDirectory)) tempDirectory = Path.Combine(Path.GetTempPath(), "openjibo-stt");

        Directory.CreateDirectory(tempDirectory);

        var baseName = $"turn-{turn.TurnId}";
        var oggPath = Path.Combine(tempDirectory, $"{baseName}.ogg");
        var wavPath = Path.Combine(tempDirectory, $"{baseName}.wav");
        logger.LogDebug("STT transcription files prepared tempDirectory={TempDirectory} oggPath={OggPath} wavPath={WavPath}",
            tempDirectory,
            oggPath,
            wavPath);

        try
        {
            var normalizedOgg = OggOpusAudioNormalizer.Normalize(frames);
            await File.WriteAllBytesAsync(oggPath, normalizedOgg, cancellationToken);
            logger.LogDebug(
                "STT normalized OGG written turnId={TurnId} oggBytes={OggBytes} frameCount={FrameCount}",
                turn.TurnId,
                normalizedOgg.Length,
                frames.Count);

            logger.LogDebug("STT ffmpeg launch turnId={TurnId} ffmpegPath={FfmpegPath}", turn.TurnId, options.FfmpegPath);
            var ffmpegResult = await processRunner.RunAsync(
                options.FfmpegPath!,
                ["-y", "-i", oggPath, "-ar", "16000", "-ac", "1", "-f", "wav", wavPath],
                cancellationToken);
            logger.LogDebug(
                "STT ffmpeg finished turnId={TurnId} exitCode={ExitCode} stdoutBytes={StdOutBytes} stderrBytes={StdErrBytes}",
                turn.TurnId,
                ffmpegResult.ExitCode,
                ffmpegResult.StdOut.Length,
                ffmpegResult.StdErr.Length);

            var wavBytes = File.Exists(wavPath) ? new FileInfo(wavPath).Length : 0;
            if (wavBytes < MinimumTranscribableWavBytes)
            {
                logger.LogDebug(
                    "STT rejecting tiny WAV turnId={TurnId} wavBytes={WavBytes} minimum={MinimumWavBytes}",
                    turn.TurnId,
                    wavBytes,
                    MinimumTranscribableWavBytes);
                return BuildResult(string.Empty, turn, wavPath, ffmpegResult, string.Empty, string.Empty);
            }

            logger.LogDebug("STT whisper launch turnId={TurnId} whisperCliPath={WhisperCliPath} modelPath={ModelPath}",
                turn.TurnId,
                options.WhisperCliPath,
                options.WhisperModelPath);
            var whisperResult = await processRunner.RunAsync(
                options.WhisperCliPath!,
                ["-m", options.WhisperModelPath!, "-f", wavPath, "-l", options.WhisperLanguage],
                cancellationToken);
            logger.LogDebug(
                "STT whisper finished turnId={TurnId} exitCode={ExitCode} stdoutBytes={StdOutBytes} stderrBytes={StdErrBytes}",
                turn.TurnId,
                whisperResult.ExitCode,
                whisperResult.StdOut.Length,
                whisperResult.StdErr.Length);

            var transcript = ExtractTranscript(whisperResult.StdOut);
            logger.LogDebug("STT extracted transcript turnId={TurnId} rawTranscript={Transcript}", turn.TurnId, transcript);
            transcript = AudioTranscriptNormalizer.NormalizeLooseTranscript(transcript);
            if (TranscriptHeuristics.IsLikelyRobotSelfAudioTranscript(transcript))
            {
                logger.LogDebug(
                    "STT rejected likely robot self-audio transcript turnId={TurnId} transcript={Transcript}",
                    turn.TurnId,
                    transcript);

                var transcriptHint = AudioTranscriptNormalizer.NormalizeLooseTranscript(ReadTranscriptHint(turn));
                if (!string.IsNullOrWhiteSpace(transcriptHint) &&
                    !TranscriptHeuristics.IsLikelyRobotSelfAudioTranscript(transcriptHint))
                {
                    logger.LogDebug(
                        "STT using transcript hint after self-audio rejection turnId={TurnId} transcriptHint={TranscriptHint}",
                        turn.TurnId,
                        transcriptHint);
                    transcript = transcriptHint;
                }
                else
                {
                    transcript = string.Empty;
                }
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                logger.LogDebug("STT falling back to transcript hint turnId={TurnId}", turn.TurnId);
                transcript = AudioTranscriptNormalizer.NormalizeLooseTranscript(ReadTranscriptHint(turn));
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                logger.LogDebug(
                    "STT returning blank transcript turnId={TurnId} oggBytes={OggBytes} wavBytes={WavBytes} ffmpegExit={FfmpegExit} whisperExit={WhisperExit}",
                    turn.TurnId,
                    new FileInfo(oggPath).Length,
                    wavBytes,
                    ffmpegResult.ExitCode,
                    whisperResult.ExitCode);
            }

            return BuildResult(
                transcript,
                turn,
                wavPath,
                ffmpegResult,
                whisperResult.StdOut,
                whisperResult.StdErr);
        }
        finally
        {
            if (options.CleanupTempFiles)
            {
                TryDelete(oggPath);
                TryDelete(wavPath);
            }

            logger.LogDebug("STT transcription end turnId={TurnId} cleanupTempFiles={CleanupTempFiles}", turn.TurnId,
                options.CleanupTempFiles);
        }
    }

    private static string? ReadTranscriptHint(TurnContext turn)
    {
        return turn.Attributes.TryGetValue("audioTranscriptHint", out var transcriptHint)
            ? transcriptHint?.ToString()
            : null;
    }

    private static IReadOnlyList<byte[]> ReadBufferedAudioFrames(TurnContext turn)
    {
        if (!turn.Attributes.TryGetValue("bufferedAudioFrames", out var value) || value is null) return [];

        return value switch
        {
            byte[][] jagged => jagged,
            IReadOnlyList<byte[]> typed => typed,
            IEnumerable<byte[]> enumerable => enumerable.ToArray(),
            JsonElement { ValueKind: JsonValueKind.Array } jsonElement => jsonElement.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.Array)
                .Select(static item => item.EnumerateArray().Select(static b => (byte)b.GetInt32()).ToArray())
                .ToArray(),
            _ => []
        };
    }

    private static int ReadBufferedAudioBytes(TurnContext turn)
    {
        return turn.Attributes.TryGetValue("bufferedAudioBytes", out var bufferedAudioBytes) &&
               bufferedAudioBytes is not null
            ? bufferedAudioBytes switch
            {
                int value => value,
                long value => (int)value,
                string value when int.TryParse(value, out var parsed) => parsed,
                _ => 0
            }
            : 0;
    }

    private static bool IsBelowNoiseFloor(TurnContext turn, int bufferedAudioBytes)
    {
        if (bufferedAudioBytes <= 0) return false;

        var minimumBufferedAudioBytes = IsShortAnswerTurn(turn)
            ? ShortAnswerBufferedAudioBytes
            : MinimumBufferedAudioBytes;

        return bufferedAudioBytes < minimumBufferedAudioBytes;
    }

    private static bool IsShortAnswerTurn(TurnContext turn)
    {
        var rules = ReadRules(turn, "listenRules")
            .Concat(ReadRules(turn, "clientRules"))
            .Concat(ReadRules(turn, "listenAsrHints"));

        return rules.Any(IsShortAnswerRule);
    }

    private static bool IsShortAnswerRule(string rule)
    {
        return string.Equals(rule, "$YESNO", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "clock/alarm_timer_change", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "clock/alarm_timer_none_set", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "create/is_it_a_keeper", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "settings/download_now_later", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "shared/yes_no", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "surprises-date/offer_date_fact", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "surprises-ota/want_to_download_now", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "word-of-the-day/surprise", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "word-of-the-day/right_word", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "word-of-the-day/puzzle", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ReadRules(TurnContext turn, string key)
    {
        if (!turn.Attributes.TryGetValue(key, out var value) || value is null) return [];

        return value switch
        {
            IReadOnlyList<string> typed => typed,
            IEnumerable<string> enumerable => enumerable,
            JsonElement { ValueKind: JsonValueKind.Array } jsonElement => jsonElement.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString() ?? string.Empty),
            _ => []
        };
    }

    private static bool ContainsOpusIdentificationHeader(byte[] frame)
    {
        return frame.AsSpan().IndexOf("OpusHead"u8) >= 0;
    }

    private SttResult BuildResult(
        string transcript,
        TurnContext turn,
        string wavPath,
        ExternalProcessResult ffmpegResult,
        string whisperStdOut,
        string whisperStdErr)
    {
        return new SttResult
        {
            Text = transcript,
            Provider = Name,
            Locale = turn.Locale,
            Metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["bufferedAudioBytes"] = ReadBufferedAudioBytes(turn),
                ["bufferedAudioChunks"] = ReadBufferedAudioFrames(turn).Count,
                ["ffmpegPath"] = options.FfmpegPath,
                ["whisperCliPath"] = options.WhisperCliPath,
                ["wavPath"] = wavPath,
                ["ffmpegStdOut"] = ffmpegResult.StdOut,
                ["ffmpegStdErr"] = ffmpegResult.StdErr,
                ["whisperStdOut"] = whisperStdOut,
                ["whisperStdErr"] = whisperStdErr
            }
        };
    }

    private static string ExtractTranscript(string standardOutput)
    {
        var lines = standardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var timecoded = lines
            .Where(static line => line.StartsWith('[') && line.Contains("-->", StringComparison.Ordinal))
            .Select(static line =>
            {
                var closingBracket = line.IndexOf(']');
                return closingBracket >= 0 ? line[(closingBracket + 1)..].Trim() : line.Trim();
            })
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        return timecoded.Length > 0 ? string.Join(" ", timecoded).Trim() : string.Join(" ", lines).Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static bool IsConfiguredPathAvailable(string? path, bool checkFileExists)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        if (!Path.IsPathRooted(path)) return true;

        return !checkFileExists || File.Exists(path);
    }
}
