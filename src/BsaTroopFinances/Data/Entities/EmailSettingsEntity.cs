namespace BsaTroopFinances.Data.Entities;

/// <summary>
/// The single row of email settings managed from /admin/settings/email. There's deliberately no flag for
/// the login-code email -- it can't be disabled, since that would lock every guardian out of the
/// portal.
/// </summary>
public class EmailSettingsEntity
{
    public string PartitionKey { get; set; } = PartitionKeyValue;
    public string RowKey { get; set; } = RowKeyValue;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public bool ReceiptSubmittedEnabled { get; set; } = true;
    public bool ReceiptStatusEnabled { get; set; } = true;
    public bool InvoiceEnabled { get; set; } = true;

    /// <summary>The "Edit access granted" notice to Editors, the committee chair and the
    /// Auth:EditorGrantNotify addresses. Off until it's deliberately turned on.</summary>
    public bool EditorGrantedEnabled { get; set; }

    /// <summary>Newline-separated extra addresses for the "receipt submitted" email, on top of every
    /// Editor (who always get it). Only that email uses it; it can include people who never sign in.</summary>
    public string AdminRecipients { get; set; } = string.Empty;

    public const string PartitionKeyValue = "Settings";
    public const string RowKeyValue = "Email";

    public IReadOnlyList<string> GetAdminRecipients() =>
        AdminRecipients.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
