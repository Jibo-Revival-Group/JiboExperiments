namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private JiboInteractionDecision BuildMeasurementConversionDecision(string transcript)
    {
        if (measurementConversionCatalog is null || !HowManyUnitsCommandParser.TryParse(transcript, out var query))
            return new JiboInteractionDecision(
                "measurement_conversion",
                MeasurementConversionSpokenReplyFormatter.FormatUnresolved());

        if (!measurementConversionCatalog.TryResolve(query.SmallUnitPhrase, query.LargeUnitPhrase, out var entry))
            return new JiboInteractionDecision(
                "measurement_conversion",
                MeasurementConversionSpokenReplyFormatter.FormatUnresolved());

        return new JiboInteractionDecision(
            "measurement_conversion",
            MeasurementConversionSpokenReplyFormatter.Format(entry));
    }
}
