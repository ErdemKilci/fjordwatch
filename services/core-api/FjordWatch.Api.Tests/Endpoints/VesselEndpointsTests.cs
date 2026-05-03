using FjordWatch.Api.Contracts;
using FjordWatch.Api.Endpoints;
using FjordWatch.Domain;
using FluentAssertions;

namespace FjordWatch.Api.Tests.Endpoints;

public class VesselEndpointsTests
{
    [Fact]
    public void ParseCategories_empty_returns_empty()
    {
        VesselEndpoints.ParseCategories(null).Should().BeEmpty();
        VesselEndpoints.ParseCategories("").Should().BeEmpty();
        VesselEndpoints.ParseCategories(" ").Should().BeEmpty();
    }

    [Fact]
    public void ParseCategories_valid_round_trips_case_insensitive()
    {
        var result = VesselEndpoints.ParseCategories("cargo,Tanker,FISHING");
        result.Should()
            .NotBeNull()
            .And.BeEquivalentTo(new[] { ShipTypeCategory.Cargo, ShipTypeCategory.Tanker, ShipTypeCategory.Fishing });
    }

    [Fact]
    public void ParseCategories_invalid_returns_null()
    {
        VesselEndpoints.ParseCategories("cargo,unicorn").Should().BeNull();
    }

    [Fact]
    public void GeoJson_LineString_packs_coordinates_in_order()
    {
        var track = new Track(
            12345,
            new[]
            {
                new TrackPoint(DateTimeOffset.Parse("2024-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), 5.0, 60.0, 12.0f, 90.0f),
                new TrackPoint(DateTimeOffset.Parse("2024-01-01T00:01:00Z", System.Globalization.CultureInfo.InvariantCulture), 5.1, 60.1, 12.5f, 92.0f),
            });

        var feature = GeoJson.ToLineString(track);

        feature.Type.Should().Be("Feature");
        feature.Geometry.Type.Should().Be("LineString");
        feature.Geometry.Coordinates.Should().HaveCount(2);
        feature.Geometry.Coordinates[0].Should().Equal(5.0, 60.0);
        feature.Geometry.Coordinates[1].Should().Equal(5.1, 60.1);
        feature.Properties.Mmsi.Should().Be(12345);
        feature.Properties.PointCount.Should().Be(2);
    }

    [Fact]
    public void GeoJson_LineString_handles_empty_track()
    {
        var feature = GeoJson.ToLineString(new Track(12345, []));
        feature.Geometry.Coordinates.Should().BeEmpty();
        feature.Properties.PointCount.Should().Be(0);
        feature.Properties.Start.Should().BeNull();
    }
}
