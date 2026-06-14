using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

public sealed class OobePortalService(ICloudStateStore stateStore, JiboCloudProtocolService? protocolService = null)
{
    private const string QrXorKey = "Wow, you cracked our secret code. Impressive. Maybe you should check out jibo.com/jobs.";
    private const int TokenExpirationHours = 1;

    // Use the protocol service's OOBE token state if available, otherwise fall back to local tracking
    private readonly ConcurrentDictionary<string, JiboCloudProtocolService.OobeTokenState>? _protocolOobeTokens = 
        protocolService?.OobeTokens;
    private static readonly ConcurrentDictionary<string, bool> _localSetupCompletion = new();

    public async Task<OobePortalResult> SignupAsync(string email, string password, string? firstName = null, string? lastName = null)
    {
        var existingUser = stateStore.GetUserByEmail(email);
        if (existingUser is not null)
            return OobePortalResult.CreateError("Email already exists");

        var user = stateStore.CreateUser(email, password, firstName, lastName);
        if (user is null)
            return OobePortalResult.CreateError("Failed to create user");

        var sessionToken = GenerateSessionToken(user.Id);
        return OobePortalResult.CreateSuccess(new { user = MapUser(user), token = sessionToken });
    }

    public async Task<OobePortalResult> LoginAsync(string email, string password)
    {
        var user = stateStore.AuthenticateUser(email, password);
        if (user is null)
            return OobePortalResult.CreateError("Invalid credentials");

        var sessionToken = GenerateSessionToken(user.Id);
        return OobePortalResult.CreateSuccess(new { user = MapUser(user), token = sessionToken });
    }

    public async Task<OobePortalResult> GetRobotsAsync(string userId)
    {
        var user = stateStore.GetUserById(userId);
        if (user is null)
            return OobePortalResult.CreateError("User not found");

        var loops = stateStore.GetLoops();
        var robots = loops.Select(loop => new
        {
            name = loop.Name,
            loopId = loop.LoopId,
            status = "active",
            lastSeen = loop.UpdatedUtc.ToUnixTimeMilliseconds()
        }).ToArray();

        return OobePortalResult.CreateSuccess(new { robots });
    }

    public async Task<OobePortalResult> PrepareRobotSetupAsync(string userId, string ssid, string password,
        string? staticIp = null, string? netmask = null, string? gateway = null,
        string? dns1 = null, string? dns2 = null)
    {
        var user = stateStore.GetUserById(userId);
        if (user is null)
            return OobePortalResult.CreateError("User not found");

        // Generate OOBE token
        var token = GenerateOobeToken();
        var expiresUtc = DateTimeOffset.UtcNow.AddHours(TokenExpirationHours);

        // Build QR payload
        var payload = BuildQrPayload(ssid, password, staticIp, netmask, gateway, dns1, dns2, token);
        var encryptedPayload = XorEncrypt(payload, QrXorKey);
        var chunks = ChunkPayload(encryptedPayload, 25);

        return OobePortalResult.CreateSuccess(new
        {
            token,
            expires = expiresUtc.ToUnixTimeMilliseconds(),
            qr = new
            {
                payload = encryptedPayload,
                codes = chunks
            }
        });
    }

    public async Task<OobePortalResult> GetRobotSetupStatusAsync(string token)
    {
        // Check if setup is complete using the shared token state from protocol service
        bool isComplete = false;

        if (_protocolOobeTokens is not null && _protocolOobeTokens.TryGetValue(token, out var tokenState))
        {
            isComplete = tokenState.Complete;
        }
        else
        {
            // Fall back to local tracking if protocol service not available
            isComplete = _localSetupCompletion.TryGetValue(token, out var complete) && complete;
        }

        return OobePortalResult.CreateSuccess(new
        {
            complete = isComplete,
            expires = DateTimeOffset.UtcNow.AddHours(TokenExpirationHours).ToUnixTimeMilliseconds()
        });
    }

    // Method to mark setup as complete (called by protocol service when robot completes setup)
    public void MarkSetupComplete(string token)
    {
        if (_protocolOobeTokens is not null && _protocolOobeTokens.TryGetValue(token, out var tokenState))
        {
            tokenState.Complete = true;
        }
        else
        {
            _localSetupCompletion[token] = true;
        }
    }

    public bool ValidateSessionToken(string token, out string? userId)
    {
        // Simple token validation - in production, use proper JWT
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2)
            {
                userId = null;
                return false;
            }

            userId = parts[0];
            var timestamp = long.Parse(parts[1]);
            var tokenAge = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp;

            // Token valid for 24 hours
            return tokenAge < 24 * 60 * 60 * 1000;
        }
        catch
        {
            userId = null;
            return false;
        }
    }

    private static string GenerateSessionToken(string userId)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"{userId}.{timestamp}";
    }

    private static string GenerateOobeToken()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return $"oobe-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string BuildQrPayload(string ssid, string password, 
        string? staticIp, string? netmask, string? gateway, string? dns1, string? dns2, string token)
    {
        var lines = new List<string> { ssid, password };

        if (!string.IsNullOrWhiteSpace(staticIp))
        {
            lines.Add(staticIp);
            lines.Add(netmask ?? "");
            lines.Add(gateway ?? "");
            lines.Add(dns1 ?? "");
            lines.Add(dns2 ?? "");
        }

        lines.Add(token);
        return string.Join('\n', lines);
    }

    private static string XorEncrypt(string plaintext, string key)
    {
        var result = new char[plaintext.Length];
        for (int i = 0; i < plaintext.Length; i++)
        {
            result[i] = (char)(plaintext[i] ^ key[i % key.Length]);
        }
        return new string(result);
    }

    private static string[] ChunkPayload(string payload, int chunkSize)
    {
        var chunks = new List<string>();
        for (int i = 0; i < payload.Length; i += chunkSize)
        {
            var chunk = payload.Substring(i, Math.Min(chunkSize, payload.Length - i));
            var chunkNumber = (i / chunkSize) + 1;
            var totalChunks = (int)Math.Ceiling((double)payload.Length / chunkSize);
            chunks.Add($"{chunkNumber}/{totalChunks}\n{chunk}");
        }
        return chunks.ToArray();
    }

    private static object MapUser(UserRecord user)
    {
        return new
        {
            id = user.Id,
            email = user.Email,
            firstName = user.FirstName,
            lastName = user.LastName,
            isActive = user.IsActive
        };
    }
}

public sealed class OobePortalResult
{
    public bool Success { get; init; }
    public object? Data { get; init; }
    public string? Error { get; init; }

    public static OobePortalResult CreateSuccess(object data) => new() { Success = true, Data = data };
    public static OobePortalResult CreateError(string error) => new() { Success = false, Error = error };
}
