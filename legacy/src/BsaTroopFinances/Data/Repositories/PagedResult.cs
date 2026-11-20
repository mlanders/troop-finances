namespace BsaTroopFinances.Data.Repositories;

/// <summary>One page of a list plus how many rows match in total (for "Page 3 of 7").</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount)
{
    public int TotalPages(int pageSize) => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)pageSize));
}
