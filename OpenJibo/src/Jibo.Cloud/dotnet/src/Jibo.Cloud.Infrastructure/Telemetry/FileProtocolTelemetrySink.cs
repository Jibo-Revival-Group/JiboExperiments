using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jibo.Cloud.Infrastructure.Telemetry;

public sealed class FileProtocolTelemetrySink(
    ILogger<FileProtocolTelemetrySink> logger,
    IOptions<ProtocolTelemetryOptions> options) : IProtocolTelemetrySink
{
    private const string Redacted = "[REDACTED]";
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "X-Amz-Security-Token",
        "Location",
        "Content-Location",
        "X-OpenJibo-Release-Smoke-Secret"
    };
    private static readonly HashSet<string> SensitiveBodyProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "token",
        "accessToken",
        "access_token",
        "refreshToken",
        "refresh_token",
        "idToken",
        "id_token",
        "secret",
        "clientSecret",
        "client_secret",
        "secretAccessKey",
        "password",
        "currentPassword",
        "newPassword"
    };
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task RecordAsync(ProtocolEnvelope envelope, ProtocolDispatchResult result,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return;

        try
        {
            var directory = CapturePathResolver.Resolve(
                options.Value.DirectoryPath,
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory);
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMdd}.events.ndjson");

            var payload = new
            {
                capturedUtc = DateTimeOffset.UtcNow,
                request = new
                {
                    envelope.RequestId,
                    envelope.Transport,
                    envelope.Method,
                    envelope.HostName,
                    envelope.Path,
                    envelope.ServicePrefix,
                    envelope.Operation,
                    envelope.DeviceId,
                    envelope.CorrelationId,
                    envelope.FirmwareVersion,
                    envelope.ApplicationVersion,
                    Headers = SanitizeHeaders(envelope.Headers),
                    BodyText = SanitizeBody(envelope.BodyText)
                },
                response = new
                {
                    result.StatusCode,
                    result.ContentType,
                    Headers = SanitizeHeaders(result.Headers),
                    BodyText = SanitizeBody(result.BodyText)
                }
            };

            var line = JsonSerializer.Serialize(payload) + Environment.NewLine;

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await File.AppendAllTextAsync(filePath, line, cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }

            await CaptureIndexWriter.AppendAsync(
                directory,
                "http",
                "protocol_record",
                new Dictionary<string, object?>
                {
                    ["method"] = envelope.Method,
                    ["host"] = envelope.HostName,
                    ["path"] = envelope.Path,
                    ["servicePrefix"] = envelope.ServicePrefix,
                    ["operation"] = envelope.Operation,
                    ["statusCode"] = result.StatusCode,
                    ["contentType"] = result.ContentType,
                    ["requestId"] = envelope.RequestId,
                    ["eventFilePath"] = filePath
                },
                cancellationToken);

            logger.LogDebug(
                "HTTP telemetry {Method} {Host}{Path} target={Target} status={StatusCode}",
                envelope.Method,
                envelope.HostName,
                envelope.Path,
                $"{envelope.ServicePrefix}.{envelope.Operation}".Trim('.'),
                result.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Skipping HTTP telemetry write for {Method} {Host}{Path} because capture storage was unavailable.",
                envelope.Method,
                envelope.HostName,
                envelope.Path);
        }
    }

    private static IReadOnlyDictionary<string, string> SanitizeHeaders(IDictionary<string, string> headers) =>
        headers.ToDictionary(
            pair => pair.Key,
            pair => IsSensitiveName(pair.Key, SensitiveHeaders) ? Redacted : pair.Value,
            StringComparer.OrdinalIgnoreCase);

    private static string SanitizeBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return body;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
                return "[JSON SCALAR BODY OMITTED]";
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
                WriteSanitizedJson(writer, document.RootElement);
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (JsonException)
        {
            // A generic telemetry sink cannot prove that form, text, multipart, or malformed
            // JSON bodies are credential-free. Fail closed instead of retaining replayable data.
            return "[NON-JSON BODY OMITTED]";
        }
    }

    private static void WriteSanitizedJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSensitiveName(property.Name, SensitiveBodyProperties)) writer.WriteStringValue(Redacted);
                    else WriteSanitizedJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteSanitizedJson(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitiveName(string name, IReadOnlySet<string> exactNames)
    {
        if (exactNames.Contains(name)) return true;
        var normalized = name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        return normalized.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("privatekey", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("pairinghandle", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("challenge", StringComparison.OrdinalIgnoreCase);
    }
}
