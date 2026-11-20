using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BsaTroopFinances.Tests;

/// <summary>The real ReimbursementService with faked storage: what a receipt has to be, and each step
/// of the review (approve, reject, reopen, pay, change the amount, delete) with its audit entry and email.</summary>
public class ReimbursementServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 18, 0, 0, TimeSpan.Zero);

    private readonly IReimbursementRequestRepository _requests = Substitute.For<IReimbursementRequestRepository>();
    private readonly IReceiptStorageService _receipts = Substitute.For<IReceiptStorageService>();
    private readonly IFinanceAuditRepository _audit = Substitute.For<IFinanceAuditRepository>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly ReimbursementService _service;

    public ReimbursementServiceTests()
    {
        _service = new ReimbursementService(_requests, _receipts, _audit, _notifications, new FixedClock(Now), new PassThroughUnitOfWork());
        _receipts.UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>())
            .Returns(c => $"receipts/{c.ArgAt<string>(0)}/{c.ArgAt<string>(1)}");
    }

    private Task<ReimbursementRequestEntity> SubmitAsync(
        decimal amount = 42.50m, string contentType = "image/png", long size = 2048, string? onBehalfBy = null) =>
        _service.SubmitAsync("maria", "Maria Rivera", "maria@example.com", "alex", "Alex Rivera", amount, "Campout groceries",
            new MemoryStream([1, 2, 3]), contentType, "receipt.png", size, onBehalfBy);

    private ReimbursementRequestEntity Stored(string status, decimal amount = 42.50m)
    {
        var request = new ReimbursementRequestEntity
        {
            PartitionKey = ReimbursementRequestEntity.BuildPartitionKey("maria"), RowKey = "req1", RequestId = "req1",
            SubmitterGuardianId = "maria", SubmitterName = "Maria Rivera", SubmitterEmail = "maria@example.com",
            AmountRequested = amount, Description = "Campout groceries", ReceiptBlobPath = "receipts/maria/req1",
            ReceiptContentType = "image/png", StatusValue = status,
        };
        _requests.GetByIdAsync("maria", "req1").Returns(request);
        return request;
    }

    // ---- Submitting

    [Fact]
    public async Task A_submitted_receipt_is_saved_pending_audited_and_emailed()
    {
        var request = await SubmitAsync();

        Assert.Equal((ReimbursementStatus.Pending, 42.50m, Now), (request.StatusValue, request.AmountRequested, request.SubmittedUtc));
        Assert.Equal($"receipts/maria/{request.RequestId}", request.ReceiptBlobPath);
        Assert.Equal(("alex", "Alex Rivera", "receipt.png", 2048L), (request.ScoutId, request.ScoutName, request.ReceiptOriginalFileName, request.ReceiptSizeBytes));
        await _requests.Received(1).AddAsync(request);
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Reimbursement, request.RequestId, "Submitted", "Maria Rivera",
            Arg.Any<string?>(), Arg.Is<string?>(d => d!.Contains("Campout groceries")));
        await _notifications.Received(1).ReceiptSubmittedAsync(request);
    }

    [Fact]
    public async Task A_receipt_entered_for_someone_else_is_audited_under_whoever_entered_it()
    {
        var request = await SubmitAsync(onBehalfBy: "Pat Treasurer");

        Assert.Equal("Pat Treasurer", request.SubmittedOnBehalfBy);
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Reimbursement, request.RequestId, "Submitted on behalf of Maria Rivera", "Pat Treasurer",
            Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("IMAGE/PNG")]
    [InlineData("application/pdf")]
    public async Task Jpeg_png_and_pdf_receipts_are_accepted(string contentType) =>
        Assert.Equal(contentType, (await SubmitAsync(contentType: contentType)).ReceiptContentType);

    [Theory]
    [InlineData(0, "image/png", 2048)]
    [InlineData(-5, "image/png", 2048)]
    [InlineData(10, "image/gif", 2048)]
    [InlineData(10, "text/html", 2048)]
    [InlineData(10, "image/png", 10 * 1024 * 1024 + 1)]
    public async Task A_bad_amount_file_type_or_oversized_file_is_refused_before_anything_is_stored(decimal amount, string contentType, long size)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => SubmitAsync(amount, contentType, size));

        await _receipts.DidNotReceiveWithAnyArgs().UploadAsync(default!, default!, default!, default!);
        await _requests.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _notifications.DidNotReceiveWithAnyArgs().ReceiptSubmittedAsync(default!);
    }

    [Fact]
    public async Task A_file_of_exactly_ten_megabytes_is_allowed() =>
        Assert.Equal(10L * 1024 * 1024, (await SubmitAsync(size: 10 * 1024 * 1024)).ReceiptSizeBytes);

    [Fact]
    public async Task If_saving_fails_the_uploaded_receipt_is_removed_and_no_email_goes_out()
    {
        _requests.AddAsync(Arg.Any<ReimbursementRequestEntity>()).ThrowsAsync(new InvalidOperationException("database down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => SubmitAsync());

        await _receipts.Received(1).DeleteAsync(Arg.Is<string>(p => p.StartsWith("receipts/maria/")));
        await _notifications.DidNotReceiveWithAnyArgs().ReceiptSubmittedAsync(default!);
    }

    // ---- Reviewing

    [Fact]
    public async Task Approving_records_the_reviewer_and_tells_the_submitter()
    {
        var request = Stored(ReimbursementStatus.Pending);

        await _service.ApproveAsync("maria", "req1", "Pat Treasurer", "Looks right");

        Assert.Equal((ReimbursementStatus.Approved, "Pat Treasurer", "Looks right", (DateTimeOffset?)Now),
            (request.StatusValue, request.ReviewedBy, request.ReviewNotes, request.ReviewedUtc));
        await _requests.Received(1).UpdateAsync(request);
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Reimbursement, "req1", "Approved", "Pat Treasurer", "Looks right", Arg.Any<string?>());
        await _notifications.Received(1).ReceiptStatusChangedAsync(request);
    }

    [Fact]
    public async Task Rejecting_records_why_and_tells_the_submitter()
    {
        var request = Stored(ReimbursementStatus.Pending);

        await _service.RejectAsync("maria", "req1", "Pat Treasurer", "Receipt is unreadable");

        Assert.Equal((ReimbursementStatus.Rejected, "Receipt is unreadable"), (request.StatusValue, request.ReviewNotes));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Reimbursement, "req1", "Rejected", "Pat Treasurer", "Receipt is unreadable", Arg.Any<string?>());
        await _notifications.Received(1).ReceiptStatusChangedAsync(request);
    }

    [Fact]
    public async Task Reopening_a_rejected_request_clears_the_review_without_an_email()
    {
        var request = Stored(ReimbursementStatus.Rejected);
        request.ReviewedBy = "Pat Treasurer";
        request.ReviewNotes = "Wrong receipt";
        request.ReviewedUtc = Now.AddDays(-1);

        await _service.ReopenAsync("maria", "req1", "Pat Treasurer", "New receipt attached");

        Assert.Equal((ReimbursementStatus.Pending, null, null, null), (request.StatusValue, request.ReviewedBy, request.ReviewNotes, request.ReviewedUtc));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Reimbursement, "req1", "Reopened to Pending", "Pat Treasurer", "New receipt attached", Arg.Any<string?>());
        await _notifications.DidNotReceiveWithAnyArgs().ReceiptStatusChangedAsync(default!);
    }

    [Theory]
    [InlineData(ReimbursementStatus.Pending)]
    [InlineData(ReimbursementStatus.Approved)]
    [InlineData(ReimbursementStatus.Paid)]
    public async Task Only_a_rejected_request_can_be_reopened(string status)
    {
        Stored(status);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ReopenAsync("maria", "req1", "Pat Treasurer", null));

        await _requests.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    [Fact]
    public async Task Marking_paid_records_who_paid_and_how_and_tells_the_submitter()
    {
        var request = Stored(ReimbursementStatus.Approved);

        await _service.MarkPaidAsync("maria", "req1", "Pat Treasurer", "Check 1043");

        Assert.Equal((ReimbursementStatus.Paid, "Pat Treasurer", "Check 1043", (DateTimeOffset?)Now),
            (request.StatusValue, request.PaidBy, request.PaymentMethodNote, request.PaidUtc));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Reimbursement, "req1", "Marked Paid", "Pat Treasurer", "Check 1043", Arg.Any<string?>());
        await _notifications.Received(1).ReceiptStatusChangedAsync(request);
    }

    [Fact]
    public async Task Changing_the_amount_audits_the_old_and_new_amounts()
    {
        var request = Stored(ReimbursementStatus.Pending, amount: 42.50m);

        await _service.UpdateAmountAsync("maria", "req1", 40m, "Pat Treasurer");

        Assert.Equal(40m, request.AmountRequested);
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Reimbursement, "req1", "Amount Changed", "Pat Treasurer",
            Arg.Any<string?>(), $"{42.50m:C} → {40m:C}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task The_amount_cant_be_changed_to_zero_or_less(decimal amount)
    {
        var request = Stored(ReimbursementStatus.Pending, amount: 42.50m);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.UpdateAmountAsync("maria", "req1", amount, "Pat Treasurer"));

        Assert.Equal(42.50m, request.AmountRequested);
        await _requests.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    // ---- Deleting

    [Fact]
    public async Task A_pending_request_can_be_deleted_with_its_receipt_file()
    {
        var request = Stored(ReimbursementStatus.Pending);

        await _service.DeletePendingAsync("maria", "req1");

        await _requests.Received(1).DeleteAsync(request);
        await _receipts.Received(1).DeleteAsync("receipts/maria/req1");
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Reimbursement, "req1", "Deleted", "maria", Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Theory]
    [InlineData(ReimbursementStatus.Approved)]
    [InlineData(ReimbursementStatus.Rejected)]
    [InlineData(ReimbursementStatus.Paid)]
    public async Task A_reviewed_request_cant_be_deleted(string status)
    {
        Stored(status);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeletePendingAsync("maria", "req1"));

        await _requests.DidNotReceiveWithAnyArgs().DeleteAsync(default!);
        await _receipts.DidNotReceiveWithAnyArgs().DeleteAsync(default!);
    }

    [Fact]
    public async Task Every_step_on_a_request_that_isnt_there_says_so()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApproveAsync("maria", "gone", "Pat", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RejectAsync("maria", "gone", "Pat", "No"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ReopenAsync("maria", "gone", "Pat", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.MarkPaidAsync("maria", "gone", "Pat", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAmountAsync("maria", "gone", 5, "Pat"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeletePendingAsync("maria", "gone"));

        await _audit.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default!, default!, default, default);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
