namespace Jibo.Cloud.Application.Services;

public static class BufferedAudioPageClassifier
{
    public static BufferedAudioPageCounts Describe(IReadOnlyList<byte[]> pages)
    {
        var rawFrameCount = pages.Count;
        var metadataPageCount = CountMetadataPages(pages);
        var audioBearingPageCount = rawFrameCount - metadataPageCount;

        return new BufferedAudioPageCounts(
            rawFrameCount,
            audioBearingPageCount < 0 ? 0 : audioBearingPageCount,
            metadataPageCount);
    }

    public static int CountAudioBearingPages(IReadOnlyList<byte[]> pages)
    {
        return pages.Count(IsAudioBearingPage);
    }

    public static int CountMetadataPages(IReadOnlyList<byte[]> pages)
    {
        return pages.Count(IsMetadataPage);
    }

    public static bool IsAudioBearingPage(byte[] page)
    {
        return !IsMetadataPage(page);
    }

    public static bool IsMetadataPage(byte[] page)
    {
        return ContainsMarker(page, "OpusHead"u8) || ContainsMarker(page, "OpusTags"u8);
    }

    private static bool ContainsMarker(byte[] page, ReadOnlySpan<byte> marker)
    {
        return page.AsSpan().IndexOf(marker) >= 0;
    }
}

public readonly record struct BufferedAudioPageCounts(
    int RawFrameCount,
    int AudioBearingPageCount,
    int MetadataPageCount);
