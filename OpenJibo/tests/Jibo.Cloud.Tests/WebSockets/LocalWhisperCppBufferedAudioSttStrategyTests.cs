using Jibo.Cloud.Infrastructure.Audio;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.WebSockets;

public sealed class LocalWhisperCppBufferedAudioSttStrategyTests
{
    [Fact]
    public void CanHandle_ReturnsFalse_WhenLocalWhisperIsDisabled()
    {
        var strategy = new LocalWhisperCppBufferedAudioSttStrategy(
            new BufferedAudioSttOptions
            {
                EnableLocalWhisperCpp = false,
                FfmpegPath = "ffmpeg",
                WhisperCliPath = "whisper-cli",
                WhisperModelPath = "model.bin"
            },
            new FakeExternalProcessRunner());

        var turn = new TurnContext
        {
            Attributes = new Dictionary<string, object?>
            {
                ["bufferedAudioFrames"] = new[] { BuildMinimalOggPage() }
            }
        };

        Assert.False(strategy.CanHandle(turn));
    }

    [Fact]
    public void CanHandle_ReturnsFalse_WhenConfiguredAbsoluteWhisperPathIsMissing()
    {
        var strategy = new LocalWhisperCppBufferedAudioSttStrategy(
            new BufferedAudioSttOptions
            {
                EnableLocalWhisperCpp = true,
                FfmpegPath = "/usr/bin/ffmpeg",
                WhisperCliPath = "/path/that/does/not/exist/whisper-cli",
                WhisperModelPath = "/path/that/does/not/exist/model.bin"
            },
            new FakeExternalProcessRunner());

        var turn = new TurnContext
        {
            Attributes = new Dictionary<string, object?>
            {
                ["bufferedAudioFrames"] = new[] { BuildMinimalOggPage() }
            }
        };

        Assert.False(strategy.CanHandle(turn));
    }

    [Fact]
    public void Resolve_UsesEnvironmentOverrides_WhenConfiguredPathsAreEmpty()
    {
        var resolved = BufferedAudioSttPathResolver.Resolve(
            new BufferedAudioSttOptions
            {
                EnableLocalWhisperCpp = true,
                FfmpegPath = "",
                WhisperCliPath = "",
                WhisperModelPath = ""
            },
            name => name switch
            {
                "OPENJIBO_STT_FFMPEG_PATH" => "/custom/bin/ffmpeg",
                "OPENJIBO_STT_WHISPER_CLI_PATH" => "/custom/bin/whisper-cli",
                "OPENJIBO_STT_WHISPER_MODEL_PATH" => "/custom/models/ggml-base.en.bin",
                _ => null
            },
            path => path.StartsWith("/custom/", StringComparison.Ordinal),
            homeDirectory: null,
            isMacOS: true,
            isLinux: false);

        Assert.Equal("/custom/bin/ffmpeg", resolved.FfmpegPath);
        Assert.Equal("/custom/bin/whisper-cli", resolved.WhisperCliPath);
        Assert.Equal("/custom/models/ggml-base.en.bin", resolved.WhisperModelPath);
    }

    [Fact]
    public void Resolve_UsesMacDiscovery_WhenLegacyLinuxDefaultsAreConfigured()
    {
        var existingPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "/opt/homebrew/bin/ffmpeg",
            "/opt/homebrew/bin/whisper-cli",
            "/Users/test/whisper.cpp/models/ggml-base.en.bin"
        };

        var resolved = BufferedAudioSttPathResolver.Resolve(
            new BufferedAudioSttOptions
            {
                EnableLocalWhisperCpp = true,
                FfmpegPath = "/usr/bin/ffmpeg",
                WhisperCliPath = "/usr/bin/whisper.cpp/build/bin/whisper-cli",
                WhisperModelPath = "/usr/bin/whisper.cpp/models/ggml-base.en.bin"
            },
            _ => null,
            existingPaths.Contains,
            homeDirectory: "/Users/test",
            isMacOS: true,
            isLinux: false);

