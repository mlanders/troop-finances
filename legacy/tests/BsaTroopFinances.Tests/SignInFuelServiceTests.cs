using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>Drivers' fuel paybacks sent from the meeting sign-in app after a trip.</summary>
public class SignInFuelServiceTests
{
    private readonly InMemorySignInAppRepository _signIn = new();
    private readonly IReimbursementRequestRepository _requests = Substitute.For<IReimbursementRequestRepository>();
    private readonly IFinanceAuditRepository _audit = Substitute.For<IFinanceAuditRepository>();
    private readonly List<ReimbursementRequestEntity> _stored = [];

    private static readonly List<GuardianEntity> Guardians =
    [
        new() { RowKey = "pat@x", GuardianId = "g-pat", FullName = "Pat Lee", Email = "pat@x" },
        new() { RowKey = "kim@x", GuardianId = "g-kim", FullName = "Kim Lee", Email = "kim@x" },
    ];

    public SignInFuelServiceTests()
    {
        _requests.GetBySourcePrefixAsync(Arg.Any<string>())
            .Returns(c => _stored.Where(r => r.SourceRef!.StartsWith(c.Arg<string>(), StringComparison.Ordinal)).ToList());
        _requests.When(r => r.AddAsync(Arg.Any<ReimbursementRequestEntity>())).Do(c => _stored.Add(c.Arg<ReimbursementRequestEntity>()));
        _requests.When(r => r.DeleteAsync(Arg.Any<ReimbursementRequestEntity>())).Do(c => _stored.Remove(c.Arg<ReimbursementRequestEntity>()));
        _signIn.People["p-pat"] = new SignInPersonEntity { RowKey = "p-pat", FullName = "Pat Lee", PersonType = SignInPersonType.Adult, LinkedGuardianId = "g-pat" };
        _signIn.People["p-kim"] = new SignInPersonEntity { RowKey = "p-kim", FullName = "Kim Lee", PersonType = SignInPersonType.Adult };
    }

    private SignInFuelService Service()
    {
        var guardians = Substitute.For<IGuardianRepository>();
        guardians.GetByIdAsync(Arg.Any<string>()).Returns(c => Guardians.FirstOrDefault(g => g.GuardianId == c.Arg<string>()));
        return new SignInFuelService(_signIn, _requests, guardians, _audit, new PassThroughUnitOfWork(), TimeProvider.System);
    }

    private static FuelUpload Fuel(params FuelUploadLine[] lines) => new("Winter Campout", "Dec 4 – 6", "Leader", lines);

    private static FuelUploadLine Driver(string entryId = "e1", string? personId = "p-pat", decimal amount = 35) =>
        new(entryId, "Pat Lee", personId, amount, "140 mi round trip at $0.25 a mile.");

    [Fact]
    public async Task A_linked_driver_gets_a_pending_payback_with_no_receipt()
    {
        var status = Assert.Single(await Service().ReceiveFuelAsync("t1", Fuel(Driver())));

        var request = Assert.Single(_stored);
        Assert.Equal(("g-pat", 35m, ReimbursementStatus.Pending, "fuel:t1:e1", 35m), (request.SubmitterGuardianId, request.AmountRequested, request.StatusValue, request.SourceRef, request.SourceAmount));
        Assert.False(request.HasReceipt);
        Assert.True(request.IsFuelPayback);
        Assert.StartsWith("Fuel: Winter Campout (Dec 4 – 6). 140 mi", request.Description);
        Assert.Equal(new FuelLineStatusDto("e1", FuelPaybackStatus.Pending, 35, 35), status);
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Reimbursement, request.RequestId, "Fuel payback for Pat Lee", "Sign-in app (Leader)", Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Theory]
    [InlineData("p-kim")]   // on the roster, not linked to a guardian
    [InlineData(null)]      // typed in on the trip
    [InlineData("p-gone")]  // not on the roster here at all
    public async Task A_driver_who_cant_be_matched_to_a_guardian_gets_nothing_yet(string? personId)
    {
        var status = Assert.Single(await Service().ReceiveFuelAsync("t1", Fuel(Driver(personId: personId))));

        Assert.Equal(FuelPaybackStatus.NotLinked, status.Status);
        Assert.Empty(_stored);
    }

