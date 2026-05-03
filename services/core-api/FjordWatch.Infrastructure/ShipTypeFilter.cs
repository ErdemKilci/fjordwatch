using FjordWatch.Domain;

namespace FjordWatch.Infrastructure;

internal static class ShipTypeFilter
{
    /// <summary>
    /// Expand a coarse-category filter into the underlying AIS ship-type codes
    /// (per ITU-R M.1371-5). Returned as a flat array for parameter binding.
    /// </summary>
    public static short[] FromCategories(IReadOnlyCollection<ShipTypeCategory>? categories)
    {
        if (categories is null || categories.Count == 0)
        {
            return Array.Empty<short>();
        }

        var codes = new HashSet<short>();
        foreach (var category in categories)
        {
            foreach (var code in CodesFor(category))
            {
                codes.Add(code);
            }
        }
        return codes.ToArray();
    }

    private static short[] CodesFor(ShipTypeCategory category) => category switch
    {
        ShipTypeCategory.Fishing => [30],
        ShipTypeCategory.Tug => [31, 32],
        ShipTypeCategory.Military => [35],
        ShipTypeCategory.Sailing => [36],
        ShipTypeCategory.Pleasure => [37],
        ShipTypeCategory.SearchAndRescue => [51],
        ShipTypeCategory.HighSpeed => Range(40, 49),
        ShipTypeCategory.Passenger => Range(60, 69),
        ShipTypeCategory.Cargo => Range(70, 79),
        ShipTypeCategory.Tanker => Range(80, 89),
        ShipTypeCategory.Other => Range(90, 99),
        _ => [],
    };

    private static short[] Range(short from, short toInclusive)
    {
        var len = toInclusive - from + 1;
        var arr = new short[len];
        for (var i = 0; i < len; i++)
        {
            arr[i] = (short)(from + i);
        }
        return arr;
    }
}
