using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IListViewRepository
{
    /// <summary>Every saved view for one list page, by name.</summary>
    Task<IReadOnlyList<ListViewEntity>> GetForListAsync(string list);

    Task<ListViewEntity?> GetAsync(string list, string viewId);

    Task AddAsync(ListViewEntity view);

    Task DeleteAsync(string list, string viewId);
}
