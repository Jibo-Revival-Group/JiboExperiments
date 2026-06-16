namespace Jibo.Cloud.Infrastructure.Audio;

internal static class OggOpusAudioNormalizer
{
    public static byte[] Normalize(IReadOnlyList<byte[]> pages)
    {
        if (pages.Count == 0) return [];

        return pages.SelectMany(static page => page).ToArray();
    }
}
