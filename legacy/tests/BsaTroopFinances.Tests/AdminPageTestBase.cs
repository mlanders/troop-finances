using System.Security.Claims;
using Bunit;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BsaTroopFinances.Tests;

public enum AdminRole
{
    Writer,
    Reader,
}

/// <summary>
/// Base for admin page tests: renders pages in memory with bUnit, signed in as a fake admin with
/// the Editor (Writer) or Viewer (Reader) role claim -- the same claim AdminRoleClaimsTransformation
/// adds from the AdminUsers table. Each test
/// registers NSubstitute fakes for whatever repositories and services the page injects.
/// </summary>
public abstract class AdminPageTestBase : BunitContext
{
    protected AdminPageTestBase()
    {
        // Tests needing a fixed clock register their own; the last registration wins.
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton(TestTroop.Info);
    }

    protected void SignInAs(AdminRole role, string name = "Test Admin", params Claim[] extraClaims)
    {
        var auth = AddAuthorization();
        auth.SetAuthorized(name);
        auth.SetClaims([new Claim(TroopFinanceClaimsExtensions.RoleClaimType, role == AdminRole.Writer
            ? AdminAccessRole.Editor
            : AdminAccessRole.Viewer), .. extraClaims]);
    }

    protected T Fake<T>() where T : class
    {
        var fake = NSubstitute.Substitute.For<T>();
        Services.AddSingleton(fake);
        return fake;
    }

    protected static InvoiceEntity Invoice(
        string id = "inv1",
        string scoutId = "scout1",
        string scoutName = "Alex Rivera",
        string description = "Summer camp deposit",
        decimal total = 275,
        decimal applied = 100,
        decimal due = 175,
        string status = InvoiceStatus.Outstanding) => new()
    {
        PartitionKey = scoutId,
        RowKey = id,
        InvoiceId = id,
        ScoutId = scoutId,
        ScoutName = scoutName,
        Description = description,
        TotalAmount = total,
        AmountAppliedFromAccount = applied,
        AmountDueFromFamily = due,
        StatusValue = status,
        IssuedUtc = new DateTimeOffset(2026, 9, 1, 17, 0, 0, TimeSpan.Zero),
        DueDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
    };
}
