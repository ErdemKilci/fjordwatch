using System.Text.Json.Serialization;

namespace FjordWatch.Api.Realtime;

/// <summary>
/// Wire shape produced by the Rust ingestion service onto the
/// <c>ais:positions</c> Redis Stream. Field names match the Rust struct
/// (camelCase via serde), so the JsonPropertyName attributes mirror that.
/// </summary>
public sealed class StreamMessage
{
    [JsonPropertyName("mmsi")]
    public long Mmsi { get; set; }

    [JsonPropertyName("ts")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("message_type")]
    public byte MessageType { get; set; }

    [JsonPropertyName("position")]
    public StreamPosition? Position { get; set; }
}

public sealed class StreamPosition
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("speed_over_ground")]
    public float? SpeedOverGround { get; set; }

    [JsonPropertyName("course_over_ground")]
    public float? CourseOverGround { get; set; }

    [JsonPropertyName("true_heading")]
    public ushort? TrueHeading { get; set; }
}