        Assert.Equal("/opt/homebrew/bin/ffmpeg", resolved.FfmpegPath);
        Assert.Equal("/opt/homebrew/bin/whisper-cli", resolved.WhisperCliPath);
        Assert.Equal("/Users/test/whisper.cpp/models/ggml-base.en.bin", resolved.WhisperModelPath);
    }

    [Fact]
    public void Resolve_KeepsExplicitRelativePaths()
    {
        var resolved = BufferedAudioSttPathResolver.Resolve(
            new BufferedAudioSttOptions
            {
                EnableLocalWhisperCpp = true,
                FfmpegPath = "ffmpeg",
                WhisperCliPath = "whisper-cli",
                WhisperModelPath = "models/ggml-base.en.bin"
            },
            name => name switch
            {
                "OPENJIBO_STT_FFMPEG_PATH" => "/custom/bin/ffmpeg",
                "OPENJIBO_STT_WHISPER_CLI_PATH" => "/custom/bin/whisper-cli",
                "OPENJIBO_STT_WHISPER_MODEL_PATH" => "/custom/models/ggml-base.en.bin",
                _ => null
            },
            _ => true,
            homeDirectory: "/Users/test",
            isMacOS: true,
            isLinux: false);

        Assert.Equal("ffmpeg", resolved.FfmpegPath);
        Assert.Equal("whisper-cli", resolved.WhisperCliPath);
        Assert.Equal("models/ggml-base.en.bin", resolved.WhisperModelPath);
    }

    [Fact]
    public void CanHandle_ReturnsFalse_WhenBufferedAudioHasNoOpusIdentificationHeader()
    {
        var strategy = new LocalWhisperCppBufferedAudioSttStrategy(
            new BufferedAudioSttOptions
            {
                EnableLocalWhisperCpp = true,
                FfmpegPath = "ffmpeg",
                WhisperCliPath = "whisper-cli",
                WhisperModelPath = "model.bin"
            },
            new FakeExternalProcessRunner());

        var turn = new TurnContext
        {
            Attributes = new Dictionary<string, object?>
            {
                ["bufferedAudioFrames"] = new[] { BuildMinimalOggPageWithoutOpusHead() }
            }
        };

        Assert.False(strategy.CanHandle(turn));
    }

    [Fact]
    public void CanHandle_ReturnsFalse_WhenBufferedAudioIsBelowNoiseFloor()
    {
        var strategy = new LocalWhisperCppBufferedAudioSttStrategy(
            new BufferedAudioSttOptions
            {
                EnableLocalWhisperCpp = true,
                FfmpegPath = "ffmpeg",
                WhisperCliPath = "whisper-cli",
                WhisperModelPath = "model.bin"
            },
            new FakeExternalProcessRunner());

        var turn = new TurnContext
        {
            Attributes = new Dictionary<string, object?>
            {
                ["bufferedAudioBytes"] = 47,
                ["bufferedAudioFrames"] = new[] { BuildMinimalOggPage() }
            }
        };

        Assert.False(strategy.CanHandle(turn));
    }

    [Theory]
    [InlineData("shared/yes_no")]
    [InlineData("word-of-the-day/surprise")]
    public void CanHandle_ReturnsTrue_WhenShortAnswerTurnsStayUnderTheStandardNoiseFloor(string listenRule)
    {
        var strategy = new LocalWhisperCppBufferedAudioSttStrategy(
            new BufferedAudioSttOptions
            {
                EnableLocalWhisperCpp = true,
                FfmpegPath = "ffmpeg",
                WhisperCliPath = "whisper-cli",
                WhisperModelPath = "model.bin"
            },
            new FakeExternalProcessRunner());

        var turn = new TurnContext
        {
            Attributes = new Dictionary<string, object?>
            {
                ["bufferedAudioBytes"] = 47,
                ["bufferedAudioFrames"] = new[] { BuildMinimalOggPage() },
                ["listenRules"] = new[] { listenRule }
            }
        };

        Assert.True(strategy.CanHandle(turn));
    }

    [Fact]
    public async Task TranscribeAsync_UsesFfmpegAndWhisperCpp_WhenConfigured()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"openjibo-stt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var runner = new FakeExternalProcessRunner();
            var strategy = new LocalWhisperCppBufferedAudioSttStrategy(
                new BufferedAudioSttOptions
                {
                    EnableLocalWhisperCpp = true,
                    FfmpegPath = "ffmpeg",
                    WhisperCliPath = "whisper-cli",
                    WhisperModelPath = "model.bin",
                    TempDirectory = tempDirectory
                },
                runner);

            var turn = new TurnContext
            {
                TurnId = "turn-local-stt",
                Locale = "en-US",
                Attributes = new Dictionary<string, object?>
                {
                    ["bufferedAudioBytes"] = 147,
                    ["bufferedAudioFrames"] = new[] { BuildMinimalOggPage() }
                }
            };

            var result = await strategy.TranscribeAsync(turn);

            Assert.Equal("tell me a joke", result.Text);
            Assert.Equal("local-whispercpp-buffered-audio", result.Provider);
            Assert.Equal(2, runner.Calls.Count);
            Assert.Equal("ffmpeg", runner.Calls[0].FileName);
            Assert.Equal("whisper-cli", runner.Calls[1].FileName);
            Assert.Equal(147, result.Metadata["bufferedAudioBytes"]);
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }
    }

    [Theory]
    [InlineData("shared/yes_no")]
    [InlineData("word-of-the-day/surprise")]
    public async Task TranscribeAsync_HandlesShortAnswerTurnsWithoutHittingTheStandardNoiseFloor(string listenRule)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"openjibo-stt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var runner = new FakeExternalProcessRunner("[00:00:00.000 --> 00:00:01.000] yes.");
            var strategy = new LocalWhisperCppBufferedAudioSttStrategy(
                new BufferedAudioSttOptions
                {
                    EnableLocalWhisperCpp = true,
                    FfmpegPath = "ffmpeg",
                    WhisperCliPath = "whisper-cli",
                    WhisperModelPath = "model.bin",
                    TempDirectory = tempDirectory
                },
                runner);

            var turn = new TurnContext
            {
                TurnId = listenRule == "shared/yes_no"
                    ? "turn-short-yes-no"
                    : "turn-short-word-of-the-day",
                Locale = "en-US",
                Attributes = new Dictionary<string, object?>
                {
                    ["bufferedAudioBytes"] = 47,
                    ["bufferedAudioFrames"] = new[] { BuildMinimalOggPage() },
                    ["listenRules"] = new[] { listenRule }
                }
            };

            var result = await strategy.TranscribeAsync(turn);

            Assert.Equal("yes", result.Text);
            Assert.Equal("local-whispercpp-buffered-audio", result.Provider);
            Assert.Equal(2, runner.Calls.Count);
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public async Task TranscribeAsync_NormalizesLoosePunctuationFromWhisperOutput()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"openjibo-stt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var runner = new FakeExternalProcessRunner("[00:00:00.000 --> 00:00:01.000] - Thank you. - Yes.");
            var strategy = new LocalWhisperCppBufferedAudioSttStrategy(
                new BufferedAudioSttOptions
                {
                    EnableLocalWhisperCpp = true,
                    FfmpegPath = "ffmpeg",
                    WhisperCliPath = "whisper-cli",
                    WhisperModelPath = "model.bin",
                    TempDirectory = tempDirectory
                },
                runner);

            var turn = new TurnContext
            {
                TurnId = "turn-local-stt-punctuation",
                Locale = "en-US",
                Attributes = new Dictionary<string, object?>
                {
                    ["bufferedAudioBytes"] = 147,
                    ["bufferedAudioFrames"] = new[] { BuildMinimalOggPage() }
                }
            };

            var result = await strategy.TranscribeAsync(turn);

            Assert.Equal("thank you yes", result.Text);
            Assert.Equal("local-whispercpp-buffered-audio", result.Provider);
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public async Task TranscribeAsync_Throws_WhenBufferedAudioIsBelowNoiseFloor()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"openjibo-stt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var runner = new FakeExternalProcessRunner();
            var strategy = new LocalWhisperCppBufferedAudioSttStrategy(
                new BufferedAudioSttOptions
                {
                    EnableLocalWhisperCpp = true,
                    FfmpegPath = "ffmpeg",
                    WhisperCliPath = "whisper-cli",
                    WhisperModelPath = "model.bin",
                    TempDirectory = tempDirectory
                },
                runner);

            var turn = new TurnContext
            {
                TurnId = "turn-local-stt-noise-floor",
                Locale = "en-US",
                Attributes = new Dictionary<string, object?>
                {
                    ["bufferedAudioBytes"] = 47,
                    ["bufferedAudioFrames"] = new[] { BuildMinimalOggPage() }
                }
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.TranscribeAsync(turn));
            Assert.Contains("too short or noisy", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(runner.Calls);
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }
    }

    private static byte[] BuildMinimalOggPage()
    {
        return
        [
            0x4F, 0x67, 0x67, 0x53,
            0x00,
            0x02,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x01,
            0x13,
            0x4F, 0x70, 0x75, 0x73, 0x48, 0x65, 0x61, 0x64, 0x01, 0x01, 0x38, 0x01, 0x80, 0xBB, 0x00, 0x00, 0x00, 0x00,
            0x00
        ];
    }

    private static byte[] BuildMinimalOggPageWithoutOpusHead()
    {
        var page = BuildMinimalOggPage();
        "NotAudio"u8.CopyTo(page.AsSpan(28, 8));
        return page;
    }

    private sealed class FakeExternalProcessRunner(
        string whisperStdOut = "[00:00:00.000 --> 00:00:01.000] tell me a joke")
        : IExternalProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public Task<ExternalProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((fileName, arguments));

            if (!string.Equals(fileName, "ffmpeg", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new ExternalProcessResult(0, whisperStdOut, string.Empty));

            var outputPath = arguments[^1];
            File.WriteAllBytes(outputPath, "RIFF"u8);
            return Task.FromResult(new ExternalProcessResult(0, string.Empty, string.Empty));
        }
    }
}
