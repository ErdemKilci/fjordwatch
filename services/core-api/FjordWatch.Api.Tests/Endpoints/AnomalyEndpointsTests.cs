using FjordWatch.Api.Endpoints;
using FjordWatch.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FjordWatch.Api.Tests.Endpoints;

public class AnomalyEndpointsTests
{
    [Fact]
    public async Task ListAnomalies_clamps_limit_and_min_score()
    {
        var repo = new RecordingAnomalyRepo();
        var result = await AnomalyEndpoints.ListAnomalies(repo, null, minScore: 5.0f, limit: 10_000, CancellationToken.None);
        result.Result.Should().BeOfType<Ok<IReadOnlyList<AnomalyDto>>>();
        repo.LastMinScore.Should().Be(1.0f);
        repo.LastLimit.Should().Be(AnomalyEndpoints.MaxLimit);
    }

    [Fact]
    public async Task ListAnomalies_rejects_since_older_than_30_days()
    {
        var repo = new RecordingAnomalyRepo();
        var since = DateTimeOffset.UtcNow - TimeSpan.FromDays(60);
        var result = await AnomalyEndpoints.ListAnomalies(repo, since, null, null, CancellationToken.None);
        result.Result.Should().BeOfType<BadRequest<string>>();
    }

    [Fact]
    public async Task ListAnomalies_uses_default_since_window_when_unspecified()
    {
        var repo = new RecordingAnomalyRepo();
        var before = DateTimeOffset.UtcNow;
        await AnomalyEndpoints.ListAnomalies(repo, null, null, null, CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        repo.LastSince.Should().BeOnOrAfter(before - AnomalyEndpoints.DefaultSinceWindow - TimeSpan.FromSeconds(1));
        repo.LastSince.Should().BeOnOrBefore(after - AnomalyEndpoints.DefaultSinceWindow + TimeSpan.FromSeconds(1));
    }

    private sealed class RecordingAnomalyRepo : IAnomalyRepository
    {
        public DateTimeOffset LastSince { get; private set; }
        public float LastMinScore { get; private set; }
        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<Anomaly>> ListAsync(
            DateTimeOffset since,
            float minScore,
            int limit,
            CancellationToken ct)
        {
            LastSince = since;
            LastMinScore = minScore;
            LastLimit = limit;
            IReadOnlyList<Anomaly> empty = [];
            return Task.FromResult(empty);
        }
    }
}
