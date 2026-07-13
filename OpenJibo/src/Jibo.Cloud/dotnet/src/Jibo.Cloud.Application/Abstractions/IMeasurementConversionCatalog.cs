namespace Jibo.Cloud.Application.Abstractions;

public sealed record MeasurementUnit(string Singular, string Plural, IReadOnlyList<string> Aliases);

public sealed record MeasurementConversionEntry(
    MeasurementUnit SmallUnit,
    MeasurementUnit LargeUnit,
    double Count);

public interface IMeasurementConversionCatalog
{
    bool TryResolve(string smallUnitPhrase, string largeUnitPhrase, out MeasurementConversionEntry entry);
}
