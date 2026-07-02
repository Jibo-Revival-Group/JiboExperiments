using System.Net.Http.Headers;
using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Infrastructure.Audio;

public sealed class AzureSpeechBufferedAudioSttStrategy(
    BufferedAudioSttOptions options,
    IExternalProcessRunner processRunner,
    HttpClient httpClient,
    ILogger<AzureSpeechBufferedAudioSttStrategy> logger)
    : ISttStrategy
{
    private const int MinimumBufferedAudioBytes = 64;
    private const int ShortAnswerBufferedAudioBytes = 16;
    private const int MinimumTranscribableWavBytes = 1024;

    private const string FfmpegAudioPreprocessFilter =
        "silenceremove=start_periods=1:start_duration=0.03:start_threshold=-45dB:stop_periods=-1:stop_duration=0.5:stop_threshold=-45dB,volume=8dB";

    private readonly BufferedAudioSttOptions _options = BufferedAudioSttPathResolver.Resolve(options);

    public string Name => "azure-speech-buffered-audio";

    public bool CanHandle(TurnContext turn)
    {
        var frames = ReadBufferedAudioFrames(turn);
        var audioBearingPageCount = BufferedAudioPageClassifier.CountAudioBearingPages(frames);
        var metadataPageCount = BufferedAudioPageClassifier.CountMetadataPages(frames);

        logger.LogDebug(
            "Azure STT can-handle check start turnId={TurnId} bufferedBytes={BufferedBytes} rawFrames={RawFrames} audioPages={AudioPages} metadataPages={MetadataPages} enabled={Enabled} regionConfigured={RegionConfigured} keyConfigured={KeyConfigured}",
            turn.TurnId,
            ReadBufferedAudioBytes(turn),
            frames.Count,
            audioBearingPageCount,
            metadataPageCount,
            _options.EnableAzureSpeech,
            !string.IsNullOrWhiteSpace(_options.AzureSpeechRegion),
            !string.IsNullOrWhiteSpace(_options.AzureSpeechSubscriptionKey));

        return _options.EnableAzureSpeech &&
               !string.IsNullOrWhiteSpace(_options.AzureSpeechRegion) &&
               !string.IsNullOrWhiteSpace(_options.AzureSpeechSubscriptionKey) &&
               IsConfiguredPathAvailable(_options.FfmpegPath, false) &&
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
            "Azure STT transcription start turnId={TurnId} bufferedBytes={BufferedBytes} rawFrames={RawFrames} audioPages={AudioPages} metadataPages={MetadataPages} locale={Locale}",
            turn.TurnId,
            ReadBufferedAudioBytes(turn),
            frames.Count,
            audioBearingPageCount,
            metadataPageCount,
            turn.Locale);

        if (frames.Count == 0)
            throw new InvalidOperationException("Azure speech STT requires buffered websocket audio frames.");

        if (!frames.Any(ContainsOpusIdentificationHeader))
            throw new InvalidOperationException(
                "Azure speech STT requires buffered Ogg/Opus audio with an Opus identification header.");

        if (IsBelowNoiseFloor(turn, ReadBufferedAudioBytes(turn)))
            throw new InvalidOperationException(
                "Azure speech STT rejected buffered audio as too short or noisy for transcription.");

        var tempDirectory = _options.TempDirectory;
        if (string.IsNullOrWhiteSpace(tempDirectory)) tempDirectory = Path.Combine(Path.GetTempPath(), "openjibo-stt");

        Directory.CreateDirectory(tempDirectory);

        var baseName = $"turn-{turn.TurnId}";
        var oggPath = Path.Combine(tempDirectory, $"{baseName}.ogg");
        var wavPath = Path.Combine(tempDirectory, $"{baseName}.wav");
        logger.LogDebug(
            "Azure STT transcription files prepared tempDirectory={TempDirectory} oggPath={OggPath} wavPath={WavPath}",
            tempDirectory,
            oggPath,
            wavPath);

        try
        {
            var pageCounts = BufferedAudioPageClassifier.Describe(frames);
            var normalizedOgg = OggOpusAudioNormalizer.Normalize(frames);
            await File.WriteAllBytesAsync(oggPath, normalizedOgg, cancellationToken);
            logger.LogDebug(
                "Azure STT normalized OGG written turnId={TurnId} oggBytes={OggBytes} rawFrames={RawFrames} audioPages={AudioPages} metadataPages={MetadataPages}",
                turn.TurnId,
                normalizedOgg.Length,
                pageCounts.RawFrameCount,
                pageCounts.AudioBearingPageCount,
                pageCounts.MetadataPageCount);

            logger.LogDebug(
                "Azure STT ffmpeg launch turnId={TurnId} ffmpegPath={FfmpegPath} audioFilter={AudioFilter}",
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
                "Azure STT ffmpeg finished turnId={TurnId} exitCode={ExitCode} stdoutBytes={StdOutBytes} stderrBytes={StdErrBytes}",
                turn.TurnId,
                ffmpegResult.ExitCode,
                ffmpegResult.StdOut.Length,
                ffmpegResult.StdErr.Length);

            var wavBytes = File.Exists(wavPath) ? new FileInfo(wavPath).Length : 0;
            logger.LogDebug(
                "Azure STT WAV prepared turnId={TurnId} wavBytes={WavBytes} audioFilter={AudioFilter}",
                turn.TurnId,
                wavBytes,
                FfmpegAudioPreprocessFilter);

            if (wavBytes < MinimumTranscribableWavBytes)
            {
                logger.LogDebug(
                    "Azure STT rejecting tiny WAV turnId={TurnId} wavBytes={WavBytes} minimum={MinimumWavBytes} rawFrames={RawFrames} audioPages={AudioPages}",
                    turn.TurnId,
                    wavBytes,
                    MinimumTranscribableWavBytes,
                    pageCounts.RawFrameCount,
                    pageCounts.AudioBearingPageCount);
                if (!_options.CleanupTempFiles)
                {
                    TryDelete(oggPath);
                    TryDelete(wavPath);
                }

                return BuildResult(string.Empty, turn, wavPath, ffmpegResult, string.Empty, string.Empty, pageCounts);
            }

            logger.LogDebug(
                "Azure STT speech request launch turnId={TurnId} region={Region} endpoint={Endpoint}",
                turn.TurnId,
                _options.AzureSpeechRegion,
                ResolveEndpoint(turn.Locale));
            var azureResult = await TranscribeWithAzureAsync(turn, wavPath, cancellationToken);
            logger.LogDebug(
                "Azure STT speech request finished turnId={TurnId} status={Status} transcriptBytes={TranscriptBytes}",
                turn.TurnId,
                azureResult.Status,
                azureResult.Transcript.Length);

            var transcript = AudioTranscriptNormalizer.NormalizeLooseTranscript(azureResult.Transcript);
            if (TranscriptHeuristics.IsLikelyRobotSelfAudioTranscript(transcript))
            {
                var embeddedWakePhraseCommand = TranscriptHeuristics.ExtractWakePhraseCommand(transcript);
                if (!string.IsNullOrWhiteSpace(embeddedWakePhraseCommand) &&
                    !string.Equals(embeddedWakePhraseCommand, transcript, StringComparison.Ordinal) &&
                    !TranscriptHeuristics.IsLikelyRobotSelfAudioTranscript(embeddedWakePhraseCommand))
                {
                    transcript = embeddedWakePhraseCommand;
                }
                else
                {
                    var transcriptHint = AudioTranscriptNormalizer.NormalizeLooseTranscript(ReadTranscriptHint(turn));
                    if (!string.IsNullOrWhiteSpace(transcriptHint) &&
                        !TranscriptHeuristics.IsLikelyRobotSelfAudioTranscript(transcriptHint))
                        transcript = transcriptHint;
                    else
                        transcript = string.Empty;
                }
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                transcript = AudioTranscriptNormalizer.NormalizeLooseTranscript(ReadTranscriptHint(turn));
                logger.LogDebug("Azure STT falling back to transcript hint turnId={TurnId}", turn.TurnId);
            }

            if (!string.IsNullOrWhiteSpace(transcript))
                return BuildResult(
                    transcript,
                    turn,
                    wavPath,
                    ffmpegResult,
                    azureResult.RawResponse,
                    azureResult.Status,
                    pageCounts);

            if (_options.CleanupTempFiles)
                return BuildResult(
                    transcript,
                    turn,
                    wavPath,
                    ffmpegResult,
                    azureResult.RawResponse,
                    azureResult.Status,
                    pageCounts);

            TryDelete(oggPath);
            TryDelete(wavPath);

            return BuildResult(
                transcript,
                turn,
                wavPath,
                ffmpegResult,
                azureResult.RawResponse,
                azureResult.Status,
                pageCounts);
        }
        finally
        {
            if (_options.CleanupTempFiles)
            {
                TryDelete(oggPath);
                TryDelete(wavPath);
            }

            logger.LogDebug("Azure STT transcription end turnId={TurnId} cleanupTempFiles={CleanupTempFiles}", turn.TurnId,
                _options.CleanupTempFiles);
        }
    }

    private async Task<AzureSpeechResult> TranscribeWithAzureAsync(
        TurnContext turn,
        string wavPath,
        CancellationToken cancellationToken)
    {
        var endpoint = ResolveEndpoint(turn.Locale);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _options.AzureSpeechSubscriptionKey);
        request.Content = new ByteArrayContent(await File.ReadAllBytesAsync(wavPath, cancellationToken));
        request.Content.Headers.TryAddWithoutValidation(
            "Content-Type",
            "audio/wav; codecs=\"audio/pcm\"; samplerate=16000");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Azure STT request failed turnId={TurnId} statusCode={StatusCode} endpoint={Endpoint}",
                turn.TurnId,
                (int)response.StatusCode,
                endpoint);
            throw new InvalidOperationException(
                $"Azure speech STT request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        if (string.IsNullOrWhiteSpace(rawResponse))
            return new AzureSpeechResult(string.Empty, "empty-response", rawResponse);

        using var document = JsonDocument.Parse(rawResponse);
        var root = document.RootElement;

        var status = root.TryGetProperty("RecognitionStatus", out var statusElement) &&
                     statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString() ?? string.Empty
            : string.Empty;
        var transcript = root.TryGetProperty("DisplayText", out var displayText) &&
                         displayText.ValueKind == JsonValueKind.String
            ? displayText.GetString() ?? string.Empty
            : string.Empty;

        if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(status, "NoMatch", StringComparison.OrdinalIgnoreCase))
            logger.LogDebug("Azure STT returned non-success recognition status {Status} for turnId={TurnId}", status,
                turn.TurnId);

        return new AzureSpeechResult(transcript, status, rawResponse);
    }

    private string ResolveEndpoint(string? locale)
    {
        if (!string.IsNullOrWhiteSpace(_options.AzureSpeechEndpoint))
            return _options.AzureSpeechEndpoint!;

        var region = _options.AzureSpeechRegion?.Trim();
        var language = string.IsNullOrWhiteSpace(locale) ? _options.WhisperLanguage : locale!;
        return $"https://{region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language={Uri.EscapeDataString(language)}";
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
        string azureRawResponse,
        string azureStatus,
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
                ["wavPath"] = wavPath,
                ["ffmpegAudioFilter"] = FfmpegAudioPreprocessFilter,
                ["ffmpegStdOut"] = ffmpegResult.StdOut,
                ["ffmpegStdErr"] = ffmpegResult.StdErr,
                ["azureSpeechRegion"] = _options.AzureSpeechRegion,
                ["azureSpeechEndpoint"] = ResolveEndpoint(turn.Locale),
                ["azureSpeechStatus"] = azureStatus,
                ["azureSpeechResponse"] = azureRawResponse
            }
        };
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

    private sealed record AzureSpeechResult(string Transcript, string Status, string RawResponse);
}
