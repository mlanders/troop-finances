using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;

namespace BsaTroopFinances.Services;

/// <summary>
/// A family as the app sees it: guardians who share a scout, and all of their scouts. There is no
/// family record; households are worked out from the guardian-scout links whenever they're needed.
/// An adult with no scouts linked is a household of one.
/// </summary>
public record Household(string Name, IReadOnlyList<GuardianEntity> Guardians, IReadOnlyList<string> ScoutIds)
{
    /// <summary>One guardian and only their own scouts: what a parent sees in the portal, never a
    /// scout that is linked only to the other adult in the household.</summary>
    public static Household Alone(GuardianEntity guardian, IEnumerable<string> scoutIds) => new(guardian.FullName, [guardian], scoutIds.ToList());
}

public static class Households
{
    /// <summary>Groups the guardians into households: two parents linked to the same children make one
    /// household, so they get one invoice or statement between them, not one each with the same
    /// charges on both. Names read "A and B", in name order.</summary>
    public static async Task<IReadOnlyList<Household>> BuildAsync(IEnumerable<GuardianEntity> guardians, IGuardianScoutLinkRepository links)
    {
        var households = new List<(List<GuardianEntity> Guardians, List<string> ScoutIds)>();
        foreach (var guardian in guardians.OrderBy(g => g.FullName))
        {
            var scoutIds = (await links.GetScoutsForGuardianAsync(guardian.GuardianId)).Select(l => l.ScoutId).ToList();
            var members = new List<GuardianEntity> { guardian };
            foreach (var shared in households.Where(h => h.ScoutIds.Intersect(scoutIds).Any()).ToList())
            {
                members.AddRange(shared.Guardians);
                scoutIds = shared.ScoutIds.Union(scoutIds).ToList();
                households.Remove(shared);
            }

            households.Add((members.OrderBy(g => g.FullName).ToList(), scoutIds));
        }

        return households.Select(h => new Household(string.Join(" and ", h.Guardians.Select(g => g.FullName)), h.Guardians, h.ScoutIds)).ToList();
    }
}
