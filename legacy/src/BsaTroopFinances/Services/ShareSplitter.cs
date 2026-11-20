namespace BsaTroopFinances.Services;

/// <summary>
/// Splits an amount across several recipients in proportion to their weights (all 1 for an even
/// split, hours worked for a fundraiser), to the cent. Each share is first rounded down; the cents
/// left over go one at a time to the shares that lost the most in rounding, ties broken by name,
/// so the shares always add up to exactly the total and the same inputs always give the same split.
/// </summary>
public static class ShareSplitter
{
    /// <summary>Returns no shares when there's nothing to split or any weight isn't positive.</summary>
    public static IReadOnlyList<(T Item, decimal Amount)> Split<T>(
        IReadOnlyList<T> items, IReadOnlyList<decimal> weights, decimal total, Func<T, string> tieBreak)
    {
        if (items.Count == 0 || items.Count != weights.Count || total <= 0 || weights.Any(w => w <= 0))
        {
            return [];
        }

        var totalCents = decimal.ToInt64(decimal.Round(total * 100m, 0, MidpointRounding.AwayFromZero));
        var totalWeight = weights.Sum();
        var rawCents = weights.Select(w => totalCents * w / totalWeight).ToList();
        var cents = rawCents.Select(decimal.Floor).Select(decimal.ToInt64).ToArray();
        var centsRemaining = totalCents - cents.Sum();
        var remainderOrder = rawCents.Select((raw, index) => new { Index = index, Fraction = raw - decimal.Floor(raw) })
            .OrderByDescending(x => x.Fraction)
            .ThenBy(x => tieBreak(items[x.Index]))
            .ToList();

        for (long index = 0; index < centsRemaining; index++)
        {
            cents[remainderOrder[(int)(index % remainderOrder.Count)].Index]++;
        }

        return items.Select((item, index) => (item, cents[index] / 100m)).ToList();
    }
}
