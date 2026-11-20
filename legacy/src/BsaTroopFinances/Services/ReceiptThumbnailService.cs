using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Storage;

namespace BsaTroopFinances.Services;

public interface IReceiptThumbnailService
{
    /// <summary>The receipt's thumbnail as JPEG bytes, drawn and stored the first time it is asked
    /// for; null when the receipt can't be drawn (the caller shows a placeholder instead).</summary>
    Task<byte[]?> GetOrCreateAsync(ReimbursementRequestEntity request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thumbnails for the receipt previews on My Receipts and the admin reimbursement page, drawn on
/// the server (see <see cref="ReceiptThumbnailRenderer"/>) so a browser loads one small image per
/// receipt instead of the original file. Each is stored in its own private container under the
/// receipt's path plus ".jpg" and reused from then on. A receipt that can't be drawn gets an empty
/// blob, so it isn't attempted again on every view. Thumbnails are left out of backups: they can
/// always be drawn again from the receipt.
/// </summary>
public class ReceiptThumbnailService(IBlobStore store, IReceiptStorageService receipts, ILogger<ReceiptThumbnailService> logger)
    : IReceiptThumbnailService
{
    private const long MaxReceiptBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan RenderTimeLimit = TimeSpan.FromSeconds(15);

    // One at a time: drawing is memory-hungry, and the app runs on a small plan.
    private static readonly SemaphoreSlim RenderLock = new(1, 1);

    public static string ThumbnailPath(string receiptBlobPath) => receiptBlobPath + ".jpg";

    public async Task<byte[]?> GetOrCreateAsync(ReimbursementRequestEntity request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.ReceiptBlobPath))
        {
            return null;
        }

        var path = ThumbnailPath(request.ReceiptBlobPath);
        try
        {
            var existing = await store.OpenReadAsync(TableNames.ReceiptThumbnailsContainer, path, cancellationToken);
            if (existing is not null)
            {
                await using (existing.Content)
                {
                    using var buffer = new MemoryStream();
                    await existing.Content.CopyToAsync(buffer, cancellationToken);
                    return buffer.Length == 0 ? null : buffer.ToArray();
                }
            }

            var thumbnail = await RenderAsync(request, cancellationToken);
            using var upload = new MemoryStream(thumbnail ?? []);
            await store.UploadAsync(TableNames.ReceiptThumbnailsContainer, path, upload, "image/jpeg", cancellationToken);
            return thumbnail;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A thumbnail is a convenience; the receipt itself still opens from its link.
            logger.LogWarning(ex, "Couldn't get a thumbnail for receipt {Path}", request.ReceiptBlobPath);
            return null;
        }
    }

    private async Task<byte[]?> RenderAsync(ReimbursementRequestEntity request, CancellationToken cancellationToken)
    {
        byte[] content;
        var download = await receipts.DownloadAsync(request.ReceiptBlobPath, request.ReceiptContentType, request.ReceiptOriginalFileName);
        await using (download.Content)
        {
            using var buffer = new MemoryStream();
            await download.Content.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length > MaxReceiptBytes)
            {
                return null;
            }

            content = buffer.ToArray();
        }

        // The drawing code can't be interrupted, so a file that takes too long is given up on and
        // recorded as undrawable. The lock is released when the drawing itself ends, however this
        // method leaves.
        await RenderLock.WaitAsync(cancellationToken);
        var rendering = Task.Run(() => ReceiptThumbnailRenderer.Render(content, request.ReceiptContentType), CancellationToken.None);
        _ = rendering.ContinueWith(_ => RenderLock.Release(), TaskScheduler.Default);

        var finished = await Task.WhenAny(rendering, Task.Delay(RenderTimeLimit, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        if (finished == rendering)
        {
            return await rendering;
        }

        logger.LogWarning("Drawing a thumbnail for receipt {Path} took longer than {Limit}", request.ReceiptBlobPath, RenderTimeLimit);
        return null;
    }
}
