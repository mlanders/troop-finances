namespace BsaTroopFinances.Services;

public record ReceiptDownload(Stream Content, string ContentType, string FileName);

public interface IReceiptStorageService
{
    /// <summary>Uploads a receipt file and returns its blob path (to store on the
    /// ReimbursementRequest entity). Path convention: "{guardianId}/{requestId}.{ext}", where ext
    /// is derived from the (allow-listed) content type, never the user-supplied file name.</summary>
    Task<string> UploadAsync(string guardianId, string requestId, Stream content, string contentType);

    Task<ReceiptDownload> DownloadAsync(string blobPath, string contentType, string fileName);

    Task DeleteAsync(string blobPath);
}
