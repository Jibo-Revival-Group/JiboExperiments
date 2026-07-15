using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.Conversions;

namespace Jibo.Cloud.Tests.Application;

public sealed class MeasurementConversionCatalogTests
{
    private readonly IMeasurementConversionCatalog _catalog = new MeasurementConversionCatalogLoader().LoadFromJson(
        """
        [
          {
            "smallUnit": { "singular": "foot", "plural": "feet", "aliases": ["foot", "feet"] },
            "largeUnit": { "singular": "mile", "plural": "miles", "aliases": ["mile", "miles"] },
            "count": 5280
          },
          {
            "smallUnit": { "singular": "inch", "plural": "inches", "aliases": ["inch", "inches"] },
            "largeUnit": { "singular": "foot", "plural": "feet", "aliases": ["foot", "feet"] },
            "count": 12
          }
        ]
        """);

    [Fact]
    public void TryResolve_FeetPerMile_ReturnsExpectedCount()
    {
        Assert.True(_catalog.TryResolve("feet", "mile", out var entry));
        Assert.Equal(5280, entry.Count);
        Assert.Equal("foot", entry.SmallUnit.Singular);
        Assert.Equal("mile", entry.LargeUnit.Singular);
    }

    [Fact]
    public void TryResolve_InchesPerFoot_ReturnsExpectedCount()
    {
        Assert.True(_catalog.TryResolve("inches", "foot", out var entry));
        Assert.Equal(12, entry.Count);
    }

    [Fact]
    public void TryResolve_ReverseDirection_ReturnsFalse()
    {
        Assert.False(_catalog.TryResolve("mile", "foot", out _));
    }
}
