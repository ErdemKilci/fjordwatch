namespace FjordWatch.Domain;

/// <summary>
/// Coarse-grained ship type categories matching the ITU-R M.1371-5 ship type
/// code ranges. The frontend uses these to color markers.
/// </summary>
public enum ShipTypeCategory
{
    Unknown = 0,
    Fishing,
    Cargo,
    Tanker,
    Passenger,
    HighSpeed,
    Tug,
    Pleasure,
    Sailing,
    Military,
    SearchAndRescue,
    Other,
}

public static class ShipTypeClassifier
{
    /// <summary>
    /// Map an AIS ship type code (per ITU-R M.1371-5 section 7.6.1) to a
    /// coarse category for display.
    /// </summary>
    public static ShipTypeCategory Categorize(short? code) => code switch
    {
        null or 0 => ShipTypeCategory.Unknown,
        30 => ShipTypeCategory.Fishing,
        31 or 32 => ShipTypeCategory.Tug,
        35 => ShipTypeCategory.Military,
        36 => ShipTypeCategory.Sailing,
        37 => ShipTypeCategory.Pleasure,
        51 => ShipTypeCategory.SearchAndRescue,
        >= 40 and <= 49 => ShipTypeCategory.HighSpeed,
        >= 60 and <= 69 => ShipTypeCategory.Passenger,
        >= 70 and <= 79 => ShipTypeCategory.Cargo,
        >= 80 and <= 89 => ShipTypeCategory.Tanker,
        _ => ShipTypeCategory.Other,
    };
}
