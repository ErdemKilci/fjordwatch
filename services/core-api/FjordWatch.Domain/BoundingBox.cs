namespace FjordWatch.Domain;

/// <summary>
/// Geographic bounding box in WGS84 (EPSG:4326). West/South are minimums,
/// East/North are maximums. Anti-meridian crossings (West &gt; East) are
/// rejected; callers should split into two boxes.
/// </summary>
public readonly record struct BoundingBox(double West, double South, double East, double North)
{
    public bool IsValid =>
        West >= -180 && West <= 180 &&
        East >= -180 && East <= 180 &&
        South >= -90 && South <= 90 &&
        North >= -90 && North <= 90 &&
        West < East && South < North;

    public bool Contains(double longitude, double latitude) =>
        longitude >= West && longitude <= East &&
        latitude >= South && latitude <= North;

    /// <summary>
    /// Parse a 4-tuple in the form "west,south,east,north".
    /// Returns false on any whitespace, count, parse, or range violation.
    /// </summary>
    public static bool TryParse(string? raw, out BoundingBox bbox)
    {
        bbox = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w) ||
            !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ||
            !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var e) ||
            !double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return false;
        }

        var candidate = new BoundingBox(w, s, e, n);
        if (!candidate.IsValid)
        {
            return false;
        }

        bbox = candidate;
        return true;
    }
}
