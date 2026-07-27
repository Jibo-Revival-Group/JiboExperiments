namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Maps a robot <c>general.release</c> string to a spoken flavor label.
/// </summary>
public static class RobotFlavorClassifier
{
    public const string UnsureReply = "I'm not sure what flavor I am yet.";
    public const string BetaStock = "Beta Stock";
    public const string Stock = "Stock";
    public const string Beam = "BEam";
    public const string OldBeam = "Old BEam";

    public static bool IsBeam(string? release)
    {
        return !string.IsNullOrWhiteSpace(release) &&
               release.StartsWith("BEam.", StringComparison.OrdinalIgnoreCase);
    }

    public static string ClassifySpokenReply(string? release)
    {
        if (string.IsNullOrWhiteSpace(release))
            return UnsureReply;

        var trimmed = release.Trim();
        if (IsBeam(trimmed))
            return Beam;

        if (string.Equals(trimmed, "2.0.1", StringComparison.OrdinalIgnoreCase))
            return OldBeam;

        if (string.Equals(trimmed, "2.0.0", StringComparison.OrdinalIgnoreCase))
            return Stock;

        if (TryReadMajor(trimmed, out var major))
        {
            if (major == 0)
                return BetaStock;

            if (major == 1)
                return Stock;
        }

        return UnsureReply;
    }

    private static bool TryReadMajor(string release, out int major)
    {
        major = 0;
        var dot = release.IndexOf('.');
        var majorText = dot < 0 ? release : release[..dot];
        return int.TryParse(majorText, out major);
    }
}
