namespace BsaTroopFinances.Data.Entities;

/// <summary>
/// A trip (an event sign-up) as the meeting sign-in app last sent it (PUT /api/signin/trips/{id}).
/// Receiving one never issues anything: the treasurer reviews it on Invoices > Trip sign-ups and
/// issues, voids or reissues the invoices from there. PartitionKey is a constant; RowKey is the
/// sign-in app's sign-up id.
/// </summary>
public class TripImportEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>The dates and times as the sign-in app words them, e.g. "Sat, Oct 17 – Sun, Oct 18".</summary>
    public string WhenText { get; set; } = string.Empty;

    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }

    /// <summary>When the invoices fall due: the trip's last day to drop out, or its start.</summary>
    public DateTimeOffset DueDate { get; set; }

    public DateTimeOffset LastReceivedUtc { get; set; }
    public string LastSentBy { get; set; } = string.Empty;

    public DateTimeOffset? LastAppliedUtc { get; set; }
    public string? LastAppliedBy { get; set; }

    public string SignUpId => RowKey;

    /// <summary>What each invoice says it's for.</summary>
    public string InvoiceDescription => string.IsNullOrWhiteSpace(WhenText) ? Title : $"{Title} ({WhenText})";

    public const string PartitionKeyValue = "TripImport";
}

/// <summary>
/// One person on a trip, as last sent, and the invoice issued for them. PartitionKey is the sign-up
/// id; RowKey is the sign-in app's entry id for that person on the trip, so the pair is stable
/// for as long as they stay signed up.
/// </summary>
public class TripImportLineEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The sign-in roster person, or empty for a name typed in on the trip.</summary>
    public string SignInPersonId { get; set; } = string.Empty;

    /// <summary>SignInPersonType.Scout or Adult.</summary>
    public string PersonType { get; set; } = SignInPersonType.Scout;

    /// <summary>What they owe for the trip, per the sign-in app's cost split.</summary>
    public decimal Amount { get; set; }

    /// <summary>How that was worked out, e.g. "$20.00 fee + $8.57 food = $28.57".</summary>
    public string CostText { get; set; } = string.Empty;

    /// <summary>No longer on the trip's list when it was last sent.</summary>
    public bool IsRemoved { get; set; }

    /// <summary>A one-off match the treasurer made for this line only (a typed-in name, usually):
    /// the finance scout, or guardian for an Adult. A roster link, when there is one, wins.</summary>
    public string? ManualMatchId { get; set; }

    /// <summary>The invoice issued for this line: its owner (scout or guardian id) and id.</summary>
    public string? InvoiceOwnerId { get; set; }
    public string? InvoiceId { get; set; }

    /// <summary>The amount that invoice was issued for, to spot a changed cost.</summary>
    public decimal InvoicedAmount { get; set; }

    public DateTimeOffset LastReceivedUtc { get; set; }

    public string SignUpId => PartitionKey;
    public string EntryId => RowKey;
    public bool IsAdult => PersonType == SignInPersonType.Adult;
    public bool HasInvoice => !string.IsNullOrEmpty(InvoiceId);
}

/// <summary>
/// A payment the sign-in app sent (POST /api/signin/trips/{id}/payments), saved in the same
/// transaction as the invoice payment it made. Keyed by the sign-in app's own payment id, so a
/// retry of the same payment is recognised and never recorded twice. PartitionKey is the sign-up
/// id; RowKey is that payment id.
/// </summary>
public class TripPaymentEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string EntryId { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;

    /// <summary>Who took the money and who confirmed it, as the sign-in app words it.</summary>
    public string RecordedBy { get; set; } = string.Empty;

    public DateTimeOffset ReceivedUtc { get; set; }

    public string SignUpId => PartitionKey;
    public string PaymentId => RowKey;
}
