using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;

namespace BsaTroopFinances.Services;

// ---- What the meeting sign-in app sends and gets back for drivers' fuel (see SignInAppApi). ----

/// <summary>After a trip: each driver who went, with the fuel the sign-in app works out they're owed.</summary>
public sealed record FuelUpload(string Title, string WhenText, string SentBy, IReadOnlyList<FuelUploadLine> Lines);

/// <summary>One driver. PersonId is the sign-in roster id (empty for a name typed in, which can't be paid this way).</summary>
public sealed record FuelUploadLine(string EntryId, string Name, string? PersonId, decimal Amount, string Description);

/// <summary>
/// Where one driver's fuel payback stands. Status is a FuelPaybackStatus; Amount is what the
/// treasurer will pay (they may have changed it), Estimate what the sign-in app last asked for.
/// </summary>
public sealed record FuelLineStatusDto(string EntryId, string Status, decimal Amount, decimal Estimate);

public static class FuelPaybackStatus
{
    /// <summary>The driver isn't linked to a guardian here, so nothing could be set up for them.</summary>
    public const string NotLinked = "NotLinked";
    public const string Pending = ReimbursementStatus.Pending;
    public const string Approved = ReimbursementStatus.Approved;
    public const string Rejected = ReimbursementStatus.Rejected;
    public const string Paid = ReimbursementStatus.Paid;

    /// <summary>Pending, but the treasurer changed the amount: the sign-in app's estimate no longer applies.</summary>
    public const string Adjusted = "Adjusted";
}

