using System.Text;
using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Services;

// ---- What the meeting sign-in app sends and gets back (JSON, see SignInAppApi). ----

/// <summary>One person on the sign-in app's roster.</summary>
public sealed record SignInRosterPerson(string PersonId, string Name, string PersonType, bool IsActive);

/// <summary>A trip and everyone on it, with what each owes.</summary>
public sealed record TripUpload(
    string Title, string WhenText, DateOnly StartDate, DateOnly EndDate, DateOnly? DueDate, string SentBy,
    IReadOnlyList<TripUploadLine> Lines);

/// <summary>One person on a trip. PersonId is empty for a name typed in on the trip.</summary>
public sealed record TripUploadLine(string EntryId, string Name, string? PersonId, string PersonType, decimal Amount, string CostText);

/// <summary>A payment taken in the sign-in app. PaymentId is its own id there, so a retry is recognised.</summary>
public sealed record TripPaymentUpload(string PaymentId, string EntryId, decimal Amount, string Method, string? Reference, string RecordedBy);

/// <summary>
/// Where one person on a trip stands here. Status is one of TripLineStatus's values; Matched is
/// whether the treasurer knows who they are (linked, matched by hand, or not billed).
/// </summary>
public sealed record TripLineStatusDto(string EntryId, bool Matched, string Status, decimal Total, decimal Due, decimal Paid);

public static class TripLineStatus
{
    public const string NotInvoiced = "NotInvoiced";
    public const string Outstanding = "Outstanding";
    public const string Paid = "Paid";
    public const string NotBilled = "NotBilled";
    public const string NoCharge = "NoCharge";
}

public enum TripPaymentOutcome { Recorded, AlreadyRecorded, NotFound, NotInvoiced, Rejected }

public sealed record TripPaymentResult(TripPaymentOutcome Outcome, string Message);

// ---- The treasurer's review of a trip. ----

/// <summary>What applying a trip would do to one line.</summary>
public enum TripLineAction
{
    /// <summary>Its invoice matches; nothing to do.</summary>
    UpToDate,
    /// <summary>Nothing to pay for the trip.</summary>
    NoCharge,
    /// <summary>Marked as never billed on the links page.</summary>
    NotBilled,
    /// <summary>Who they are isn't known yet: link them, or match this line by hand.</summary>
    NeedsMatch,
    Issue,
    /// <summary>Dropped out, with nothing paid: the invoice is voided.</summary>
    Void,
    /// <summary>The cost changed, with nothing paid: voided and issued again at the new amount.</summary>
    Reissue,
    /// <summary>A change after money was paid: left for the treasurer to sort out by hand.</summary>
    NeedsAttention,
}

public sealed record TripReviewLine(
    TripImportLineEntity Line, string? MatchId, string? MatchName, bool MatchIsManual, InvoiceEntity? Invoice,
    TripLineAction Action, string Note)
{
    /// <summary>What's been paid on its current invoice, family and scout funds together.</summary>
    public decimal PaidSoFar => Invoice is null ? 0 : Invoice.TotalAmount - Invoice.AmountDueFromFamily;

    public bool WillChange => Action is TripLineAction.Issue or TripLineAction.Void or TripLineAction.Reissue;
}

public sealed record TripReview(TripImportEntity Trip, IReadOnlyList<TripReviewLine> Lines)
{
    public int Changes => Lines.Count(l => l.WillChange);
    public int Waiting => Lines.Count(l => l.Action is TripLineAction.NeedsMatch or TripLineAction.NeedsAttention);
}

/// <summary>A sign-in person and the finance scout or guardian their name suggests, if just one does.</summary>
public sealed record SignInPersonRow(SignInPersonEntity Person, string? LinkedName, string? SuggestedId, string? SuggestedName);

