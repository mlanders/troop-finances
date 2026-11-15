using BsaTroopFinances.Data;
using BsaTroopFinances.Storage;

namespace BsaTroopFinances.Services;

/// <summary>
/// Wraps the private "receipts" container of the app's file store (see Storage/IBlobStore).
/// Nothing here is publicly accessible or served via SAS token -- every download goes through an
/// app-mediated Minimal API endpoint that checks auth + ownership first (see Program.cs's
/// /admin/reimbursements/{id}/receipt and /portal/reimbursements/{id}/receipt endpoints), then
/// streams via DownloadAsync below.
/// </summary>
public class ReceiptStorageService(IBlobStore store) : IReceiptStorageService
{
    // Maps from the (already allow-listed, see ReimbursementService.AllowedContentTypes)
    // content type rather than the user-supplied original file name, so an attacker-crafted
    // file name (e.g. containing "/" or "..") can never influence the blob path.
    private static readonly Dictionary<string, string> ExtensionsByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["application/pdf"] = ".pdf",
    };

    public async Task<string> UploadAsync(
        string guardianId, string requestId, Stream content, string contentType)
    {
        var ext = ExtensionsByContentType.GetValueOrDefault(contentType, string.Empty);
        var blobPath = $"{guardianId}/{requestId}{ext}";

        await store.UploadAsync(TableNames.ReceiptsContainer, blobPath, content, contentType);
        return blobPath;
    }

    public async Task<ReceiptDownload> DownloadAsync(string blobPath, string contentType, string fileName)
    {
        var blob = await store.OpenReadAsync(TableNames.ReceiptsContainer, blobPath)
            ?? throw new FileNotFoundException($"Receipt '{blobPath}' isn't in storage.");
        return new ReceiptDownload(blob.Content, contentType, fileName);
    }

    public async Task DeleteAsync(string blobPath)
    {
        await store.DeleteAsync(TableNames.ReceiptsContainer, blobPath);

        // Its thumbnail goes with it; there may not be one (or a container for one) yet.
        await store.DeleteAsync(TableNames.ReceiptThumbnailsContainer, ReceiptThumbnailService.ThumbnailPath(blobPath));
    }
}