/// <summary>
/// Drivers' fuel paybacks from the meeting sign-in app. After a trip, that app sends each driver
/// who went and the fuel worked out from the miles. Each becomes a pending reimbursement for the
/// driver's linked guardian, with no receipt, in the treasurer's usual queue: the treasurer can
/// change the amount, and approves and pays each one as usual. Sending again updates only those
/// still pending at the sign-in app's own amount; anything the treasurer changed, approved, paid
/// or rejected is left alone.
/// </summary>
public sealed class SignInFuelService(
    ISignInAppRepository signIn,
    IReimbursementRequestRepository requests,
    IGuardianRepository guardians,
    IFinanceAuditRepository audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>Sets up or updates the trip's fuel paybacks, and says where each driver stands.</summary>
    public async Task<IReadOnlyList<FuelLineStatusDto>> ReceiveFuelAsync(string signUpId, FuelUpload upload)
    {
        if (string.IsNullOrWhiteSpace(signUpId) || upload.Lines.Any(l => string.IsNullOrWhiteSpace(l.EntryId) || l.Amount <= 0 || l.Amount > 2000))
        {
            throw new ArgumentException("Fuel needs a trip id, and each driver an entry id and an amount over $0 and up to $2,000.");
        }

        var people = (await signIn.GetPeopleAsync()).ToDictionary(p => p.PersonId);
        var existing = (await requests.GetBySourcePrefixAsync(Prefix(signUpId))).ToDictionary(r => r.SourceRef!);
        var actor = $"{SignInAppService.SignInAppActor} ({Trim(upload.SentBy, 150)})";
        var results = new List<FuelLineStatusDto>();
        var sent = new HashSet<string>();

        await unitOfWork.InTransactionAsync(async () =>
        {
            foreach (var line in upload.Lines.Where(l => sent.Add(l.EntryId)))
            {
                var sourceRef = ReimbursementRequestEntity.FuelSourceRef(signUpId, line.EntryId);
                var amount = Math.Round(line.Amount, 2);
                var description = Trim($"Fuel: {upload.Title} ({upload.WhenText}). {line.Description}", 2000);
                var current = existing.GetValueOrDefault(sourceRef);
                var guardian = people.GetValueOrDefault(line.PersonId ?? string.Empty) is { IsAdult: true, LinkedGuardianId: { } id }
                    ? await guardians.GetByIdAsync(id)
                    : null;

                if (current is not null && current.StatusValue != ReimbursementStatus.Pending)
                {
                    results.Add(Status(line.EntryId, current));
                    continue;
                }

                if (current is not null && IsAdjusted(current))
                {
                    results.Add(Status(line.EntryId, current));
                    continue;
                }

                if (current is not null && (guardian is null || guardian.GuardianId != current.SubmitterGuardianId))
                {
                    // Relinked to someone else, or unlinked: this one goes, and a new one is made below if they can be paid.
                    await requests.DeleteAsync(current);
                    await audit.AddAsync(FinanceAuditEntityType.Reimbursement, current.RequestId, "Fuel payback withdrawn", actor,
                        details: $"{current.SubmitterName} is no longer the guardian for {line.Name} in the sign-in app.");
                    current = null;
                }

                if (guardian is null)
                {
                    results.Add(new(line.EntryId, FuelPaybackStatus.NotLinked, 0, amount));
                    continue;
                }

                if (current is null)
                {
                    var requestId = ReimbursementRequestEntity.NewRowKey();
                    current = new ReimbursementRequestEntity
                    {
                        PartitionKey = ReimbursementRequestEntity.BuildPartitionKey(guardian.GuardianId),
                        RowKey = requestId,
                        RequestId = requestId,
                        SubmitterGuardianId = guardian.GuardianId,
                        SubmitterName = guardian.FullName,
                        SubmitterEmail = guardian.Email,
                        SubmittedOnBehalfBy = actor,
                        AmountRequested = amount,
                        Description = description,
                        StatusValue = ReimbursementStatus.Pending,
                        SubmittedUtc = timeProvider.GetUtcNow(),
                        SourceRef = sourceRef,
                        SourceAmount = amount,
                    };
                    await requests.AddAsync(current);
                    await audit.AddAsync(FinanceAuditEntityType.Reimbursement, requestId, $"Fuel payback for {guardian.FullName}", actor,
                        details: $"{amount:C} — {description}");
                }
                else if (current.AmountRequested != amount || current.Description != description)
                {
                    var before = current.AmountRequested;
                    current.AmountRequested = amount;
                    current.SourceAmount = amount;
                    current.Description = description;
                    await requests.UpdateAsync(current);
                    await audit.AddAsync(FinanceAuditEntityType.Reimbursement, current.RequestId, "Fuel payback updated", actor,
                        details: $"{before:C} → {amount:C}");
                }

                results.Add(Status(line.EntryId, current));
            }

            // Drivers no longer on the list: an untouched pending payback goes; anything else stays for the treasurer.
            foreach (var gone in existing.Values.Where(r => !sent.Contains(EntryId(r)) && r.StatusValue == ReimbursementStatus.Pending && !IsAdjusted(r)))
            {
                await requests.DeleteAsync(gone);
                await audit.AddAsync(FinanceAuditEntityType.Reimbursement, gone.RequestId, "Fuel payback withdrawn", actor,
                    details: $"{gone.SubmitterName} is no longer a driver on {upload.Title} in the sign-in app.");
            }
        });

        return results;
    }

    /// <summary>Where each driver with a fuel payback stands (those never set up aren't listed).</summary>
    public async Task<IReadOnlyList<FuelLineStatusDto>> GetFuelStatusAsync(string signUpId) =>
        (await requests.GetBySourcePrefixAsync(Prefix(signUpId))).Select(r => Status(EntryId(r), r)).ToList();

    private static FuelLineStatusDto Status(string entryId, ReimbursementRequestEntity request) =>
        new(entryId,
            request.StatusValue == ReimbursementStatus.Pending && IsAdjusted(request) ? FuelPaybackStatus.Adjusted : request.StatusValue,
            request.AmountRequested, request.SourceAmount ?? request.AmountRequested);

    private static bool IsAdjusted(ReimbursementRequestEntity request) =>
        request.SourceAmount is { } asked && asked != request.AmountRequested;

    private static string Prefix(string signUpId) => ReimbursementRequestEntity.FuelSourceRef(signUpId, string.Empty);

    private static string EntryId(ReimbursementRequestEntity request) => request.SourceRef![(request.SourceRef!.LastIndexOf(':') + 1)..];

    private static string Trim(string? text, int max)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
