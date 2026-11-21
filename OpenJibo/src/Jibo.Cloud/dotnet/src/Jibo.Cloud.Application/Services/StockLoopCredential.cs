using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Microsoft.Extensions.Configuration;

namespace Jibo.Cloud.Application.Services;

/// <summary>Authenticates the stock HTTP requests for one explicitly enrolled device.</summary>
internal sealed class StockLoopCredential
{
    private readonly AwsSigV4RequestVerifier _verifier;
    private readonly AwsSigV4Credential _credential;
    private readonly AwsSigV4OperationPolicy _loop;

    private StockLoopCredential(ICloudStateStore store, string deviceId, string apiHost,
        string region, AwsSigV4Credential credential)
    {
        DeviceId = deviceId;
        _verifier = new AwsSigV4RequestVerifier(store);
        _credential = credential;
        _loop = new AwsSigV4OperationPolicy("Loop.ListLoops", region, "Loop",
            ["Loop_20160324.ListLoops", "Loop_20160324.List", "Loop_20160715.ListLoops", "Loop_20160715.List"],
            [apiHost]);
    }

    public string DeviceId { get; }

    public static StockLoopCredential? Read(ICloudStateStore store, IConfiguration? configuration)
    {
        var json = configuration?["OpenJibo:Security:StockLoopCredential"];
        if (string.IsNullOrWhiteSpace(json)) return null;

        StockCredentialOptions? options;
        try
        {
            options = JsonSerializer.Deserialize<StockCredentialOptions>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The stock Loop credential configuration is invalid.");
        }

        var host = configuration?["OpenJibo:CanonicalApiHostname"]?.Trim();
        if (options is null || string.IsNullOrWhiteSpace(options.DeviceId) ||
            string.IsNullOrWhiteSpace(options.Region) || string.IsNullOrWhiteSpace(options.AccessKeyId) ||
            string.IsNullOrWhiteSpace(options.SecretAccessKey) || string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("The stock Loop credential configuration is incomplete.");

        return new StockLoopCredential(store, options.DeviceId, host, options.Region,
            new AwsSigV4Credential(options.AccessKeyId, options.SecretAccessKey));
    }

    public bool IsPresented(ProtocolEnvelope envelope) =>
        envelope.Headers.TryGetValue("Authorization", out var value) &&
        value?.StartsWith($"AWS4-HMAC-SHA256 Credential={_credential.AccessKeyId}/", StringComparison.Ordinal) == true;

    public bool Authenticate(ProtocolEnvelope envelope) =>
        IsPresented(envelope) && _verifier.Verify(envelope, _loop, _credential).OperationAuthenticated;

    private sealed record StockCredentialOptions(
        string DeviceId, string Region, string AccessKeyId, string SecretAccessKey)
    {
        public override string ToString() => nameof(StockCredentialOptions);
    }
}
