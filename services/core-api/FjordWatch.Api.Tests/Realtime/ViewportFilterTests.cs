using FjordWatch.Api.Realtime;
using FjordWatch.Domain;
using FluentAssertions;

namespace FjordWatch.Api.Tests.Realtime;

public class ViewportFilterTests
{
    [Fact]
    public void ShouldSend_inside_bbox_passes_first_message()
    {
        var filter = new ViewportFilter
        {
            Viewport = new BoundingBox(4, 58, 12, 72),
        };
        var now = DateTimeOffset.UtcNow;
        filter.ShouldSend(123, longitude: 8, latitude: 65, now).Should().BeTrue();
    }

    [Fact]
    public void ShouldSend_outside_bbox_drops()
    {
        var filter = new ViewportFilter
        {
            Viewport = new BoundingBox(4, 58, 12, 72),
        };
        filter.ShouldSend(123, longitude: 0, latitude: 65, DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void ShouldSend_rate_limits_within_min_interval()
    {
        var filter = new ViewportFilter
        {
            Viewport = new BoundingBox(4, 58, 12, 72),
            MinIntervalPerVessel = TimeSpan.FromSeconds(3),
        };
        var t0 = DateTimeOffset.UtcNow;
        filter.ShouldSend(123, 8, 65, t0).Should().BeTrue();
        filter.ShouldSend(123, 8, 65, t0.AddSeconds(1)).Should().BeFalse();
        filter.ShouldSend(123, 8, 65, t0.AddSeconds(4)).Should().BeTrue();
    }

    [Fact]
    public void ShouldSend_rate_limit_is_per_vessel()
    {
        var filter = new ViewportFilter
        {
            Viewport = new BoundingBox(4, 58, 12, 72),
            MinIntervalPerVessel = TimeSpan.FromSeconds(3),
        };
        var t0 = DateTimeOffset.UtcNow;
        filter.ShouldSend(123, 8, 65, t0).Should().BeTrue();
        filter.ShouldSend(456, 8, 65, t0).Should().BeTrue();
    }

    [Fact]
    public void ShouldSend_no_viewport_passes_all_locations()
    {
        var filter = new ViewportFilter();
        filter.ShouldSend(1, -179, -89, DateTimeOffset.UtcNow).Should().BeTrue();
    }
}
