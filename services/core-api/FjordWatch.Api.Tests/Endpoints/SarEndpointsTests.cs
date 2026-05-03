using FjordWatch.Api.Endpoints;
using FjordWatch.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FjordWatch.Api.Tests.Endpoints;

public class SarEndpointsTests
{
    [Fact]
    public async Task ListDetections_clamps_limit()
    {
        var repo = new RecordingSarRepo();
        var result = await SarEndpoints.ListDetections(repo, "4,58,12,72", null, null, limit: 10_000, CancellationToken.None);
        result.Result.Should().BeOfType<Ok<IReadOnlyList<SarDetectionDto>>>();
        repo.LastLimit.Should().Be(SarEndpoints.MaxLimit);
    }

    [Fact]
    public async Task ListDetections_rejects_invalid_bbox()
    {
        var repo = new RecordingSarRepo();
        var result = await SarEndpoints.ListDetections(repo, "not,a,bbox", null, null, null, CancellationToken.None);
        result.Result.Should().BeOfType<BadRequest<string>>();
    }

    [Fact]
    public async Task ListDetections_rejects_since_older_than_30_days()
    {
        var repo = new RecordingSarRepo();
        var since = DateTimeOffset.UtcNow - TimeSpan.FromDays(60);
        var result = await SarEndpoints.ListDetections(repo, "4,58,12,72", since, null, null, CancellationToken.None);
        result.Result.Should().BeOfType<BadRequest<string>>();
    }

    [Fact]
    public async Task ListDetections_passes_only_dark_through()
    {
        var repo = new RecordingSarRepo();
        await SarEndpoints.ListDetections(repo, "4,58,12,72", null, onlyDark: true, null, CancellationToken.None);
        repo.LastOnlyDark.Should().BeTrue();
    }

    private sealed class RecordingSarRepo : ISarDetectionRepository
    {
        public BoundingBox LastBbox { get; private set; }
        public DateTimeOffset LastSince { get; private set; }
        public bool LastOnlyDark { get; private set; }
        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<SarDetection>> ListAsync(
            BoundingBox bbox,
            DateTimeOffset since,
            bool onlyDark,
            int limit,
            CancellationToken ct)
        {
            LastBbox = bbox;
            LastSince = since;
            LastOnlyDark = onlyDark;
            LastLimit = limit;
            IReadOnlyList<SarDetection> empty = [];
            return Task.FromResult(empty);
        }
    }
}
