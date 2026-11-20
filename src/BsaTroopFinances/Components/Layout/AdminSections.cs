namespace BsaTroopFinances.Components.Layout;

/// <summary>One page within an admin section, shown as a tab above the page.</summary>
public record AdminTab(string Label, string Href, bool WritersOnly = false);

/// <summary>A top-level item in the admin header and the pages that belong to it.</summary>
public record AdminSection(string Label, string Href, IReadOnlyList<AdminTab> Tabs, bool ExactMatch = false);

/// <summary>
/// The admin area's navigation: seven sections in the header, each with its own pages as tabs. Both
/// the header (AdminLayout) and the tab row (SectionTabs) are drawn from this one list. Pages keep
/// their own addresses; a page belongs to the tab whose address is the longest match for it, so
/// /admin/scouts/abc is under Families → Scouts.
/// </summary>
public static class AdminSections
{
    public static readonly IReadOnlyList<AdminSection> All =
    [
        new("Dashboard", "/admin", [new("Dashboard", "/admin")], ExactMatch: true),
        new("Reimbursements", "/admin/reimbursements", [new("Reimbursements", "/admin/reimbursements")]),
        new("Invoices", "/admin/invoices",
        [
            new("Invoices", "/admin/invoices"),
            new("Record family payment", "/admin/invoices/family-payment", WritersOnly: true),
            new("Invoice all active scouts", "/admin/invoices/bulk", WritersOnly: true),
            new("Trip sign-ups", "/admin/invoices/trips", WritersOnly: true),
        ]),
        new("Fundraising", "/admin/fundraisers",
        [
            new("Fundraisers", "/admin/fundraisers"),
            new("Donations", "/admin/donations"),
        ]),
        new("Families", "/admin/families",
        [
            new("Families", "/admin/families"),
            new("Scouts", "/admin/scouts"),
            new("Guardians", "/admin/guardians"),
            new("Sign-in app links", "/admin/families/signin-links"),
        ]),
        new("Bank", "/admin/bank",
        [
            new("Accounts", "/admin/bank"),
            new("Deposits", "/admin/bank/deposits"),
            new("Reconcile", "/admin/bank/reconcile"),
        ]),
        new("Reports", "/admin/reports",
        [
            new("Summary", "/admin/reports"),
            new("Troop finances", "/admin/reports/troop"),
            new("Monthly", "/admin/reports/monthly"),
            new("Activity history", "/admin/settings/activity"),
        ]),
    ];

    /// <summary>The section and tab a page belongs to, or null for pages outside the sections
    /// (Settings, sign-in). <paramref name="path"/> is the page's path, e.g. "/admin/scouts/abc".</summary>
    public static (AdminSection Section, AdminTab Tab)? Find(string path)
    {
        path = "/" + path.Split('?', '#')[0].Trim('/');
        (AdminSection Section, AdminTab Tab)? best = null;
        foreach (var section in All)
        {
            foreach (var tab in section.Tabs)
            {
                var matches = section.ExactMatch
                    ? string.Equals(path, tab.Href, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(path, tab.Href, StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith(tab.Href + "/", StringComparison.OrdinalIgnoreCase);
                if (matches && (best is null || tab.Href.Length > best.Value.Tab.Href.Length))
                {
                    best = (section, tab);
                }
            }
        }

        return best;
    }
}
