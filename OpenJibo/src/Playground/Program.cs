using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Playground;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

var minimumLevel = ParseLogEventLevel(Environment.GetEnvironmentVariable("OPENJIBO_PLAYGROUND_LOG_LEVEL") ?? "Debug");
var logDirectory = ResolvePath("captures/logs");
Directory.CreateDirectory(logDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(minimumLevel)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        theme: AnsiConsoleTheme.Code,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(logDirectory, "playground-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,
        restrictedToMinimumLevel: minimumLevel,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Enter Jibo IP and press Enter.");
    var jiboIp = (Console.ReadLine() ?? "").Trim();

    if (string.IsNullOrWhiteSpace(jiboIp))
    {
        Log.Warning("No IP entered.");
        return;
    }

    var baseHttp = $"http://{jiboIp}:8088";
    var ttsHttp = $"http://{jiboIp}:8089";
    var wsUri = new Uri($"ws://{jiboIp}:8088/simple_port");

    using var http = new HttpClient();
    using var cts = new CancellationTokenSource();

    Log.Information("Connecting to Jibo at {JiboIp}...", jiboIp);
    Log.Information("Press Ctrl+C to quit.");

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        try
        {
            // ReSharper disable once AccessToDisposedClosure
            cts.Cancel();
        }
        catch
        {
            // ignore
        }
    };

    while (!cts.IsCancellationRequested)
    {
        var taskId = $"DEBUG:demo-{Guid.NewGuid():N}";
        var requestId = $"stt_start_{Guid.NewGuid():N}";

        try
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(wsUri, cts.Token);
            Log.Information("WebSocket connected.");

            var utteranceTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            await Task.Run(async () =>
            {
                var buffer = new byte[8192];

                while (ws.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    using var ms = new MemoryStream();

                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Log.Information("WebSocket closed by server.");
                            return;
                        }

                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var json = Encoding.UTF8.GetString(ms.ToArray());

                    AsrEvent? evt;
                    try
                    {
                        evt = JsonSerializer.Deserialize<AsrEvent>(json);
                    }
                    catch
                    {
                        Log.Debug("Non-JSON WS message: {Json}", json);
                        continue;
                    }

                    if (evt == null)
                        continue;

                    if (evt.TaskId != taskId)
                        continue;

                    Log.Information("[{EventType}] {Json}", evt.EventType, json);

                    if (evt.EventType != "speech_to_text_final") continue;
                    var best = PickBestUtterance(evt.Utterances);
                    if (string.IsNullOrWhiteSpace(best)) continue;
                    utteranceTcs.TrySetResult(best);
                    return;
                }
            }, cts.Token);

            var startPayload = new
            {
                command = "start",
                task_id = taskId,
                audio_source_id = "alsa1",
                hotphrase = "none",
                speech_to_text = true,
                request_id = requestId
            };

            var startResp = await http.PostAsJsonAsync($"{baseHttp}/asr_simple_interface", startPayload, cts.Token);
            var startBody = await startResp.Content.ReadAsStringAsync(cts.Token);

            Log.Information("ASR start: {StatusCode} {ReasonPhrase}", (int)startResp.StatusCode,
                startResp.ReasonPhrase);
            if (!string.IsNullOrWhiteSpace(startBody))
                Log.Information("{Body}", startBody);

            if (!startResp.IsSuccessStatusCode)
                continue;

            Log.Information("Speak now...");

            var completed = await Task.WhenAny(utteranceTcs.Task, Task.Delay(TimeSpan.FromSeconds(15), cts.Token));

            if (completed != utteranceTcs.Task)
            {
                Log.Warning("Timed out waiting for speech_to_text_final.");
            }
            else
            {
                var heard = utteranceTcs.Task.Result;
                Log.Information("Heard: {Heard}", heard);

                var reply = BuildReply(heard);
                Log.Information("Reply: {Reply}", reply);

                var ttsPayload = new
                {
                    prompt = reply,
                    locale = "en-us",
                    voice = "griffin",
                    mode = "text",
                    outputMode = "stream"
                };

                var ttsResp = await http.PostAsJsonAsync($"{ttsHttp}/tts_speak", ttsPayload, cts.Token);
                var ttsBody = await ttsResp.Content.ReadAsStringAsync(cts.Token);

                Log.Information("TTS: {StatusCode} {ReasonPhrase}", (int)ttsResp.StatusCode, ttsResp.ReasonPhrase);
                if (!string.IsNullOrWhiteSpace(ttsBody))
                    Log.Information("{Body}", ttsBody);
            }

            var stopPayload = new
            {
                command = "stop",
                task_id = taskId,
                request_id = $"stt_stop_{Guid.NewGuid():N}"
            };

            var stopResp = await http.PostAsJsonAsync($"{baseHttp}/asr_simple_interface", stopPayload, cts.Token);
            _ = await stopResp.Content.ReadAsStringAsync(cts.Token);

            Log.Information("STT task stopped.");
            Log.Information("Press Enter to run another round, or Ctrl+C to quit.");
            Console.ReadLine();
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while running the playground round.");
            Log.Information("Retrying in 2 seconds...");
            await Task.Delay(2000, cts.Token);
        }
    }
}
finally
{
    Log.CloseAndFlush();
}

return;

static string PickBestUtterance(List<AsrUtterance>? utterances)
{
    if (utterances == null || utterances.Count == 0)
        return "";

    var cleaned = utterances
        .Select(u => NormalizeUtterance(u.Utterance))
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(s => s.Length)
        .ToList();

    return cleaned.FirstOrDefault() ?? "";
}

static string NormalizeUtterance(string? text)
{
    if (string.IsNullOrWhiteSpace(text))
        return "";

    var s = text.Trim();

    if (s.Length >= 2 && char.ToLowerInvariant(s[0]) == char.ToLowerInvariant(s[1]))
        s = s[1..];

    return s;
}

static string BuildReply(string heard)
{
    var text = heard.Trim().ToLowerInvariant();

    if (text.Contains("time"))
        return $"It is {DateTime.Now:hh:mm tt}.";

    if (text.Contains("hello") || text.Contains("hi"))
        return "Hello! I heard you loud and clear.";

    return text.Contains("your name") ? "I am Jibo, running with a local demo bridge." : $"You said: {heard}";
}

static string ResolvePath(string configuredPath)
{
    if (Path.IsPathRooted(configuredPath)) return Path.GetFullPath(configuredPath);

    var repoRoot = FindOpenJiboRepoRoot(Directory.GetCurrentDirectory()) ??
                   FindOpenJiboRepoRoot(AppContext.BaseDirectory) ??
                   Directory.GetCurrentDirectory();

    return Path.GetFullPath(configuredPath, repoRoot);
}

static string? FindOpenJiboRepoRoot(string? startPath)
{
    if (string.IsNullOrWhiteSpace(startPath)) return null;

    var directory = new DirectoryInfo(Path.GetFullPath(startPath));
    if (directory is { Exists: false, Parent: not null }) directory = directory.Parent;

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "OpenJibo.slnx"))) return directory.FullName;

        directory = directory.Parent;
    }

    return null;
}

static LogEventLevel ParseLogEventLevel(string? value)
{
    return Enum.TryParse<LogEventLevel>(value, true, out var level)
        ? level
        : LogEventLevel.Debug;
}