using System.Security.Cryptography;
using System.Text.Json;
using NuGet.Packaging;

// Package bytes enter on stdin. No package files are installed or executed.
try
{
    if (args.Length != 1 || !args[0].StartsWith("sha512-", StringComparison.Ordinal))
        throw new InvalidDataException("Expected a scanner SHA-512 hash");
    var expected = Convert.FromBase64String(args[0][7..]);
    if (expected.Length != 64) throw new InvalidDataException("Invalid hash size");
    using var input = Console.OpenStandardInput();
    using var bytes = new MemoryStream();
    var buffer = new byte[81920];
    int count;
    while ((count = await input.ReadAsync(buffer)) != 0)
    {
        if (bytes.Length + count > 128 * 1024 * 1024)
            throw new InvalidDataException("Oversized archive");
        bytes.Write(buffer, 0, count);
    }
    bytes.Position = 0;
    using var reader = new PackageArchiveReader(bytes, leaveStreamOpen: true);
    var signed = await reader.IsSignedAsync(CancellationToken.None);
    if (signed)
    {
        var signature = await reader.GetPrimarySignatureAsync(CancellationToken.None)
            ?? throw new InvalidDataException("Missing signature metadata");
        await reader.ValidateIntegrityAsync(signature.SignatureContent, CancellationToken.None);
    }
    var actual = Convert.FromBase64String(reader.GetContentHash(CancellationToken.None));
    if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        throw new InvalidDataException("Content hash mismatch");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        nuget_content_hash_verified = true,
        content_hash = "sha512-" + Convert.ToBase64String(actual),
        signed_package_integrity_checked = signed,
        package_signature_trust_verified = false,
        verifier_library_version = typeof(PackageArchiveReader).Assembly.GetName().Version?.ToString()
    }));
    return 0;
}
catch
{
    Console.Error.WriteLine("Package content integrity/hash check failed; no approval recorded.");
    return 1;
}
