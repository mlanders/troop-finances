using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;

namespace BsaTroopFinances.Tests;

/// <summary>The real AdminUserRepository against a real SQL database (LocalDB).</summary>
public class SqlAdminUserRepositoryTests(SqlTestDatabase sql) : IClassFixture<SqlTestDatabase>
{
    private static readonly DateTimeOffset When = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    private async Task ReadyAsync()
    {
        Skip.IfNot(sql.Available, sql.UnavailableReason);
        await sql.ResetAsync();
    }

    [SkippableFact]
    public async Task Admin_users_round_trip_count_by_role_and_delete()
    {
        await ReadyAsync();
        var users = new AdminUserRepository(sql.Db);
        await users.AddAsync(new AdminUserEntity { RowKey = "microsoft:pat", Name = "Pat", Email = "pat@example.com", Role = AdminAccessRole.Editor, FirstSeenUtc = When, LastSeenUtc = When });
        await users.AddAsync(new AdminUserEntity { RowKey = "google:1", Name = "Jordan", Email = "jordan@example.com", FirstSeenUtc = When, LastSeenUtc = When });

        var jordan = await users.GetAsync("google:1");
        Assert.NotNull(jordan);
        Assert.Equal((AdminAccessRole.None, When), (jordan.Role, jordan.FirstSeenUtc));
        Assert.Equal(1, await users.CountWithRoleAsync(AdminAccessRole.Editor));

        jordan.Role = AdminAccessRole.Viewer;
        jordan.IsCommitteeChair = true;
        await users.UpdateAsync(jordan);
        // The new row version comes back on the entity, so it can be updated again.
        jordan.Name = "Jordan Lee";
        await users.UpdateAsync(jordan);

        var reread = await users.GetAsync("google:1");
        Assert.Equal((AdminAccessRole.Viewer, true, "Jordan Lee"), (reread!.Role, reread.IsCommitteeChair, reread.Name));
        Assert.Equal(["Jordan Lee", "Pat"], (await users.GetAllAsync()).Select(u => u.Name));

        await users.DeleteAsync("google:1");
        Assert.Null(await users.GetAsync("google:1"));
    }

    [SkippableFact]
    public async Task Sign_ins_round_trip_find_by_verified_address_and_go_with_their_person()
    {
        await ReadyAsync();
        var users = new AdminUserRepository(sql.Db);
        await users.AddAsync(new AdminUserEntity { RowKey = "microsoft:pat", Name = "Pat", FirstSeenUtc = When, LastSeenUtc = When });
        await users.AddSignInAsync(new AdminSignInEntity { RowKey = "microsoft:pat", UserKey = "microsoft:pat", Provider = "Microsoft", VerifiedEmail = "pat@example.com", FirstSeenUtc = When, LastSeenUtc = When });
        await users.AddSignInAsync(new AdminSignInEntity { RowKey = "google:pat", UserKey = "microsoft:pat", Provider = "Google", VerifiedEmail = "pat@example.com", FirstSeenUtc = When, LastSeenUtc = When.AddDays(1) });
        await users.AddSignInAsync(new AdminSignInEntity { RowKey = "google:other", UserKey = "google:other", Provider = "Google", FirstSeenUtc = When, LastSeenUtc = When });

        Assert.Equal(["google:pat", "microsoft:pat"], (await users.FindSignInsByVerifiedEmailAsync("pat@example.com")).Select(s => s.RowKey));
        Assert.Equal(2, (await users.GetSignInsForUserAsync("microsoft:pat")).Count);

        var google = await users.GetSignInAsync("google:pat");
        google!.LastSeenUtc = When.AddDays(2);
        await users.UpdateSignInAsync(google);
        Assert.Equal(When.AddDays(2), (await users.GetSignInAsync("google:pat"))!.LastSeenUtc);

        await users.DeleteAsync("microsoft:pat");
        Assert.Equal(["google:other"], (await users.GetAllSignInsAsync()).Select(s => s.RowKey));
    }
}
