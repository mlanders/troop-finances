using System.Security.Claims;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BsaTroopFinances.Services;

public class AdminUserService(
    IAdminUserRepository repository,
    IFinanceAuditRepository auditRepository,
    INotificationService notificationService,
    IMemoryCache cache,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<AdminUserService> logger) : IAdminUserService
{
    /// <summary>How long a role is remembered before the database is asked again -- the most a
    /// change made on another server instance can take to apply. Changes made here apply at once.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    /// <summary>Last-seen time, name and email are written back at most this often.</summary>
    private static readonly TimeSpan SeenRefresh = TimeSpan.FromMinutes(10);

    public const string BootstrapEditorsKey = "Auth:BootstrapEditors";

    /// <summary>Addresses of the Global admins: Read only day to day, can always turn on temporary
    /// Edit (with the alert email), and can allow others to.</summary>
    public const string GlobalAdminsKey = "Auth:GlobalAdmins";

    public async Task<AdminUserEntity?> ResolveAsync(ClaimsPrincipal user)
    {
        var key = AdminIdentity.Key(user);
        if (key.Length == 0)
        {
            return null;
        }

        if (cache.TryGetValue(SignInCacheKey(key), out string? cachedUserKey)
            && cache.TryGetValue(CacheKey(cachedUserKey!), out AdminUserEntity? cached) && cached is not null)
        {
            return cached;
        }

        var now = timeProvider.GetUtcNow();
        var name = AdminIdentity.Name(user);
        var email = AdminIdentity.Email(user) ?? string.Empty;
        var verified = NormalizedVerifiedEmail(user);
        var signIn = await repository.GetSignInAsync(key);
        AdminUserEntity row;
        if (signIn is null)
        {
            (signIn, row) = await AddSignInAsync(user, key, verified, now);
        }
        else
        {
            row = await repository.GetAsync(signIn.UserKey) ?? await AddUserAsync(signIn.UserKey, name, email, now);
            if (now - signIn.LastSeenUtc > SeenRefresh || signIn.Name != name || signIn.Email != email || signIn.VerifiedEmail != verified)
            {
                signIn.Name = name;
                signIn.Email = email;
                signIn.VerifiedEmail = verified;
                signIn.LastSeenUtc = now;
                await TryUpdateSignInAsync(signIn);
            }
        }

        if (verified.Length > 0)
        {
            row = await CombineMatchingPeopleAsync(row, verified);
        }

        if (now - row.LastSeenUtc > SeenRefresh || row.Name != name || row.Email != email)
        {
            row.Name = name;
            row.Email = email;
            row.LastSeenUtc = now;
            row = await TryUpdateAsync(row);
        }

        if (row.Role == AdminAccessRole.None && await IsBootstrapEditorAsync(user))
        {
            row.Role = AdminAccessRole.Editor;
            row.RoleChangedBy = BootstrapEditorsKey;
            row.RoleChangedUtc = now;
            await repository.UpdateAsync(row);
            await auditRepository.AddAsync(FinanceAuditEntityType.AdminUser, row.RowKey, "Edit Access Granted", BootstrapEditorsKey,
                details: $"{Describe(row)} was the first Editor, from the {BootstrapEditorsKey} setting.");
            logger.LogWarning("Gave {Email} Edit access from {Setting}: there were no Editors yet", email, BootstrapEditorsKey);
        }

        // After the bootstrap step, so a listed address that's also a bootstrap one still becomes
        // the first Editor rather than settling for Read only.
        row = await SyncGlobalAdminAsync(row, now);

        cache.Set(SignInCacheKey(key), row.RowKey, CacheFor);
        cache.Set(CacheKey(row.RowKey), row, CacheFor);
        return row;
    }

    public async Task<string> GetRoleAsync(string signInKey)
    {
        var userKey = await GetUserKeyAsync(signInKey);
        if (userKey is null)
        {
            return AdminAccessRole.None;
        }

        // The effective role, so temporary Edit ends on time: an open page still acting as Editor
        // stops matching and is sent back through sign-in (AdminRevalidatingAuthStateProvider).
        if (cache.TryGetValue(CacheKey(userKey), out AdminUserEntity? cached) && cached is not null)
        {
            return cached.EffectiveRole(timeProvider.GetUtcNow());
        }

        var row = await repository.GetAsync(userKey);
        if (row is null)
        {
            return AdminAccessRole.None;
        }

        cache.Set(CacheKey(userKey), row, CacheFor);
        return row.EffectiveRole(timeProvider.GetUtcNow());
    }

    public Task<IReadOnlyList<AdminUserEntity>> GetAllAsync() => repository.GetAllAsync();

    public Task<IReadOnlyList<AdminSignInEntity>> GetAllSignInsAsync() => repository.GetAllSignInsAsync();

    /// <summary>
    /// Records an account signing in for the first time, and finds whose it is: a person already
    /// keyed by it (restored from before accounts were recorded separately), else the person
    /// with another account the provider vouched for the same address on, else someone new --
    /// an access request.
    /// </summary>
    private async Task<(AdminSignInEntity SignIn, AdminUserEntity Row)> AddSignInAsync(ClaimsPrincipal user, string key, string verified, DateTimeOffset now)
    {
        var name = AdminIdentity.Name(user);
        var email = AdminIdentity.Email(user) ?? string.Empty;
        var row = await repository.GetAsync(key);
        var linked = false;
        if (row is null && verified.Length > 0)
        {
            row = await FindPersonByVerifiedEmailAsync(verified, exceptUserKey: null);
            linked = row is not null;
        }

        row ??= await AddUserAsync(key, name, email, now);
        var signIn = new AdminSignInEntity
        {
            RowKey = key,
            UserKey = row.RowKey,
            Provider = AdminIdentity.Provider(user),
            Name = name,
            Email = email,
            VerifiedEmail = verified,
            FirstSeenUtc = now,
            LastSeenUtc = now,
        };
        try
        {
            await repository.AddSignInAsync(signIn);
        }
        catch (DbUpdateException)
        {
            // Two requests from a brand-new sign-in raced; the other one recorded it.
            signIn = await repository.GetSignInAsync(key) ?? throw new InvalidOperationException($"Couldn't record admin sign-in {key}.");
            return (signIn, await repository.GetAsync(signIn.UserKey) ?? row);
        }

        if (linked)
        {
            await auditRepository.AddAsync(FinanceAuditEntityType.AdminUser, row.RowKey, "Sign-in Linked", name,
                details: $"{signIn.Provider} account {Describe(name, email)} joined {Describe(row)}: both verified as {verified}.");
            logger.LogInformation("Admin sign-in {SignInKey} joined {UserKey} by verified address {Email}", key, row.RowKey, verified);
        }

        return (signIn, row);
    }

    private async Task<AdminUserEntity> AddUserAsync(string userKey, string name, string email, DateTimeOffset now)
    {
        var row = new AdminUserEntity
        {
            RowKey = userKey,
            Name = name,
            Email = email,
            FirstSeenUtc = now,
            LastSeenUtc = now,
        };
        try
        {
            await repository.AddAsync(row);
            logger.LogInformation("New admin sign-in {UserKey} ({Email}) is waiting for access", userKey, email);
            return row;
        }
        catch (DbUpdateException)
        {
            // Two requests from a brand-new sign-in raced; the other one created the row.
            return await repository.GetAsync(userKey) ?? throw new InvalidOperationException($"Couldn't record admin user {userKey}.");
        }
    }

    /// <summary>
    /// Anyone else listed separately with an account the provider vouched for the same address
    /// on is the same person -- e.g. both accounts had access before they could be grouped, or
    /// the second was used before the first one's address was known -- so they're combined.
    /// </summary>
    private async Task<AdminUserEntity> CombineMatchingPeopleAsync(AdminUserEntity row, string verified)
    {
        var others = (await repository.FindSignInsByVerifiedEmailAsync(verified)).Select(s => s.UserKey).Distinct().ToList();
        foreach (var userKey in others)
        {
            if (userKey == row.RowKey || await repository.GetAsync(userKey) is not { } other)
            {
                continue;
            }

            try
            {
                row = await CombineAsync(row, other, row.Name, $"both verified as {verified}");
            }
            catch (DbUpdateConcurrencyException)
            {
                // Changed at the same moment (e.g. an Editor at work); the next sign-in tries again.
                break;
            }
        }

        return row;
    }

    public async Task CombineAsync(string userKey, string otherUserKey, ClaimsPrincipal actor)
    {
        EnsureEditor(actor);
        if (userKey == otherUserKey)
        {
            return;
        }

        var row = await repository.GetAsync(userKey) ?? throw new InvalidOperationException("That person isn't on the list any more.");
        var other = await repository.GetAsync(otherUserKey) ?? throw new InvalidOperationException("That person isn't on the list any more.");
        var signIns = await repository.GetAllSignInsAsync();
        if (!EmailsOf(row, signIns).Overlaps(EmailsOf(other, signIns)))
        {
            throw new InvalidOperationException("Only people who share an email address can be combined.");
        }

        await CombineAsync(row, other, ActorName(actor), $"combined by {ActorName(actor)}");
    }

    /// <summary>
    /// Makes two people one: the one with more access (or, if equal, the one first seen) stays,
    /// and the other's accounts move to them. Nobody loses access -- whichever account they use,
    /// they get the higher of the two roles -- so no last-Editor check is needed.
    /// </summary>
    private async Task<AdminUserEntity> CombineAsync(AdminUserEntity a, AdminUserEntity b, string actorName, string why)
    {
        var (keep, gone) = Rank(b) > Rank(a) || (Rank(b) == Rank(a) && b.FirstSeenUtc < a.FirstSeenUtc) ? (b, a) : (a, b);
        keep.IsCommitteeChair |= gone.IsCommitteeChair;
        keep.IsGlobalAdmin |= gone.IsGlobalAdmin;
        if (keep.Role == AdminAccessRole.Viewer)
        {
            // The longer of the two permissions; null (from before end dates) has no end.
            var now = timeProvider.GetUtcNow();
            if (gone.IsAllowedTemporaryEdit(now)
                && (!keep.IsAllowedTemporaryEdit(now) || (keep.CanTurnOnEditUntilUtc is not null
                    && (gone.CanTurnOnEditUntilUtc is null || gone.CanTurnOnEditUntilUtc > keep.CanTurnOnEditUntilUtc))))
            {
                (keep.CanTurnOnEdit, keep.CanTurnOnEditUntilUtc) = (true, gone.CanTurnOnEditUntilUtc);
            }

            if (gone.EditUntilUtc > keep.EditUntilUtc || keep.EditUntilUtc is null)
            {
                (keep.EditUntilUtc, keep.EditReason) = (gone.EditUntilUtc, gone.EditReason);
            }
        }
        else
        {
            ClearTemporaryEdit(keep);
        }
        if (gone.FirstSeenUtc < keep.FirstSeenUtc)
        {
            keep.FirstSeenUtc = gone.FirstSeenUtc;
        }

        if (gone.LastSeenUtc > keep.LastSeenUtc)
        {
            (keep.Name, keep.Email, keep.LastSeenUtc) = (gone.Name, gone.Email, gone.LastSeenUtc);
        }

        await repository.UpdateAsync(keep);
        foreach (var signIn in await repository.GetSignInsForUserAsync(gone.RowKey))
        {
            signIn.UserKey = keep.RowKey;
            await repository.UpdateSignInAsync(signIn);
            cache.Remove(SignInCacheKey(signIn.RowKey));
        }

        await repository.DeleteAsync(gone.RowKey);
        Forget(keep.RowKey);
        Forget(gone.RowKey);
        await auditRepository.AddAsync(FinanceAuditEntityType.AdminUser, keep.RowKey, "People Combined", actorName,
            details: $"{Describe(gone)} ({AdminAccessRole.DisplayName(gone.Role)}) is now part of {Describe(keep)} ({AdminAccessRole.DisplayName(keep.Role)}): {why}.");
        logger.LogInformation("Combined admin user {GoneKey} into {UserKey}: {Why}", gone.RowKey, keep.RowKey, why);
        return keep;
    }

    private static int Rank(AdminUserEntity row) => row.Role switch
    {
        AdminAccessRole.Editor => 2,
        AdminAccessRole.Viewer => 1,
        _ => 0,
    };

    /// <summary>Every address a person is known by, lower-cased: theirs and each of their accounts'.</summary>
    public static IReadOnlySet<string> EmailsOf(AdminUserEntity person, IEnumerable<AdminSignInEntity> signIns) =>
        signIns.Where(s => s.UserKey == person.RowKey)
            .SelectMany(s => new[] { s.Email, s.VerifiedEmail })
            .Append(person.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToLowerInvariant())
            .ToHashSet();

    /// <summary>The person with an account the provider vouched for this address on; one with access first.</summary>
    private async Task<AdminUserEntity?> FindPersonByVerifiedEmailAsync(string verified, string? exceptUserKey)
    {
        var people = new List<AdminUserEntity>();
        foreach (var userKey in (await repository.FindSignInsByVerifiedEmailAsync(verified)).Select(s => s.UserKey).Distinct())
        {
            if (userKey != exceptUserKey && await repository.GetAsync(userKey) is { } person)
            {
                people.Add(person);
            }
        }

        return people.FirstOrDefault(p => p.HasAccess) ?? people.FirstOrDefault();
    }

    /// <summary>Whose an account is: its recorded person, or a person keyed by it with no accounts recorded.</summary>
    private async Task<string?> GetUserKeyAsync(string signInKey)
    {
        if (cache.TryGetValue(SignInCacheKey(signInKey), out string? cached) && cached is not null)
        {
            return cached;
        }

        var userKey = (await repository.GetSignInAsync(signInKey))?.UserKey
            ?? (await repository.GetAsync(signInKey))?.RowKey;
        if (userKey is not null)
        {
            cache.Set(SignInCacheKey(signInKey), userKey, CacheFor);
        }

        return userKey;
    }

    public async Task SetRoleAsync(string userKey, string role, ClaimsPrincipal actor)
    {
        if (role is not (AdminAccessRole.Viewer or AdminAccessRole.Editor))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Only Read-only or Edit access can be given.");
        }

        var row = await GetForChangeAsync(userKey, actor);
        if (row.Role == role)
        {
            return;
        }

        await EnsureNotLastEditorAsync(row);
        var previous = row.Role;
        row.Role = role;
        if (role == AdminAccessRole.Editor)
        {
            // Standing Edit makes temporary Edit pointless; changing back to Read only doesn't bring it back.
            ClearTemporaryEdit(row);
        }
        row.RoleChangedBy = ActorName(actor);
        row.RoleChangedUtc = timeProvider.GetUtcNow();
        await repository.UpdateAsync(row);
        Forget(userKey);

        var action = previous == AdminAccessRole.None
            ? $"Access Granted ({AdminAccessRole.DisplayName(role)})"
            : $"Access Changed to {AdminAccessRole.DisplayName(role)}";
        await auditRepository.AddAsync(FinanceAuditEntityType.AdminUser, userKey, action, ActorName(actor),
            details: $"{Describe(row)}: {AdminAccessRole.DisplayName(previous)} → {AdminAccessRole.DisplayName(role)}.");

        if (role == AdminAccessRole.Editor)
        {
            await notificationService.EditorGrantedAsync(row, ActorName(actor));
        }
    }

    public async Task RemoveAsync(string userKey, ClaimsPrincipal actor)
    {
        var row = await GetForChangeAsync(userKey, actor);
        await EnsureNotLastEditorAsync(row);
        await repository.DeleteAsync(userKey);
        Forget(userKey);

        await auditRepository.AddAsync(FinanceAuditEntityType.AdminUser, userKey,
            row.HasAccess ? "Access Removed" : "Request Dismissed", ActorName(actor),
            details: row.HasAccess
                ? $"{Describe(row)} no longer has {AdminAccessRole.DisplayName(row.Role)} access."
                : $"Dismissed the access request from {Describe(row)}.");
    }

    public async Task SetCommitteeChairAsync(string userKey, bool isChair, ClaimsPrincipal actor)
    {
        EnsureEditor(actor);
        var row = await repository.GetAsync(userKey)
            ?? throw new InvalidOperationException("That person isn't on the list any more.");
        if (row.IsCommitteeChair == isChair)
        {
            return;
        }

        if (isChair)
        {
            foreach (var other in (await repository.GetAllAsync()).Where(u => u.IsCommitteeChair && u.RowKey != userKey))
            {
                other.IsCommitteeChair = false;
                await repository.UpdateAsync(other);
                Forget(other.RowKey);
            }
        }

        row.IsCommitteeChair = isChair;
        await repository.UpdateAsync(row);
        Forget(userKey);
        await auditRepository.AddAsync(FinanceAuditEntityType.AdminUser, userKey,
            isChair ? "Marked Committee Chair" : "Committee Chair Cleared", ActorName(actor), details: Describe(row));
    }

    public async Task AllowTemporaryEditAsync(string userKey, DateOnly lastDay, ClaimsPrincipal actor)
    {
        var row = await GetRowToAllowAsync(userKey, actor);
        if (row.Role != AdminAccessRole.Viewer)
        {
            throw new InvalidOperationException("Only people with Read-only access can be allowed temporary Edit access.");
        }

        var today = Today();
        if (lastDay < today)
        {
            throw new InvalidOperationException("Pick today or a later day for temporary Edit to end.");
        }

        if (lastDay > today.AddDays(MaxTemporaryEditAllowanceDays))
        {
            throw new InvalidOperationException("Temporary Edit can be allowed for up to a year at a time.");
        }

        var until = EndOfDay(lastDay);
        var now = timeProvider.GetUtcNow();
        var change = row.IsAllowedTemporaryEdit(now) ? "Temporary Edit End Date Changed" : "Temporary Edit Allowed";
        if (row.IsAllowedTemporaryEdit(now) && row.CanTurnOnEditUntilUtc == until)
        {
            return;
        }

        row.CanTurnOnEdit = true;
        row.CanTurnOnEditUntilUtc = until;
        // Edit they have on can't outlast the permission it came from.
        if (row.EditUntilUtc > until && !row.IsGlobalAdmin)
        {
            row.EditUntilUtc = until;
        }

        await repository.UpdateAsync(row);
        Forget(userKey);
        await auditRepository.AddAsync(FinanceAuditEntityType.AdminUser, userKey, change, ActorName(actor),
            details: $"{Describe(row)} can turn on Edit access for a few hours when they need it, through {lastDay:MMM d, yyyy}.");
    }

    public async Task StopAllowingTemporaryEditAsync(string userKey, ClaimsPrincipal actor)
    {
        var row = await GetRowToAllowAsync(userKey, actor);
        if (!row.CanTurnOnEdit)
        {
            return;
        }

        var wasOn = row.IsTemporaryEditOn(timeProvider.GetUtcNow());
        ClearTemporaryEdit(row);
        await repository.UpdateAsync(row);
        Forget(userKey);
        await auditRepository.AddAsync(FinanceAuditEntityType.AdminUser, userKey, "Temporary Edit Disallowed", ActorName(actor),
            details: $"{Describe(row)} can no longer turn on Edit access{(wasOn ? "; the temporary Edit they had on was ended" : "")}.");
    }

    /// <summary>The checks shared by allowing and stopping temporary Edit.</summary>
    private async Task<AdminUserEntity> GetRowToAllowAsync(string userKey, ClaimsPrincipal actor)
    {
        // A standing Editor or the Global admin -- not someone who is only on temporary Edit.
        if (!actor.CanAllowTemporaryEdit())
        {
            throw new InvalidOperationException("Only an Editor or the Global admin can allow temporary Edit access.");
        }

        if (string.Equals(await GetUserKeyAsync(AdminIdentity.Key(actor)), userKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("You can't change your own access. Ask another Editor.");
        }

        return await repository.GetAsync(userKey)
            ?? throw new InvalidOperationException("That person isn't on the list any more.");
    }

    /// <summary>Today's date where the troop is (the app's local time zone, Pacific).</summary>
    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone).DateTime);

    /// <summary>The moment <paramref name="day"/> ends (midnight after it) in the app's local time zone.</summary>
    private DateTimeOffset EndOfDay(DateOnly day)
    {
        var midnight = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, timeProvider.LocalTimeZone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    /// <summary>How far ahead an Editor can set the last day of temporary Edit.</summary>
    public const int MaxTemporaryEditAllowanceDays = 366;

    public async Task StartTemporaryEditAsync(ClaimsPrincipal actor, TimeSpan duration, string reason)
    {
        if (!TemporaryEditDurations.Contains(duration))
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Temporary Edit lasts 1, 2, 4 or 8 hours.");
        }

        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw new InvalidOperationException("Say why you need Edit access.");
        }

        if (reason.Length > MaxEditReasonLength)
        {
            throw new InvalidOperationException($"Keep the reason to {MaxEditReasonLength} characters or fewer.");
        }

        var now = timeProvider.GetUtcNow();
        var row = await GetOwnRowAsync(actor);
        if (row is null || !row.CanElevate(now))
        {
            throw new InvalidOperationException("You aren't allowed to turn on Edit access. Ask an Editor.");
        }

        row.EditUntilUtc = now + duration;
        // Not past the last day an Editor allowed (a Global admin's permission has no end).
        var cutShort = !row.IsGlobalAdmin && row.CanTurnOnEditUntilUtc < row.EditUntilUtc;
        if (cutShort)
        {
            row.EditUntilUtc = row.CanTurnOnEditUntilUtc;
        }

        row.EditReason = reason;
        await repository.UpdateAsync(row);
        Forget(row.RowKey);
        await auditRepository.AddAsync(FinanceAuditEntityType.AdminUser, row.RowKey, "Temporary Edit Started", ActorName(actor),
            details: $"{Describe(row)} turned on Edit access for {Hours(duration)}{(cutShort ? " (cut short where their permission ends)" : "")}, until {row.EditUntilUtc:u}: {reason}");
        logger.LogWarning("{UserKey} turned on temporary Edit access until {Until}", row.RowKey, row.EditUntilUtc);
        await notificationService.TemporaryEditStartedAsync(row, row.EditUntilUtc.Value, reason);
    }

    /// <summary>
    /// Keeps <see cref="AdminUserEntity.IsGlobalAdmin"/> in step with Auth:GlobalAdmins: set when
    /// any of the person's accounts has a listed address the provider vouched for, cleared when
    /// none does. A Global admin with no access yet gets Read only.
    /// </summary>
    private async Task<AdminUserEntity> SyncGlobalAdminAsync(AdminUserEntity row, DateTimeOffset now)
    {
        var listed = ConfiguredAddresses(configuration, GlobalAdminsKey);
        var isGlobalAdmin = listed.Count > 0
            && (await repository.GetSignInsForUserAsync(row.RowKey))
                .Any(s => listed.Contains(s.VerifiedEmail, StringComparer.OrdinalIgnoreCase));
        var giveReadOnly = isGlobalAdmin && row.Role == AdminAccessRole.None;
        if (row.IsGlobalAdmin == isGlobalAdmin && !giveReadOnly)
        {
            return row;
        }

        row.IsGlobalAdmin = isGlobalAdmin;
        if (giveReadOnly)
        {
            row.Role = AdminAccessRole.Viewer;
            row.RoleChangedBy = GlobalAdminsKey;
            row.RoleChangedUtc = now;
        }

        try
        {
            await repository.UpdateAsync(row);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Changed at the same moment; the next sign-in tries again.
            return await repository.GetAsync(row.RowKey) ?? row;
        }

        await auditRepository.AddAsync(FinanceAuditEntityType.AdminUser, row.RowKey,
            isGlobalAdmin ? "Global Admin Set" : "Global Admin Cleared", GlobalAdminsKey,
            details: isGlobalAdmin
                ? $"{Describe(row)} is a Global admin, from the {GlobalAdminsKey} setting{(giveReadOnly ? ", and was given Read-only access" : "")}."
                : $"{Describe(row)} is no longer listed in the {GlobalAdminsKey} setting.");
        logger.LogWarning("{UserKey} Global admin: {IsGlobalAdmin}, from {Setting}", row.RowKey, isGlobalAdmin, GlobalAdminsKey);
        return row;
    }

    public async Task EndTemporaryEditAsync(ClaimsPrincipal actor)
    {
        var row = await GetOwnRowAsync(actor);
        if (row is null || !row.IsTemporaryEditOn(timeProvider.GetUtcNow()))
        {
            return;
        }

        row.EditUntilUtc = null;
        row.EditReason = string.Empty;
        await repository.UpdateAsync(row);
        Forget(row.RowKey);
        await auditRepository.AddAsync(FinanceAuditEntityType.AdminUser, row.RowKey, "Temporary Edit Ended Early", ActorName(actor),
            details: $"{Describe(row)} turned off Edit access before it ran out.");
    }

    /// <summary>How long temporary Edit can be turned on for.</summary>
    public static readonly IReadOnlyList<TimeSpan> TemporaryEditDurations =
        [TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(4), TimeSpan.FromHours(8)];

    public const int MaxEditReasonLength = 500;

    private static string Hours(TimeSpan duration) => duration.TotalHours == 1 ? "1 hour" : $"{duration.TotalHours:0} hours";

    private static void ClearTemporaryEdit(AdminUserEntity row)
    {
        row.CanTurnOnEdit = false;
        row.CanTurnOnEditUntilUtc = null;
        row.EditUntilUtc = null;
        row.EditReason = string.Empty;
    }

    /// <summary>The signed-in person's own row, fresh from the database (never the cache).</summary>
    private async Task<AdminUserEntity?> GetOwnRowAsync(ClaimsPrincipal actor) =>
        await GetUserKeyAsync(AdminIdentity.Key(actor)) is { } userKey ? await repository.GetAsync(userKey) : null;

    /// <summary>
    /// Only while nobody has Edit access, and only for an address the provider vouches for: once
    /// the first Editor exists, an outside sign-in claiming a listed address gets nothing.
    /// </summary>
    private async Task<bool> IsBootstrapEditorAsync(ClaimsPrincipal user)
    {
        var verified = AdminIdentity.VerifiedEmail(user);
        if (string.IsNullOrWhiteSpace(verified)
            || !ConfiguredAddresses(configuration, BootstrapEditorsKey).Contains(verified, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return await repository.CountWithRoleAsync(AdminAccessRole.Editor) == 0;
    }

    private async Task<AdminUserEntity> GetForChangeAsync(string userKey, ClaimsPrincipal actor)
    {
        EnsureEditor(actor);
        if (string.Equals(await GetUserKeyAsync(AdminIdentity.Key(actor)), userKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("You can't change your own access. Ask another Editor.");
        }

        return await repository.GetAsync(userKey)
            ?? throw new InvalidOperationException("That person isn't on the list any more.");
    }

    private async Task EnsureNotLastEditorAsync(AdminUserEntity row)
    {
        if (row.Role == AdminAccessRole.Editor && await repository.CountWithRoleAsync(AdminAccessRole.Editor) <= 1)
        {
            throw new InvalidOperationException($"{row.Name} is the only Editor. Give someone else Edit access first.");
        }
    }

    /// <summary>Temporary Edit is for treasurer work, not for handing out lasting access.</summary>
    private static void EnsureEditor(ClaimsPrincipal actor)
    {
        if (!actor.IsStandingTroopFinanceWriter())
        {
            throw new InvalidOperationException(actor.IsTroopFinanceWriter()
                ? "Temporary Edit access can't change who has access. Ask an Editor."
                : "Only Editors can change who has access.");
        }
    }

    private async Task<AdminUserEntity> TryUpdateAsync(AdminUserEntity row)
    {
        try
        {
            // The new row version is read back into the entity, so it stays current for later updates.
            await repository.UpdateAsync(row);
            return row;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Changed (or removed) at the same moment -- e.g. an Editor granting access. Their
            // change wins; last-seen can wait for the next refresh.
            return await repository.GetAsync(row.RowKey) ?? row;
        }
    }

    private async Task TryUpdateSignInAsync(AdminSignInEntity signIn)
    {
        try
        {
            await repository.UpdateSignInAsync(signIn);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Moved to another person or removed at the same moment; theirs wins, last-seen can wait.
        }
    }

    private void Forget(string userKey) => cache.Remove(CacheKey(userKey));

    private static string CacheKey(string userKey) => $"AdminUser:{userKey}";

    /// <summary>Which person an account belongs to. Role changes forget the person's row
    /// (<see cref="CacheKey"/>), not this.</summary>
    private static string SignInCacheKey(string signInKey) => $"AdminSignIn:{signInKey}";

    private static string NormalizedVerifiedEmail(ClaimsPrincipal user) =>
        AdminIdentity.VerifiedEmail(user)?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string ActorName(ClaimsPrincipal actor) => actor.Identity?.Name ?? "unknown";

    private static string Describe(AdminUserEntity row) => Describe(row.Name, row.Email);

    private static string Describe(string name, string email) =>
        string.IsNullOrWhiteSpace(email) ? name : $"{name} ({email})";

    /// <summary>A list of addresses from configuration: a JSON array, or one comma/semicolon-separated string.</summary>
    public static IReadOnlyList<string> ConfiguredAddresses(IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);
        var values = section.Value is { } single
            ? [single]
            : section.GetChildren().Select(c => c.Value ?? string.Empty);
        return values
            .SelectMany(v => v.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