/// <summary>
/// The meeting sign-in app's side of the books. That app sends its roster (so each person can be
/// linked once to a finance scout or guardian), its trips with what each person owes (reviewed
/// here before any invoice is issued), and payments its leaders and scribes took at meetings
/// (recorded against those invoices). Every invoice and payment goes through ScoutAccountService,
/// the same as one made on any other page, so the ledger, audit trail and emails all work the
/// usual way.
/// </summary>
public sealed class SignInAppService(
    ISignInAppRepository repository,
    IScoutRepository scouts,
    IGuardianRepository guardians,
    IInvoiceRepository invoices,
    IScoutAccountService accounts,
    IFinanceAuditRepository audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const string SignInAppActor = "Sign-in app";

    // ---- Roster ----

    /// <summary>
    /// Brings the sign-in roster up to date: adds and renames people, and marks anyone no longer
    /// sent as gone. A person who changes from Scout to Adult (or back) loses their link, since a
    /// scout's link can't stand for an adult.
    /// </summary>
    public async Task ReceiveRosterAsync(IReadOnlyList<SignInRosterPerson> roster)
    {
        var now = timeProvider.GetUtcNow();
        var existing = (await repository.GetPeopleAsync()).ToDictionary(p => p.PersonId);
        var sent = new HashSet<string>();
        foreach (var incoming in roster)
        {
            if (string.IsNullOrWhiteSpace(incoming.PersonId) || !SignInPersonType.IsValid(incoming.PersonType) || !sent.Add(incoming.PersonId))
            {
                continue;
            }

            var person = existing.GetValueOrDefault(incoming.PersonId)
                ?? new SignInPersonEntity { PartitionKey = SignInPersonEntity.PartitionKeyValue, RowKey = incoming.PersonId.Trim() };
            if (person.PersonType != incoming.PersonType && person.LastSyncedUtc != default)
            {
                person.LinkedScoutId = null;
                person.LinkedGuardianId = null;
            }

            person.FullName = Trim(incoming.Name, 200);
            person.PersonType = incoming.PersonType;
            person.IsActiveInSignIn = incoming.IsActive;
            person.LastSyncedUtc = now;
            await repository.SavePersonAsync(person);
        }

        foreach (var gone in existing.Values.Where(p => p.IsActiveInSignIn && !sent.Contains(p.PersonId)))
        {
            gone.IsActiveInSignIn = false;
            gone.LastSyncedUtc = now;
            await repository.SavePersonAsync(gone);
        }
    }

    /// <summary>Everyone the sign-in app sent, with who they're linked to and a suggestion for those who aren't.</summary>
    public async Task<IReadOnlyList<SignInPersonRow>> GetPeopleAsync()
    {
        var people = await repository.GetPeopleAsync();
        var allScouts = await scouts.GetAllAsync();
        var allGuardians = await guardians.GetAllAsync();
        var scoutNames = allScouts.ToDictionary(s => s.ScoutId, s => s.FullName);
        var guardianNames = allGuardians.ToDictionary(g => g.GuardianId, g => g.FullName);
        var linkedScouts = people.Where(p => p.LinkedScoutId is not null).Select(p => p.LinkedScoutId!).ToHashSet();
        var linkedGuardians = people.Where(p => p.LinkedGuardianId is not null).Select(p => p.LinkedGuardianId!).ToHashSet();
        var scoutCandidates = allScouts.Where(s => s.IsActive).Select(s => (s.ScoutId, s.FullName)).ToList();
        var guardianCandidates = allGuardians.Where(g => g.IsActive).Select(g => (g.GuardianId, g.FullName)).ToList();

        return people.Select(p =>
        {
            var names = p.IsAdult ? guardianNames : scoutNames;
            var linkedName = p.LinkedId is { } id ? names.GetValueOrDefault(id, "(removed)") : null;
            var suggested = p.IsLinked || p.NotBilled ? null
                : NameMatch.Suggest(p.FullName, p.IsAdult ? guardianCandidates : scoutCandidates, p.IsAdult ? linkedGuardians : linkedScouts);
            return new SignInPersonRow(p, linkedName, suggested, suggested is null ? null : names[suggested]);
        }).ToList();
    }

    /// <summary>
    /// Links a sign-in person to a finance scout (a Scout) or guardian (an Adult), or with a null
    /// id removes the link. notBilled marks them as never charged instead. One finance person can
    /// stand for only one sign-in person.
    /// </summary>
    public async Task LinkAsync(string personId, string? financeId, bool notBilled, string actor)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            var person = await repository.GetPersonAsync(personId)
                ?? throw new InvalidOperationException("That person isn't on the sign-in app's roster any more.");
            financeId = string.IsNullOrWhiteSpace(financeId) || notBilled ? null : financeId;

            string? name = null;
            if (financeId is not null)
            {
                name = person.IsAdult
                    ? (await guardians.GetByIdAsync(financeId))?.FullName
                    : (await scouts.GetByIdAsync(financeId))?.FullName;
                if (name is null)
                {
                    throw new InvalidOperationException(person.IsAdult ? "That guardian wasn't found." : "That scout wasn't found.");
                }

                var other = (await repository.GetPeopleAsync()).FirstOrDefault(p =>
                    p.PersonId != personId && (person.IsAdult ? p.LinkedGuardianId : p.LinkedScoutId) == financeId);
                if (other is not null)
                {
                    throw new InvalidOperationException($"{name} is already linked to {other.FullName} in the sign-in app. Unlink them first.");
                }
            }

            person.LinkedScoutId = person.IsAdult ? null : financeId;
            person.LinkedGuardianId = person.IsAdult ? financeId : null;
            person.NotBilled = notBilled;
            person.LinkedBy = actor;
            person.LinkedUtc = timeProvider.GetUtcNow();
            await repository.SavePersonAsync(person);

            var what = notBilled ? "Marked not billed" : name is null ? "Unlinked" : "Linked";
            await audit.AddAsync(FinanceAuditEntityType.SignInLink, person.PersonId, what, actor,
                details: notBilled ? $"{person.FullName} (sign-in app) is never billed."
                    : name is null ? $"{person.FullName} (sign-in app) is no longer linked."
                    : $"{person.FullName} (sign-in app) is {name} here.");
        });
    }

    // ---- Trips ----

    /// <summary>
    /// Saves a trip as sent: adds new people, updates names and amounts, and marks anyone no
    /// longer on it as removed. Nothing is issued or voided until the treasurer applies it.
    /// </summary>
    public async Task ReceiveTripAsync(string signUpId, TripUpload upload)
    {
        if (string.IsNullOrWhiteSpace(signUpId) || upload.Lines.Any(l => l.Amount < 0 || !SignInPersonType.IsValid(l.PersonType)))
        {
            throw new ArgumentException("A trip needs an id, and every line a type and an amount of zero or more.");
        }

        var now = timeProvider.GetUtcNow();
        var trip = await repository.GetTripAsync(signUpId)
            ?? new TripImportEntity { PartitionKey = TripImportEntity.PartitionKeyValue, RowKey = signUpId };
        trip.Title = Trim(upload.Title, 200);
        trip.WhenText = Trim(upload.WhenText, 200);
        trip.StartDate = AsDate(upload.StartDate);
        trip.EndDate = AsDate(upload.EndDate);
        trip.DueDate = AsDate(upload.DueDate ?? upload.StartDate);
        trip.LastReceivedUtc = now;
        trip.LastSentBy = Trim(upload.SentBy, 200);

        await unitOfWork.InTransactionAsync(async () =>
        {
            await repository.SaveTripAsync(trip);
            var existing = (await repository.GetLinesAsync(signUpId)).ToDictionary(l => l.EntryId);
            var sent = new HashSet<string>();
            foreach (var incoming in upload.Lines.Where(l => !string.IsNullOrWhiteSpace(l.EntryId)))
            {
                if (!sent.Add(incoming.EntryId))
                {
                    continue;
                }

                var line = existing.GetValueOrDefault(incoming.EntryId)
                    ?? new TripImportLineEntity { PartitionKey = signUpId, RowKey = incoming.EntryId.Trim() };
                line.Name = Trim(incoming.Name, 200);
                line.SignInPersonId = incoming.PersonId?.Trim() ?? string.Empty;
                line.PersonType = incoming.PersonType;
                line.Amount = Math.Round(incoming.Amount, 2);
                line.CostText = Trim(incoming.CostText, 400);
                line.IsRemoved = false;
                line.LastReceivedUtc = now;
                await repository.SaveLineAsync(line);
            }

            foreach (var gone in existing.Values.Where(l => !l.IsRemoved && !sent.Contains(l.EntryId)))
            {
                gone.IsRemoved = true;
                gone.LastReceivedUtc = now;
                await repository.SaveLineAsync(gone);
            }
        });
    }

    public Task<IReadOnlyList<TripImportEntity>> GetTripsAsync() => repository.GetTripsAsync();

    /// <summary>What applying the trip would do, line by line. Null when the trip was never sent.</summary>
    public async Task<TripReview?> GetReviewAsync(string signUpId)
    {
        if (await repository.GetTripAsync(signUpId) is not { } trip)
        {
            return null;
        }

        var people = (await repository.GetPeopleAsync()).ToDictionary(p => p.PersonId);
        var scoutNames = (await scouts.GetAllAsync()).ToDictionary(s => s.ScoutId, s => s.FullName);
        var guardianNames = (await guardians.GetAllAsync()).ToDictionary(g => g.GuardianId, g => g.FullName);
        var lines = new List<TripReviewLine>();
        foreach (var line in await repository.GetLinesAsync(signUpId))
        {
            var invoice = line.HasInvoice ? await invoices.GetByIdAsync(line.InvoiceOwnerId!, line.InvoiceId!) : null;
            if (invoice?.StatusValue == InvoiceStatus.Voided)
            {
                invoice = null;
            }

            var person = people.GetValueOrDefault(line.SignInPersonId);
            var matchId = person?.LinkedId ?? line.ManualMatchId;
            var names = line.IsAdult ? guardianNames : scoutNames;
            var matchName = matchId is null ? null : names.GetValueOrDefault(matchId);
            var (action, note) = Decide(line, person, matchName is null ? null : matchId, invoice);
            lines.Add(new TripReviewLine(line, matchName is null ? null : matchId, matchName,
                person?.LinkedId is null && line.ManualMatchId is not null, invoice, action, note));
        }

        return new TripReview(trip, lines.OrderBy(l => l.Line.IsAdult).ThenBy(l => l.Line.Name).ToList());
    }

    /// <summary>The rules behind the review, one line at a time.</summary>
    internal static (TripLineAction Action, string Note) Decide(
        TripImportLineEntity line, SignInPersonEntity? person, string? matchId, InvoiceEntity? invoice)
    {
        var paid = invoice is null ? 0 : invoice.TotalAmount - invoice.AmountDueFromFamily;
        if (invoice is not null)
        {
            if (line.IsRemoved)
            {
                return paid == 0
                    ? (TripLineAction.Void, "Dropped out: the invoice is voided.")
                    : (TripLineAction.NeedsAttention, $"Dropped out after paying {paid:C}. Refund or void the invoice by hand.");
            }

            if (line.Amount == invoice.TotalAmount)
            {
                return (TripLineAction.UpToDate, string.Empty);
            }

            if (paid > 0)
            {
                return (TripLineAction.NeedsAttention, $"The cost went from {invoice.TotalAmount:C} to {line.Amount:C} after {paid:C} was paid. Adjust it by hand.");
            }

            return line.Amount > 0
                ? (TripLineAction.Reissue, $"The cost went from {invoice.TotalAmount:C} to {line.Amount:C}: voided and issued again.")
                : (TripLineAction.Void, "Nothing to pay any more: the invoice is voided.");
        }

        if (line.IsRemoved)
        {
            return (TripLineAction.UpToDate, "Dropped out before being invoiced.");
        }

        if (line.Amount <= 0)
        {
            return (TripLineAction.NoCharge, "Nothing to pay.");
        }

        if (person is { NotBilled: true })
        {
            return (TripLineAction.NotBilled, "Marked as never billed.");
        }

        if (matchId is null)
        {
            return string.IsNullOrEmpty(line.SignInPersonId)
                ? (TripLineAction.NeedsMatch, "Typed in on the trip, not picked from the roster: match them below.")
                : (TripLineAction.NeedsMatch, "Not linked yet: link them on the Sign-in app links page, or match them below.");
        }

        return (TripLineAction.Issue, string.Empty);
    }

    /// <summary>Matches one line by hand (a typed-in name, usually), or with null clears it.</summary>
    public async Task SetManualMatchAsync(string signUpId, string entryId, string? financeId)
    {
        var line = await repository.GetLineAsync(signUpId, entryId)
            ?? throw new InvalidOperationException("That person isn't on the trip any more.");
        line.ManualMatchId = string.IsNullOrWhiteSpace(financeId) ? null : financeId;
        await repository.SaveLineAsync(line);
    }

    /// <summary>
    /// Does what the review says: issues new invoices, voids those of people who dropped out with
    /// nothing paid, and voids and reissues any whose cost changed with nothing paid. Lines needing
    /// a match or the treasurer's attention are left alone. Returns how many invoices changed.
    /// </summary>
    public async Task<int> ApplyAsync(string signUpId, string actor)
    {
        var review = await GetReviewAsync(signUpId)
            ?? throw new InvalidOperationException("That trip hasn't been sent from the sign-in app.");
        var trip = review.Trip;
        var changed = 0;
        foreach (var item in review.Lines.Where(l => l.WillChange))
        {
            var line = item.Line;

            // Saved up to twice below (after the void, then after the reissue): with no version
            // each save overwrites, rather than failing as stale after the first.
            line.Version = null;
            if (item.Invoice is { } current)
            {
                var reason = item.Action == TripLineAction.Reissue
                    ? $"Trip cost changed to {line.Amount:C} in the sign-in app."
                    : line.IsRemoved ? "Dropped out of the trip in the sign-in app." : "Nothing to pay for the trip any more.";
                await accounts.VoidInvoiceAsync(current.OwnerId, current.InvoiceId, reason, actor);
                line.InvoiceOwnerId = null;
                line.InvoiceId = null;
                line.InvoicedAmount = 0;
                await repository.SaveLineAsync(line);
                line.Version = null;
            }

            if (item.Action is TripLineAction.Issue or TripLineAction.Reissue)
            {
                var issued = line.IsAdult
                    ? await accounts.IssueGuardianInvoiceAsync(item.MatchId!, line.Amount, trip.DueDate, trip.InvoiceDescription, actor)
                    : await accounts.IssueInvoiceAsync(item.MatchId!, line.Amount, 0, trip.DueDate, trip.InvoiceDescription, actor);
                line.InvoiceOwnerId = issued.OwnerId;
                line.InvoiceId = issued.InvoiceId;
                line.InvoicedAmount = line.Amount;
                await repository.SaveLineAsync(line);
            }

            changed++;
        }

        trip.LastAppliedUtc = timeProvider.GetUtcNow();
        trip.LastAppliedBy = actor;
        await repository.SaveTripAsync(trip);
        return changed;
    }

    /// <summary>How many trips have lines waiting on the treasurer: changes to apply, or people to match.</summary>
    public async Task<int> CountTripsNeedingReviewAsync()
    {
        // Trips over for more than a month are left out: anything still open on one is old news.
        var since = timeProvider.GetUtcNow().AddDays(-30);
        var count = 0;
        foreach (var trip in await repository.GetTripsAsync())
        {
            if (trip.EndDate >= since
                && await GetReviewAsync(trip.SignUpId) is { } review && (review.Changes > 0 || review.Waiting > 0))
            {
                count++;
            }
        }

        return count;
    }

    // ---- Status and payments, for the sign-in app ----

    /// <summary>Where everyone on a trip stands, for the sign-in app's Paid / Owes badges.</summary>
    public async Task<IReadOnlyList<TripLineStatusDto>> GetStatusAsync(string signUpId)
    {
        if (await GetReviewAsync(signUpId) is not { } review)
        {
            return [];
        }

        return review.Lines.Where(l => !l.Line.IsRemoved).Select(l =>
        {
            var matched = l.MatchId is not null || l.Action == TripLineAction.NotBilled;
            if (l.Invoice is { } invoice)
            {
                var status = invoice.StatusValue == InvoiceStatus.Paid ? TripLineStatus.Paid : TripLineStatus.Outstanding;
                return new TripLineStatusDto(l.Line.EntryId, true, status, invoice.TotalAmount, invoice.AmountDueFromFamily, l.PaidSoFar);
            }

            var notInvoiced = l.Action switch
            {
                TripLineAction.NotBilled => TripLineStatus.NotBilled,
                TripLineAction.NoCharge => TripLineStatus.NoCharge,
                _ => TripLineStatus.NotInvoiced,
            };
            return new TripLineStatusDto(l.Line.EntryId, matched, notInvoiced, l.Line.Amount, l.Line.Amount, 0);
        }).ToList();
    }

    /// <summary>
    /// Records a payment the sign-in app took against that person's trip invoice, through the same
    /// RecordInvoicePaymentAsync as every other payment, in one transaction with the record of it
    /// here -- so the same PaymentId sent again is recognised and never recorded twice.
    /// </summary>
    public async Task<TripPaymentResult> RecordPaymentAsync(string signUpId, TripPaymentUpload upload)
    {
        if (string.IsNullOrWhiteSpace(upload.PaymentId) || string.IsNullOrWhiteSpace(upload.EntryId))
        {
            return new(TripPaymentOutcome.Rejected, "A payment needs its id and the person's entry id.");
        }

        if (!InvoicePaymentMethod.External.Contains(upload.Method))
        {
            return new(TripPaymentOutcome.Rejected, "A trip payment must be by check, cash, Venmo or other.");
        }

        if (await repository.GetPaymentAsync(signUpId, upload.PaymentId) is not null)
        {
            return new(TripPaymentOutcome.AlreadyRecorded, "Already recorded.");
        }

        var line = await repository.GetLineAsync(signUpId, upload.EntryId);
        if (line is null)
        {
            return new(TripPaymentOutcome.NotFound, "That person isn't on the trip here. Send the trip to finance first.");
        }

        var invoice = line.HasInvoice ? await invoices.GetByIdAsync(line.InvoiceOwnerId!, line.InvoiceId!) : null;
        if (invoice is null || invoice.StatusValue == InvoiceStatus.Voided)
        {
            return new(TripPaymentOutcome.NotInvoiced, "The treasurer hasn't issued this person's invoice yet.");
        }

        try
        {
            await unitOfWork.InTransactionAsync(async () =>
            {
                await accounts.RecordInvoicePaymentAsync(invoice.OwnerId, invoice.InvoiceId, Math.Round(upload.Amount, 2),
                    upload.Method, upload.Reference, Trim(upload.RecordedBy, 200));
                await repository.AddPaymentAsync(new TripPaymentEntity
                {
                    PartitionKey = signUpId,
                    RowKey = upload.PaymentId.Trim(),
                    EntryId = line.EntryId,
                    InvoiceId = invoice.InvoiceId,
                    Amount = Math.Round(upload.Amount, 2),
                    Method = upload.Method,
                    Reference = Trim(upload.Reference ?? string.Empty, 400),
                    RecordedBy = Trim(upload.RecordedBy, 200),
                    ReceivedUtc = timeProvider.GetUtcNow(),
                });
            });
        }
        catch (DbUpdateException)
        {
            // The same payment, sent twice at once: the other one won, and this one rolled back.
            if (await repository.GetPaymentAsync(signUpId, upload.PaymentId) is not null)
            {
                return new(TripPaymentOutcome.AlreadyRecorded, "Already recorded.");
            }

            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new(TripPaymentOutcome.Rejected, ex.Message);
        }

        return new(TripPaymentOutcome.Recorded, "Recorded.");
    }

    // A calendar date, not a moment: stored as midnight UTC and shown from its UTC date, as the
    // invoice pages do.
    private static DateTimeOffset AsDate(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static string Trim(string? text, int max)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

/// <summary>Suggesting which finance scout or guardian a sign-in name is.</summary>
public static class NameMatch
{
    /// <summary>The name with case, punctuation and runs of spaces ignored: "O'Brien,  Sam" and "obrien sam" match.</summary>
    public static string Normalize(string name)
    {
        var text = new StringBuilder(name.Length);
        var space = false;
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                text.Append(c);
                space = false;
            }
            else if (char.IsWhiteSpace(c) && !space && text.Length > 0)
            {
                text.Append(' ');
                space = true;
            }
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// The one candidate with the same name, when there's exactly one and they aren't linked to
    /// someone else already. Two people with the same name get no suggestion: a guess there could
    /// put money on the wrong scout.
    /// </summary>
    public static string? Suggest(string name, IEnumerable<(string Id, string Name)> candidates, IReadOnlySet<string> alreadyLinked)
    {
        var wanted = Normalize(name);
        if (wanted.Length == 0)
        {
            return null;
        }

        var same = candidates.Where(c => Normalize(c.Name) == wanted).ToList();
        return same.Count == 1 && !alreadyLinked.Contains(same[0].Id) ? same[0].Id : null;
    }
}
