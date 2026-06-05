using System.Text;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Api.Hosting;

internal static class ApiRequestEnvelopeFactory
{
    internal static async Task<ProtocolEnvelope> CreateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Request.EnableBuffering();

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, false, leaveOpen: true);
        var bodyText = await reader.ReadToEndAsync(cancellationToken);
        context.Request.Body.Position = 0;

        var target = context.Request.Headers["X-Amz-Target"].ToString();
        var targetParts = target.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);

        return new ProtocolEnvelope
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Transport = "http",
            Method = context.Request.Method,
            HostName = context.Request.Host.Host,
            Path = context.Request.Path.Value ?? "/",
            ServicePrefix = targetParts.Length > 0 ? targetParts[0] : null,
            Operation = targetParts.Length > 1 ? targetParts[1] : null,
            DeviceId = context.Request.Headers["X-Jibo-RobotId"].ToString(),
            CorrelationId = context.TraceIdentifier,
            FirmwareVersion = context.Request.Headers["X-OpenJibo-Firmware"].ToString(),
            ApplicationVersion = context.Request.Headers["X-OpenJibo-AppVersion"].ToString(),
            BodyText = bodyText,
            Headers = context.Request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(),
                StringComparer.OrdinalIgnoreCase)
        };
    }
}