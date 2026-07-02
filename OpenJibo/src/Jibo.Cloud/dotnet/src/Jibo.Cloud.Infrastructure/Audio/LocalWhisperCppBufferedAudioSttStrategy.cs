using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Infrastructure.Audio;

public sealed class LocalWhisperCppBufferedAudioSttStrategy(
    BufferedAudioSttOptions options,
    IExternalProcessRunner processRunner,
    ILogger<LocalWhisperCppBufferedAudioSttStrategy> logger)
    : ISttStrategy
{
    private const int MinimumBufferedAudioBytes = 64;
    private const int ShortAnswerBufferedAudioBytes = 16;
    private const int MinimumTranscribableWavBytes = 1024;

    private const string FfmpegAudioPreprocessFilter =
        "silenceremove=start_periods=1:start_duration=0.03:start_threshold=-45dB:stop_periods=-1:stop_duration=0.5:stop_threshold=-45dB,volume=8dB";

    private readonly BufferedAudioSttOptions _options = BufferedAudioSttPathResolver.Resolve(options);

    public LocalWhisperCppBufferedAudioSttStrategy(
        BufferedAudioSttOptions options,
        IExternalProcessRunner processRunner)
        : this(options, processRunner, NullLogger<LocalWhisperCppBufferedAudioSttStrategy>.Instance)
    {
    }

    public string Name => "local-whispercpp-buffered-audio";

    public bool CanHandle(TurnContext turn)
    {
        var frames = ReadBufferedAudioFrames(turn);
        var audioBearingPageCount = BufferedAudioPageClassifier.CountAudioBearingPages(frames);
        var metadataPageCount = BufferedAudioPageClassifier.CountMetadataPages(frames);

        logger.LogDebug(
            "STT can-handle check start turnId={TurnId} bufferedBytes={BufferedBytes} rawFrames={RawFrames} audioPages={AudioPages} metadataPages={MetadataPages} enabled={Enabled}",
            turn.TurnId,
            ReadBufferedAudioBytes(turn),
            frames.Count,
            audioBearingPageCount,
            metadataPageCount,
            _options.EnableLocalWhisperCpp);

        return _options.EnableLocalWhisperCpp &&
               IsConfiguredPathAvailable(_options.FfmpegPath, false) &&
               IsConfiguredPathAvailable(_options.WhisperCliPath, true) &&
               IsConfiguredPathAvailable(_options.WhisperModelPath, true) &&
               frames.Any(ContainsOpusIdentificationHeader) &&
               audioBearingPageCount > 0 &&
               !IsBelowNoiseFloor(turn, ReadBufferedAudioBytes(turn));
    }

    public async Task<SttResult> TranscribeAsync(TurnContext turn, CancellationToken cancellationToken = default)
    {
        var frames = ReadBufferedAudioFrames(turn);
        var audioBearingPageCount = BufferedAudioPageClassifier.CountAudioBearingPages(frames);
        var metadataPageCount = BufferedAudioPageClassifier.CountMetadataPages(frames);
        logger.LogDebug(
            "STT transcription start turnId={TurnId} bufferedBytes={BufferedBytes} rawFrames={RawFrames} audioPages={AudioPages} metadataPages={MetadataPages} locale={Locale}",
            turn.TurnId,
            ReadBufferedAudioBytes(turn),
            frames.Count,
            audioBearingPageCount,
            metadataPageCount,
            turn.Locale);

        if (frames.Count == 0)
            throw new InvalidOperationException("Local whisper.cpp STT requires buffered websocket audio frames.");

        if (!frames.Any(ContainsOpusIdentificationHeader))
            throw new InvalidOperationException(
                "Local whisper.cpp STT requires buffered Ogg/Opus audio with an Opus identification header.");

        if (IsBelowNoiseFloor(turn, ReadBufferedAudioBytes(turn)))
            throw new InvalidOperationException(
                "Local whisper.cpp STT rejected buffered audio as too short or noisy for transcription.");

        var tempDirectory = _options.TempDirectory;
        if (string.IsNullOrWhiteSpace(tempDirectory)) tempDirectory = Path.Combine(Path.GetTempPath(), "openjibo-stt");

        Directory.CreateDirectory(tempDirectory);

        var baseName = $"turn-{turn.TurnId}";
        var oggPath = Path.Combine(tempDirectory, $"{baseName}.ogg");
        var wavPath = Path.Combine(tempDirectory, $"{baseName}.wav");
        logger.LogDebug(
            "STT transcription files prepared tempDirectory={TempDirectory} oggPath={OggPath} wavPath={WavPath}",
            tempDirectory,
            oggPath,
            wavPath);

        try
        {
            var pageCounts = BufferedAudioPageClassifier.Describe(frames);
            var normalizedOgg = OggOpusAudioNormalizer.Normalize(frames);
            await File.WriteAllBytesAsync(oggPath, normalizedOgg, cancellationToken);
            logger.LogDebug(
                "STT normalized OGG written turnId={TurnId} oggBytes={OggBytes} rawFrames={RawFrames} audioPages={AudioPages} metadataPages={MetadataPages}",
                turn.TurnId,
                normalizedOgg.Length,
                pageCounts.RawFrameCount,
                pageCounts.AudioBearingPageCount,
                pageCounts.MetadataPageCount);

            logger.LogDebug(
                "STT ffmpeg launch turnId={TurnId} ffmpegPath={FfmpegPath} audioFilter={AudioFilter}",
                turn.TurnId,
                _options.FfmpegPath,
                FfmpegAudioPreprocessFilter);
            var ffmpegResult = await processRunner.RunAsync(
                _options.FfmpegPath!,
                [
                    "-y", "-i", oggPath,
                    "-af", FfmpegAudioPreprocessFilter,
                    "-ar", "16000",
                    "-ac", "1",
                    "-f", "wav",
                    wavPath
                ],
                cancellationToken);
            logger.LogDebug(
                "STT ffmpeg finished turnId={TurnId} exitCode={ExitCode} stdoutBytes={StdOutBytes} stderrBytes={StdErrBytes}",
                turn.TurnId,
                ffmpegResult.ExitCode,
                ffmpegResult.StdOut.Length,
                ffmpegResult.StdErr.Length);

            var wavBytes = File.Exists(wavPath) ? new FileInfo(wavPath).Length : 0;
            logger.LogDebug(
                "STT WAV prepared turnId={TurnId} wavBytes={WavBytes} audioFilter={AudioFilter}",
                turn.TurnId,
                wavBytes,
                FfmpegAudioPreprocessFilter);

            if (wavBytes < MinimumTranscribableWavBytes)
            {
                logger.LogDebug(
                    "STT rejecting tiny WAV turnId={TurnId} wavBytes={WavBytes} minimum={MinimumWavBytes} rawFrames={RawFrames} audioPages={AudioPages}",
                    turn.TurnId,
                    wavBytes,
                    MinimumTranscribableWavBytes,
                    pageCounts.RawFrameCount,
                    pageCounts.AudioBearingPageCount);
                if (!_options.CleanupTempFiles)
                {
                    TryDelete(oggPath);
                    TryDelete(wavPath);
                    logger.LogDebug(
                        "STT deleted rejected tiny WAV artifacts turnId={TurnId} oggPath={OggPath} wavPath={WavPath}",
                        turn.TurnId,
                        oggPath,
                        wavPath);
                }

                return BuildResult(string.Empty, turn, wavPath, ffmpegResult, string.Empty, string.Empty, pageCounts);
            }

            logger.LogDebug("STT whisper launch turnId={TurnId} whisperCliPath={WhisperCliPath} modelPath={ModelPath}",
                turn.TurnId,
                _options.WhisperCliPath,
                _options.WhisperModelPath);
            var whisperResult = await processRunner.RunAsync(
                _options.WhisperCliPath!,
                ["-m", _options.WhisperModelPath!, "-f", wavPath, "-l", _options.WhisperLanguage],
                cancellationToken);
            logger.LogDebug(
                "STT whisper finished turnId={TurnId} exitCode={ExitCode} stdoutBytes={StdOutBytes} stderrBytes={StdErrBytes}",
                turn.TurnId,
                whisperResult.ExitCode,
                whisperResult.StdOut.Length,
                whisperResult.StdErr.Length);

            var transcript = ExtractTranscript(whisperResult.StdOut);
            logger.LogDebug("STT extracted transcript turnId={TurnId} rawTranscript={Transcript}", turn.TurnId,
                transcript);
            transcript = AudioTranscriptNormalizer.NormalizeLooseTranscript(transcript);
            if (TranscriptHeuristics.IsLikelyRobotSelfAudioTranscript(transcript))
            {
                var embeddedWakePhraseCommand = TranscriptHeuristics.ExtractWakePhraseCommand(transcript);
                if (!string.IsNullOrWhiteSpace(embeddedWakePhraseCommand) &&
                    !string.Equals(embeddedWakePhraseCommand, transcript, StringComparison.Ordinal) &&
                    !TranscriptHeuristics.IsLikelyRobotSelfAudioTranscript(embeddedWakePhraseCommand))
                {
                    logger.LogDebug(
                        "STT preserved embedded wake-phrase command after robot self-audio turnId={TurnId} transcript={Transcript} command={Command}",
                        turn.TurnId,
                        transcript,
                        embeddedWakePhraseCommand);
                    transcript = embeddedWakePhraseCommand;
                }
                else
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
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                logger.LogDebug("STT falling back to transcript hint turnId={TurnId}", turn.TurnId);
                transcript = AudioTranscriptNormalizer.NormalizeLooseTranscript(ReadTranscriptHint(turn));
            }

            if (!string.IsNullOrWhiteSpace(transcript))
                return BuildResult(
                    transcript,
                    turn,
                    wavPath,
                    ffmpegResult,
                    whisperResult.StdOut,
                    whisperResult.StdErr,
                    pageCounts);

            logger.LogDebug(
                "STT returning blank transcript turnId={TurnId} oggBytes={OggBytes} wavBytes={WavBytes} rawFrames={RawFrames} audioPages={AudioPages} ffmpegExit={FfmpegExit} whisperExit={WhisperExit}",
                turn.TurnId,
                new FileInfo(oggPath).Length,
                wavBytes,
                pageCounts.RawFrameCount,
                pageCounts.AudioBearingPageCount,
                ffmpegResult.ExitCode,
                whisperResult.ExitCode);

            if (_options.CleanupTempFiles)
                return BuildResult(
                    transcript,
                    turn,
                    wavPath,
                    ffmpegResult,
                    whisperResult.StdOut,
                    whisperResult.StdErr,
                    pageCounts);

            TryDelete(oggPath);
            TryDelete(wavPath);

            logger.LogDebug(
                "STT deleted blank transcription artifacts turnId={TurnId} oggPath={OggPath} wavPath={WavPath}",
                turn.TurnId,
                oggPath,
                wavPath);

            return BuildResult(
                transcript,
                turn,
                wavPath,
                ffmpegResult,
                whisperResult.StdOut,
                whisperResult.StdErr,
                pageCounts);
        }
        finally
        {
            if (_options.CleanupTempFiles)
            {
                TryDelete(oggPath);
                TryDelete(wavPath);
            }

            logger.LogDebug("STT transcription end turnId={TurnId} cleanupTempFiles={CleanupTempFiles}", turn.TurnId,
                _options.CleanupTempFiles);
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
        string whisperStdErr,
        BufferedAudioPageCounts pageCounts)
    {
        return new SttResult
        {
            Text = transcript,
            Provider = Name,
            Locale = turn.Locale,
            Metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["bufferedAudioBytes"] = ReadBufferedAudioBytes(turn),
                ["bufferedAudioChunks"] = pageCounts.RawFrameCount,
                ["bufferedAudioRawFrames"] = pageCounts.RawFrameCount,
                ["bufferedAudioMetadataPages"] = pageCounts.MetadataPageCount,
                ["bufferedAudioAudioBearingPages"] = pageCounts.AudioBearingPageCount,
                ["ffmpegPath"] = _options.FfmpegPath,
                ["whisperCliPath"] = _options.WhisperCliPath,
                ["wavPath"] = wavPath,
                ["ffmpegAudioFilter"] = FfmpegAudioPreprocessFilter,
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
