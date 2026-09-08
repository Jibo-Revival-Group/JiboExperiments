using System.IO.Compression;
using System.Text;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

/// <summary>Observed log identifiers, separate from verified registration evidence.</summary>
public sealed record RobotLogIdentityEvidence(
    string? SerialNumber, string? RobotName, bool HasSerialEvidence, bool HasConflictingEvidence)
{
    public static RobotLogIdentityEvidence Extract(byte[] content)
    {
        const int maxEvidenceBytes = 256 * 1024;
        try
        {
            var bytes = content;
            if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            {
                using var input = new MemoryStream(bytes, writable: false);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                var buffer = new byte[16 * 1024];
                while (output.Length < maxEvidenceBytes)
                {
                    var read = gzip.Read(buffer, 0, Math.Min(buffer.Length, maxEvidenceBytes - (int)output.Length));
                    if (read == 0) break;
                    output.Write(buffer, 0, read);
                }
                bytes = output.ToArray();
            }
            else if (bytes.Length > maxEvidenceBytes) bytes = bytes[..maxEvidenceBytes];

            var text = Encoding.UTF8.GetString(bytes);
            var serials = RobotIdentityCandidateExtractor.ExtractSerialNumbers(text);
            var names = RobotIdentityCandidateExtractor.Extract(text)
                .Select(candidate => candidate.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new(serials.Count == 1 ? serials[0] : null, names.Length == 1 ? names[0] : null,
                serials.Count > 0, serials.Count > 1 || names.Length > 1);
        }
        catch (InvalidDataException)
        {
            return new(null, null, false, false);
        }
    }

    public DeviceRegistration? ResolveDevice(IReadOnlyList<DeviceRegistration> devices)
    {
        if (HasConflictingEvidence) return null;
        var serialMatches = string.IsNullOrWhiteSpace(SerialNumber) ? [] : devices.Where(device =>
            string.Equals(device.VerifiedSerialNumber, SerialNumber, StringComparison.OrdinalIgnoreCase)).ToArray();
        var nameMatches = string.IsNullOrWhiteSpace(RobotName) ? [] : devices.Where(device =>
            new[] { device.DeviceId, device.RobotId, device.FriendlyName }.Any(name =>
                string.Equals(name, RobotName, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (serialMatches.Length > 1 || nameMatches.Length > 1) return null;
        if (serialMatches.Length == 1)
            return nameMatches.Length == 0 || nameMatches[0].DeviceId == serialMatches[0].DeviceId
                ? serialMatches[0] : null;
        if (nameMatches.Length != 1) return null;
        var namedDevice = nameMatches[0];
        // An observed serial may help an existing exact name match, but may never
        // override a different verified serial or become verified by this route.
        return HasSerialEvidence && !string.IsNullOrWhiteSpace(namedDevice.VerifiedSerialNumber) &&
            !string.Equals(namedDevice.VerifiedSerialNumber, SerialNumber, StringComparison.OrdinalIgnoreCase)
            ? null : namedDevice;
    }
}