    [Fact]
    public async Task Sending_again_updates_a_pending_payback_at_the_sign_in_apps_amount()
    {
        await Service().ReceiveFuelAsync("t1", Fuel(Driver(amount: 35)));

        await Service().ReceiveFuelAsync("t1", Fuel(Driver(amount: 42)));

        var request = Assert.Single(_stored);
        Assert.Equal((42m, 42m), (request.AmountRequested, request.SourceAmount));
    }

    [Fact]
    public async Task An_amount_the_treasurer_changed_is_never_overwritten()
    {
        await Service().ReceiveFuelAsync("t1", Fuel(Driver(amount: 35)));
        _stored[0].AmountRequested = 50;   // the treasurer, from actual receipts

        var status = Assert.Single(await Service().ReceiveFuelAsync("t1", Fuel(Driver(amount: 42))));

        Assert.Equal(50, _stored[0].AmountRequested);
        Assert.Equal(new FuelLineStatusDto("e1", FuelPaybackStatus.Adjusted, 50, 35), status);
    }

    [Theory]
    [InlineData(ReimbursementStatus.Approved)]
    [InlineData(ReimbursementStatus.Paid)]
    [InlineData(ReimbursementStatus.Rejected)]
    public async Task A_payback_the_treasurer_has_dealt_with_is_left_alone(string status)
    {
        await Service().ReceiveFuelAsync("t1", Fuel(Driver(amount: 35)));
        _stored[0].StatusValue = status;

        await Service().ReceiveFuelAsync("t1", Fuel(Driver(amount: 42)));
        await Service().ReceiveFuelAsync("t1", Fuel());   // and even when they're no longer a driver

        Assert.Equal((35m, status), (Assert.Single(_stored).AmountRequested, _stored[0].StatusValue));
    }

    [Fact]
    public async Task Someone_no_longer_driving_loses_an_untouched_pending_payback_only()
    {
        await Service().ReceiveFuelAsync("t1", Fuel(Driver("e1"), Driver("e2")));
        _stored.Single(r => r.SourceRef == "fuel:t1:e2").AmountRequested = 20;   // adjusted: kept

        await Service().ReceiveFuelAsync("t1", Fuel());

        Assert.Equal(["fuel:t1:e2"], _stored.Select(r => r.SourceRef));
    }

    [Fact]
    public async Task A_driver_relinked_to_another_guardian_moves_to_them()
    {
        await Service().ReceiveFuelAsync("t1", Fuel(Driver()));
        _signIn.People["p-pat"].LinkedGuardianId = "g-kim";

        await Service().ReceiveFuelAsync("t1", Fuel(Driver()));

        Assert.Equal("g-kim", Assert.Single(_stored).SubmitterGuardianId);
    }

    [Fact]
    public async Task One_trips_paybacks_are_kept_apart_from_anothers()
    {
        await Service().ReceiveFuelAsync("t1", Fuel(Driver()));
        await Service().ReceiveFuelAsync("t2", Fuel(Driver()));

        await Service().ReceiveFuelAsync("t1", Fuel());

        Assert.Equal(["fuel:t2:e1"], _stored.Select(r => r.SourceRef));
        Assert.Single(await Service().GetFuelStatusAsync("t2"));
        Assert.Empty(await Service().GetFuelStatusAsync("t1"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(2500)]
    public async Task A_payback_must_be_a_sensible_amount(decimal amount) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Service().ReceiveFuelAsync("t1", Fuel(Driver(amount: amount))));

    [Fact]
    public async Task A_trip_needs_an_id_and_each_driver_an_entry_id()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().ReceiveFuelAsync(" ", Fuel(Driver())));
        await Assert.ThrowsAsync<ArgumentException>(() => Service().ReceiveFuelAsync("t1", Fuel(Driver(entryId: ""))));
    }
}
