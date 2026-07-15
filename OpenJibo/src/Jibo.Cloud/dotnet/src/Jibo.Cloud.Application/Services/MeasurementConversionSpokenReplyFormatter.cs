using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

public static class MeasurementConversionSpokenReplyFormatter
{
    private const string UnresolvedReply =
        "I don't know that conversion. Try asking how many inches are in a foot.";

    public static string Format(MeasurementConversionEntry entry)
    {
        return Format(entry.Count, entry.SmallUnit.Singular, entry.SmallUnit.Plural, entry.LargeUnit.Singular);
    }

    public static string Format(double count, string smallSingular, string smallPlural, string largeSingular)
    {
        if (Math.Abs(count - 1) < 0.000_000_1)
            return $"There is 1 {smallSingular} in one {largeSingular}.";

        var formattedCount = MathSpokenReplyFormatter.FormatAnswer(count);
        var smallLabel = Math.Abs(count - 1) < 0.000_000_1 ? smallSingular : smallPlural;
        return $"There are {formattedCount} {smallLabel} in one {largeSingular}.";
    }

    public static string FormatUnresolved() => UnresolvedReply;
}
