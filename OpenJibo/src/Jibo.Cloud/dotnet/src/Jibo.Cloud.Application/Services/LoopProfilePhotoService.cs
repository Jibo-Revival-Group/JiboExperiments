using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

public sealed class LoopProfilePhotoService(IMediaContentStore media, PortalSessionService sessions)
{
    public const string ContentType = "image/jpeg";
    public const int MaxBytes = 256 * 1024;
    public const int MaxDimension = 512;
    public const string RobotDownloadPrefix = "/api/robot/loop-profile-photos";
    public const string RobotDownloadRoute = RobotDownloadPrefix + "/{loopId}/{memberId}/{version}.jpg";

    public static bool CanHavePhoto(LoopMemberRecord member) =>
        !member.Type.Equals("robot", StringComparison.OrdinalIgnoreCase) &&
        !member.Status.Equals("removed", StringComparison.OrdinalIgnoreCase) &&
        !JiboIdentityResolver.IsStockOwnerPlaceholder(member.Id);

    public static bool IsSupportedJpeg(ReadOnlySpan<byte> photo)
    {
        if (photo.Length < 4 || photo.Length > MaxBytes ||
            photo[0] != 0xff || photo[1] != 0xd8 || photo[^2] != 0xff || photo[^1] != 0xd9) return false;

        var hasFrame = false;
        for (var offset = 2; offset + 4 <= photo.Length;)
        {
            if (photo[offset++] != 0xff) return false;
            while (offset < photo.Length && photo[offset] == 0xff) offset++;
            if (offset + 3 > photo.Length) return false;

            var marker = photo[offset++];
            var length = BinaryPrimitives.ReadUInt16BigEndian(photo[offset..]);
            if (length < 2 || offset + length > photo.Length - 2) return false;

            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (hasFrame || length < 11 || photo[offset + 2] != 8) return false;
                var height = BinaryPrimitives.ReadUInt16BigEndian(photo[(offset + 3)..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(photo[(offset + 5)..]);
                var components = photo[offset + 7];
                if (width is 0 or > MaxDimension || height is 0 or > MaxDimension ||
                    components is not (1 or 3) || length != 8 + 3 * components) return false;
                hasFrame = true;
            }

            if (marker == 0xda) return hasFrame && length >= 8 && offset + length < photo.Length - 2;
            offset += length;
        }

        return false;
    }

    public Task StoreAsync(string loopId, string memberId, byte[] photo, CancellationToken cancellationToken) =>
        media.StoreAsync(StoragePath(loopId, memberId), ContentType, photo, null, cancellationToken);

    public async Task<byte[]?> LoadAsync(string loopId, string memberId, CancellationToken cancellationToken)
    {
        var photo = await media.LoadAsync(StoragePath(loopId, memberId), cancellationToken);
        return photo?.Content is { Length: > 0 and <= MaxBytes } bytes ? bytes : null;
    }

    public async Task<IReadOnlyDictionary<string, string>> BuildRobotUrlsAsync(string loopId,
        IEnumerable<LoopMemberRecord> members, string baseUrl, CancellationToken cancellationToken)
    {
        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members.Where(CanHavePhoto))
        {
            var photo = await LoadAsync(loopId, member.Id, cancellationToken);
            if (photo is null) continue;

            var version = Version(photo);
            var token = sessions.CreateLoopPhotoToken(loopId, member.Id, version);
            urls[member.Id] = $"{baseUrl}{RobotDownloadPrefix}/{Uri.EscapeDataString(loopId)}/" +
                              $"{Uri.EscapeDataString(member.Id)}/{version}.jpg?ticket={Uri.EscapeDataString(token)}";
        }

        return urls;
    }

    public static string Version(byte[] photo) => Convert.ToHexStringLower(SHA256.HashData(photo));

    private static string StoragePath(string loopId, string memberId)
    {
        var identity = Encoding.UTF8.GetBytes($"{loopId.ToUpperInvariant()}\0{memberId.ToUpperInvariant()}");
        return $"loop-profile-photos/{Convert.ToHexStringLower(SHA256.HashData(identity))}.jpg";
    }
}
