using System.Net;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Infrastructure.Audio;
using Jibo.Cloud.Infrastructure.Platform;
using Jibo.Runtime.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.WebSockets;

public sealed class AzureSpeechBufferedAudioSttStrategyTests
{
    [Fact]
    public void CanHandle_ReturnsFalse_WhenAzureSpeechIsDisabled()
    {
        var strategy = new AzureSpeechBufferedAudioSttStrategy(
            new BufferedAudioSttOptions
            {
                EnableAzureSpeech = false,
                FfmpegPath = "ffmpeg",
                AzureSpeechRegion = "eastus",
                AzureSpeechSubscriptionKey = "secret"
            },
            new FakeExternalProcessRunner(),
            new HttpClient(new StubHttpMessageHandler()),
            NullLogger<AzureSpeechBufferedAudioSttStrategy>.Instance);

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
    public void CanHandle_ReturnsTrue_WhenAzureSpeechIsConfigured()
    {
        var strategy = new AzureSpeechBufferedAudioSttStrategy(
            new BufferedAudioSttOptions
            {
                EnableAzureSpeech = true,
                FfmpegPath = "ffmpeg",
                AzureSpeechRegion = "eastus",
                AzureSpeechSubscriptionKey = "secret"
            },
            new FakeExternalProcessRunner(),
            new HttpClient(new StubHttpMessageHandler()),
            NullLogger<AzureSpeechBufferedAudioSttStrategy>.Instance);

        var turn = new TurnContext
        {
            Attributes = new Dictionary<string, object?>
            {
                ["bufferedAudioBytes"] = 147,
                ["bufferedAudioFrames"] = new[] { BuildMinimalOggPage() }
            }
        };

        Assert.True(strategy.CanHandle(turn));
    }

    [Fact]
    public async Task TranscribeAsync_UsesAzureSpeechWhenConfigured()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"openjibo-stt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var runner = new FakeExternalProcessRunner();
            var handler = new StubHttpMessageHandler(
                request =>
                {
                    Assert.Equal(HttpMethod.Post, request.Method);
                    Assert.Contains("eastus.stt.speech.microsoft.com", request.RequestUri!.Host, StringComparison.OrdinalIgnoreCase);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new
                            {
                                RecognitionStatus = "Success",
                                DisplayText = "Tell me a joke."
                            }),
                            Encoding.UTF8,
                            "application/json")
                    };
                });

            var strategy = new AzureSpeechBufferedAudioSttStrategy(
                new BufferedAudioSttOptions
                {
                    EnableAzureSpeech = true,
                    FfmpegPath = "ffmpeg",
                    AzureSpeechRegion = "eastus",
                    AzureSpeechSubscriptionKey = "secret",
                    TempDirectory = tempDirectory
                },
                runner,
                new HttpClient(handler),
                NullLogger<AzureSpeechBufferedAudioSttStrategy>.Instance);

            var turn = new TurnContext
            {
                TurnId = "turn-azure-stt",
                Locale = "en-US",
                Attributes = new Dictionary<string, object?>
                {
                    ["bufferedAudioBytes"] = 147,
                    ["bufferedAudioFrames"] = new[] { BuildMinimalOggPage() }
                }
            };

            var result = await strategy.TranscribeAsync(turn);

            Assert.Equal("tell me a joke", result.Text);
            Assert.Equal("azure-speech-buffered-audio", result.Provider);
            Assert.Equal(1, handler.RequestCount);
            Assert.Single(runner.Calls);
            Assert.Equal("ffmpeg", runner.Calls[0].FileName);
            Assert.Equal("eastus", result.Metadata["azureSpeechRegion"]);
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public async Task TranscribeAsync_FallsBackToHint_WhenAzureReturnsSelfAudio()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"openjibo-stt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var handler = new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            RecognitionStatus = "Success",
                            DisplayText = "I heard you."
                        }),
                        Encoding.UTF8,
                        "application/json")
                });

            var strategy = new AzureSpeechBufferedAudioSttStrategy(
                new BufferedAudioSttOptions
                {
                    EnableAzureSpeech = true,
                    FfmpegPath = "ffmpeg",
                    AzureSpeechRegion = "eastus",
                    AzureSpeechSubscriptionKey = "secret",
                    TempDirectory = tempDirectory
                },
                new FakeExternalProcessRunner(),
                new HttpClient(handler),
                NullLogger<AzureSpeechBufferedAudioSttStrategy>.Instance);

            var turn = new TurnContext
            {
                TurnId = "turn-azure-stt-hint",
                Locale = "en-US",
                Attributes = new Dictionary<string, object?>
                {
                    ["bufferedAudioBytes"] = 147,
                    ["bufferedAudioFrames"] = new[] { BuildMinimalOggPage() },
                    ["audioTranscriptHint"] = "What's your cloud version?"
                }
            };

            var result = await strategy.TranscribeAsync(turn);

            Assert.Equal("what's your cloud version", result.Text);
            Assert.Equal("azure-speech-buffered-audio", result.Provider);
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
            File.WriteAllBytes(outputPath, Enumerable.Range(0, 4096).Select(index => (byte)(index % 256)).ToArray());
            return Task.FromResult(new ExternalProcessResult(0, string.Empty, string.Empty));
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler()
            : this(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            })
        {
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount += 1;
            return Task.FromResult(_responseFactory(request));
        }
    }
}
