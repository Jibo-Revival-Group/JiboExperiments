namespace Jibo.Cloud.Api.Hosting;

/// <summary>
/// Requires TLS for managed and hybrid WebSocket traffic while preserving the
/// explicitly isolated, single-robot HTTP compatibility deployment.
/// </summary>
internal sealed class WebSocketTransportPolicy(IConfiguration configuration)
{
    private const string DeploymentModeConfigurationKey = "OpenJibo:Deployment:Mode";
    private const string SecurityModeConfigurationKey = "OpenJibo:Security:Mode";
    private const string IsolatedSelfHostedMode = "self-hosted-isolated";
    private const string ManagedMode = "managed";

    internal WebSocketTransportPolicy(bool isolatedSelfHosted)
        : this(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DeploymentModeConfigurationKey] = isolatedSelfHosted ? IsolatedSelfHostedMode : ManagedMode
            })
            .Build())
    {
    }

    internal bool IsAllowed(HttpRequest request)
    {
        var deploymentMode = configuration[DeploymentModeConfigurationKey];
        if (string.Equals(deploymentMode, IsolatedSelfHostedMode, StringComparison.OrdinalIgnoreCase) &&
            !IsSecurityModeEnabled(deploymentMode))
            return true;

        if (request.IsHttps)
            return true;

        // Azure Container Apps terminates TLS before forwarding to Kestrel. Honor
        // its normalized single-value header only inside an identified managed
        // revision; arbitrary self-hosted clients cannot opt into this trust.
        if (!string.Equals(deploymentMode, ManagedMode, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(configuration["CONTAINER_APP_REVISION"]))
            return false;

        var forwardedValues = request.Headers["X-Forwarded-Proto"];
        return forwardedValues.Count == 1 &&
               string.Equals(forwardedValues[0], "https", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSecurityModeEnabled(string? deploymentMode)
    {
        var configuredValue = configuration[SecurityModeConfigurationKey];
        if (bool.TryParse(configuredValue, out var enabled))
            return enabled;

        // Preserve stock-robot HTTP compatibility only for explicitly isolated
        // self-hosting. Every other deployment mode remains fail-closed.
        return !string.Equals(deploymentMode, IsolatedSelfHostedMode, StringComparison.OrdinalIgnoreCase);
    }
}
