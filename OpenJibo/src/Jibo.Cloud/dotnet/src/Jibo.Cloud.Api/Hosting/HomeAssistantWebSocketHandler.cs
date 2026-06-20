using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Api.Hosting;

internal sealed class HomeAssistantWebSocketHandler(
    HomeAssistantConnectionRegistry registry,
    ILogger<HomeAssistantWebSocketHandler> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal HomeAssistantWebSocketHandler(HomeAssistantConnectionRegistry registry)
        : this(registry, NullLogger<HomeAssistantWebSocketHandler>.Instance)
    {
    }

    internal async Task HandleAsync(HttpContext context)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        string? instanceId = null;

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var message = await ReceiveTextAsync(socket, context.RequestAborted);
                if (message is null) break;

                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;

                switch (type?.ToLowerInvariant())
                {
                    case "register":
                    {
                        instanceId = root.TryGetProperty("instanceId", out var instanceElement)
                            ? instanceElement.GetString()
                            : null;
                        if (string.IsNullOrWhiteSpace(instanceId))
                        {
                            await registry.SendErrorAsync(socket, "instanceId is required.", context.RequestAborted);
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(instanceId))
                            registry.RemoveConnection(instanceId);

                        var pending = registry.RegisterConnection(instanceId, socket);
                        await registry.SendVerificationCodeAsync(socket, pending, context.RequestAborted);
                        logger.LogInformation("Home Assistant instance {InstanceId} registered for pairing", instanceId);
                        break;
                    }
                    case "ping":
                        await registry.SendPongAsync(socket, context.RequestAborted);
                        break;
                    default:
                        await registry.SendErrorAsync(socket, "Unsupported message type.", context.RequestAborted);
                        break;
                }
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(instanceId))
                registry.RemoveConnection(instanceId);
        }
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (socket.State == WebSocketState.Open)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken);
                return null;
            }

            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return result.MessageType == WebSocketMessageType.Text
            ? Encoding.UTF8.GetString(ms.ToArray())
            : null;
    }
}
