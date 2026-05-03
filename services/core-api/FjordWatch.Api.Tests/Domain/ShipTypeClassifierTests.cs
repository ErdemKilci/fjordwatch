using FjordWatch.Domain;
using FluentAssertions;

namespace FjordWatch.Api.Tests.Domain;

public class ShipTypeClassifierTests
{
    [Theory]
    [InlineData((short)30, ShipTypeCategory.Fishing)]
    [InlineData((short)35, ShipTypeCategory.Military)]
    [InlineData((short)51, ShipTypeCategory.SearchAndRescue)]
    [InlineData((short)42, ShipTypeCategory.HighSpeed)]
    [InlineData((short)61, ShipTypeCategory.Passenger)]
    [InlineData((short)71, ShipTypeCategory.Cargo)]
    [InlineData((short)83, ShipTypeCategory.Tanker)]
    [InlineData((short)95, ShipTypeCategory.Other)]
    [InlineData((short)0, ShipTypeCategory.Unknown)]
    public void Categorize_maps_known_codes(short code, ShipTypeCategory expected) =>
        ShipTypeClassifier.Categorize(code).Should().Be(expected);

    [Fact]
    public void Categorize_null_is_unknown() =>
        ShipTypeClassifier.Categorize(null).Should().Be(ShipTypeCategory.Unknown);
}
