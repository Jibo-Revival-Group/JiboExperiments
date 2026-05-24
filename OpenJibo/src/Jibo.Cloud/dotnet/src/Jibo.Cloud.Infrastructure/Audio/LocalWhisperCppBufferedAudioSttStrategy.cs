using System.Text.Json;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Infrastructure.Audio;

public sealed class LocalWhisperCppBufferedAudioSttStrategy(
    BufferedAudioSttOptions options,
    IExternalProcessRunner processRunner) : ISttStrategy
{
    private const int MinimumBufferedAudioBytes = 64;
    private const int ShortAnswerBufferedAudioBytes = 16;

    public string Name => "local-whispercpp-buffered-audio";

    public bool CanHandle(TurnContext turn)
    {
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

        try
        {
            await File.WriteAllBytesAsync(oggPath, OggOpusAudioNormalizer.Normalize(frames), cancellationToken);

            await processRunner.RunAsync(
                options.FfmpegPath!,
                ["-y", "-i", oggPath, "-ar", "16000", "-ac", "1", "-f", "wav", wavPath],
                cancellationToken);

            var whisperResult = await processRunner.RunAsync(
                options.WhisperCliPath!,
                ["-m", options.WhisperModelPath!, "-f", wavPath, "-l", options.WhisperLanguage],
                cancellationToken);

            var transcript = ExtractTranscript(whisperResult.StdOut);
            transcript = AudioTranscriptNormalizer.NormalizeLooseTranscript(transcript);
            if (string.IsNullOrWhiteSpace(transcript))
                throw new InvalidOperationException("whisper.cpp returned no transcript for the buffered audio turn.");

            return new SttResult
            {
                Text = transcript,
                Provider = Name,
                Locale = turn.Locale,
                Metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["bufferedAudioBytes"] = ReadBufferedAudioBytes(turn),
                    ["bufferedAudioChunks"] = frames.Count,
                    ["ffmpegPath"] = options.FfmpegPath,
                    ["whisperCliPath"] = options.WhisperCliPath,
                    ["wavPath"] = wavPath
                }
            };
        }
        finally
        {
            if (options.CleanupTempFiles)
            {
                TryDelete(oggPath);
                TryDelete(wavPath);
            }
        }
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
