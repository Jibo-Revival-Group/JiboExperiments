using Jibo.Cloud.Api.Hosting;
using Microsoft.Extensions.Configuration;

namespace Jibo.Cloud.Tests.Api;

public sealed class KestrelEndpointDefaultsTests
{
    [Fact]
    public void Build_WithCallerPfx_DoesNotInjectKeyPath_AndStillAddsHttps443()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Kestrel:Certificates:Default:Path"] = "/tmp/openjibo-dev-cert.pfx",
            ["Kestrel:Certificates:Default:Password"] = "secret"
        });

        var defaults = KestrelEndpointDefaults.Build(
            configuration,
            certPath: "/unused/cert.pem",
            keyPath: "/unused/key.pem",
            pemCertUsable: true,
            canBindPort: static _ => true,
            out var warning);

        Assert.Null(warning);
        Assert.False(defaults.ContainsKey(KestrelEndpointDefaults.CertificatePathKey));
        Assert.False(defaults.ContainsKey(KestrelEndpointDefaults.CertificateKeyPathKey));
        Assert.Equal(KestrelEndpointDefaults.DefaultHttpsUrl, defaults[KestrelEndpointDefaults.HttpsUrlKey]);
        Assert.Equal(KestrelEndpointDefaults.DefaultHttp24605Url, defaults[KestrelEndpointDefaults.Http24605UrlKey]);
        Assert.Equal(KestrelEndpointDefaults.DefaultHttp8765Url, defaults[KestrelEndpointDefaults.Http8765UrlKey]);
    }

    [Fact]
    public void Build_WithNoCertificateConfigured_AndUsablePem_WritesPathAndKeyPathTogether()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());
        const string certPath = "/repo/src/Jibo.Cloud/node/cert.pem";
        const string keyPath = "/repo/src/Jibo.Cloud/node/key.pem";

        var defaults = KestrelEndpointDefaults.Build(
            configuration,
            certPath,
            keyPath,
            pemCertUsable: true,
            canBindPort: static _ => true,
            out var warning);

        Assert.Null(warning);
        Assert.Equal(certPath, defaults[KestrelEndpointDefaults.CertificatePathKey]);
        Assert.Equal(keyPath, defaults[KestrelEndpointDefaults.CertificateKeyPathKey]);
        Assert.Equal(KestrelEndpointDefaults.DefaultHttpsUrl, defaults[KestrelEndpointDefaults.HttpsUrlKey]);
    }

    [Fact]
    public void Build_WithUnusablePem_AndNoConfiguredCert_SkipsHttpsAndCertificateKeys()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var defaults = KestrelEndpointDefaults.Build(
            configuration,
            certPath: "/missing/cert.pem",
            keyPath: "/missing/key.pem",
            pemCertUsable: false,
            canBindPort: static _ => true,
            out var warning);

        Assert.Null(warning);
        Assert.False(defaults.ContainsKey(KestrelEndpointDefaults.HttpsUrlKey));
        Assert.False(defaults.ContainsKey(KestrelEndpointDefaults.CertificatePathKey));
        Assert.False(defaults.ContainsKey(KestrelEndpointDefaults.CertificateKeyPathKey));
        Assert.Equal(KestrelEndpointDefaults.DefaultHttp24605Url, defaults[KestrelEndpointDefaults.Http24605UrlKey]);
        Assert.Equal(KestrelEndpointDefaults.DefaultHttp8765Url, defaults[KestrelEndpointDefaults.Http8765UrlKey]);
    }

    [Fact]
    public void Build_WhenPort443Unavailable_SkipsHttps_ReturnsWarning_KeepsHttpEndpoints()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var defaults = KestrelEndpointDefaults.Build(
            configuration,
            certPath: "/repo/cert.pem",
            keyPath: "/repo/key.pem",
            pemCertUsable: true,
            canBindPort: static port => port != 443,
            out var warning);

        Assert.Equal(KestrelEndpointDefaults.Port443UnavailableWarning, warning);
        Assert.False(defaults.ContainsKey(KestrelEndpointDefaults.HttpsUrlKey));
        Assert.False(defaults.ContainsKey(KestrelEndpointDefaults.CertificatePathKey));
        Assert.False(defaults.ContainsKey(KestrelEndpointDefaults.CertificateKeyPathKey));
        Assert.Equal(KestrelEndpointDefaults.DefaultHttp24605Url, defaults[KestrelEndpointDefaults.Http24605UrlKey]);
        Assert.Equal(KestrelEndpointDefaults.DefaultHttp8765Url, defaults[KestrelEndpointDefaults.Http8765UrlKey]);
    }

    [Fact]
    public void Build_WithCallerPfx_WhenPort443Unavailable_StillSkipsHttpsWithoutTouchingCertKeys()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Kestrel:Certificates:Default:Path"] = "/tmp/openjibo-dev-cert.pfx",
            ["Kestrel:Certificates:Default:Password"] = "secret"
        });

        var defaults = KestrelEndpointDefaults.Build(
            configuration,
            certPath: "/unused/cert.pem",
            keyPath: "/unused/key.pem",
            pemCertUsable: false,
            canBindPort: static _ => false,
            out var warning);

        Assert.Equal(KestrelEndpointDefaults.Port443UnavailableWarning, warning);
        Assert.False(defaults.ContainsKey(KestrelEndpointDefaults.HttpsUrlKey));
        Assert.False(defaults.ContainsKey(KestrelEndpointDefaults.CertificateKeyPathKey));
    }

    [Fact]
    public void HasCallerConfiguredCertificate_DetectsPathKeyPathOrSubject()
    {
        Assert.True(KestrelEndpointDefaults.HasCallerConfiguredCertificate(
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["Kestrel:Certificates:Default:Path"] = "/tmp/x.pfx"
            })));
        Assert.True(KestrelEndpointDefaults.HasCallerConfiguredCertificate(
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["Kestrel:Certificates:Default:KeyPath"] = "/tmp/key.pem"
            })));
        Assert.True(KestrelEndpointDefaults.HasCallerConfiguredCertificate(
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["Kestrel:Certificates:Default:Subject"] = "CN=openjibo"
            })));
        Assert.False(KestrelEndpointDefaults.HasCallerConfiguredCertificate(
            BuildConfiguration(new Dictionary<string, string?>())));
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
