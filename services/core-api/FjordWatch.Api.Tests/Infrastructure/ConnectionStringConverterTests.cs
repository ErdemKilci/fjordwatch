using FjordWatch.Infrastructure;
using FluentAssertions;

namespace FjordWatch.Api.Tests.Infrastructure;

public class ConnectionStringConverterTests
{
    [Fact]
    public void Postgres_url_is_translated_to_keyvalue()
    {
        var raw = "postgres://user:secret@db:5432/fjordwatch";
        var kv = NpgsqlConnectionStringConverter.ToKeyValue(raw);

        kv.Should().Contain("Host=db");
        kv.Should().Contain("Port=5432");
        kv.Should().Contain("Database=fjordwatch");
        kv.Should().Contain("Username=user");
        kv.Should().Contain("Password=secret");
    }

    [Fact]
    public void Postgres_keyvalue_is_passed_through()
    {
        var raw = "Host=db;Port=5432;Username=u;Password=p;Database=fw";
        NpgsqlConnectionStringConverter.ToKeyValue(raw).Should().Be(raw);
    }

    [Theory]
    [InlineData("redis://redis:6379/0", "redis:6379", "defaultDatabase=0")]
    [InlineData("redis://redis:6379", "redis:6379", null)]
    [InlineData("redis://:hunter2@redis:6379/3", "password=hunter2", "defaultDatabase=3")]
    public void Redis_url_is_translated(string raw, string mustContain1, string? mustContain2)
    {
        var cfg = RedisUrlConverter.ToConfigurationString(raw);
        cfg.Should().Contain(mustContain1);
        if (mustContain2 is not null)
        {
            cfg.Should().Contain(mustContain2);
        }
    }
}
