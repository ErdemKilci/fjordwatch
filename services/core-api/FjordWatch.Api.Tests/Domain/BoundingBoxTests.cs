using FjordWatch.Domain;
using FluentAssertions;

namespace FjordWatch.Api.Tests.Domain;

public class BoundingBoxTests
{
    [Theory]
    [InlineData("4,58,12,72", true)]
    [InlineData("4.5,58.0,12.0,72.0", true)]
    [InlineData(" 4 , 58 , 12 , 72 ", true)]
    [InlineData("4,58,12", false)]
    [InlineData("4,58,12,72,99", false)]
    [InlineData("not,a,box,sorry", false)]
    [InlineData("12,58,4,72", false)] // west > east
    [InlineData("4,72,12,58", false)] // south > north
    [InlineData("-181,58,12,72", false)]
    [InlineData("4,-91,12,72", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_validates_input(string? raw, bool expected)
    {
        var ok = BoundingBox.TryParse(raw, out var box);
        ok.Should().Be(expected);
        if (ok)
        {
            box.IsValid.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(8.0, 65.0, true)]
    [InlineData(4.0, 58.0, true)] // on lower-left corner
    [InlineData(12.0, 72.0, true)] // on upper-right corner
    [InlineData(15.0, 65.0, false)]
    [InlineData(8.0, 50.0, false)]
    public void Contains_checks_inclusive_bounds(double lon, double lat, bool expected)
    {
        var box = new BoundingBox(4, 58, 12, 72);
        box.Contains(lon, lat).Should().Be(expected);
    }
}
