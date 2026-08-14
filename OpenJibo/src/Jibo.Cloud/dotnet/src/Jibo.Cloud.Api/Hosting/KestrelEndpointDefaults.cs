namespace Jibo.Cloud.Api.Hosting;

/// <summary>
/// Builds the in-memory Kestrel endpoint/certificate defaults that
/// <c>Program.ConfigureDefaultKestrelEndpoints</c> merges into configuration.
/// Certificate keys are treated as an all-or-nothing group so a caller-supplied
/// PFX (Path + Password) is never polluted with a lone KeyPath pointing at a PEM.
/// </summary>
internal static class KestrelEndpointDefaults
{
    public const string Http24605UrlKey = "Kestrel:Endpoints:Http24605:Url";
    public const string Http8765UrlKey = "Kestrel:Endpoints:Http8765:Url";
    public const string HttpsUrlKey = "Kestrel:Endpoints:Https:Url";
    public const string CertificatePathKey = "Kestrel:Certificates:Default:Path";
    public const string CertificateKeyPathKey = "Kestrel:Certificates:Default:KeyPath";
    public const string CertificateSubjectKey = "Kestrel:Certificates:Default:Subject";

    public const string DefaultHttp24605Url = "http://0.0.0.0:24605";
    public const string DefaultHttp8765Url = "http://0.0.0.0:8765";
    public const string DefaultHttpsUrl = "https://0.0.0.0:443";

    public const string Port443UnavailableWarning =
        "openjibo: WARNING — TLS material is available but :443 is not bindable " +
        "(already in use by another process, or this process lacks permission " +
        "to bind a privileged port — grant CAP_NET_BIND_SERVICE, e.g. via the " +
        "systemd unit's AmbientCapabilities=CAP_NET_BIND_SERVICE, or run as a " +
        "user that already has it). Skipping the :443 endpoint so the rest of " +
        "the server (24605/8765) still starts — LoopUpdated push over the " +
        "native NotificationSubsystem will NOT work until :443 is free.";

    public static Dictionary<string, string?> Build(
        IConfiguration configuration,
        string certPath,
        string keyPath,
        bool pemCertUsable,
        Func<int, bool> canBindPort,
        out string? warning)
    {
        warning = null;
        var defaults = new Dictionary<string, string?>();

        void DefaultIfUnset(string key, string value)
        {
            if (string.IsNullOrEmpty(configuration[key])) defaults[key] = value;
        }

        DefaultIfUnset(Http24605UrlKey, DefaultHttp24605Url);
        DefaultIfUnset(Http8765UrlKey, DefaultHttp8765Url);

        var callerConfiguredCert = HasCallerConfiguredCertificate(configuration);
        var hasTlsSource = callerConfiguredCert || pemCertUsable;

        if (!hasTlsSource)
            return defaults;

        if (!canBindPort(443))
        {
            warning = Port443UnavailableWarning;
            return defaults;
        }

        DefaultIfUnset(HttpsUrlKey, DefaultHttpsUrl);

        // Never patch individual certificate keys on top of a caller-supplied
        // cert (e.g. run.sh's PFX + Password). Injecting KeyPath alone forces
        // Kestrel into its PEM branch and crashes startup.
        if (!callerConfiguredCert && pemCertUsable)
        {
            DefaultIfUnset(CertificatePathKey, certPath);
            DefaultIfUnset(CertificateKeyPathKey, keyPath);
        }

        return defaults;
    }

    public static bool HasCallerConfiguredCertificate(IConfiguration configuration)
    {
        return !string.IsNullOrEmpty(configuration[CertificatePathKey]) ||
               !string.IsNullOrEmpty(configuration[CertificateKeyPathKey]) ||
               !string.IsNullOrEmpty(configuration[CertificateSubjectKey]);
    }
}
