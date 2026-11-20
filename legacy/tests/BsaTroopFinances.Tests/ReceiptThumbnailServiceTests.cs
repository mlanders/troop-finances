using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Services;
using BsaTroopFinances.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace BsaTroopFinances.Tests;

/// <summary>Thumbnails stored in and read back from a real file store: a temporary folder always,
/// and Azure blob storage too when Azurite is running.</summary>
public class ReceiptThumbnailServiceTests
{
    private static byte[] Pdf()
    {
        using var stream = new MemoryStream();
        using (var document = SKDocument.CreatePdf(stream))
        {
            document.BeginPage(612, 792).DrawRect(20, 20, 200, 60, new SKPaint { Color = SKColors.Navy });
            document.EndPage();
            document.Close();
        }

        return stream.ToArray();
    }

    [SkippableTheory]
    [InlineData(TestBlobStores.FileSystem)]
    [InlineData(TestBlobStores.Azurite)]
    public async Task A_thumbnail_is_drawn_once_stored_and_removed_with_its_receipt(string kind)
    {
        await using var test = TestBlobStores.Create(kind);
        var blobs = test.Store;
        await blobs.EnsureContainerAsync(TableNames.ReceiptsContainer);
        await blobs.EnsureContainerAsync(TableNames.ReceiptThumbnailsContainer);
        var storage = new ReceiptStorageService(blobs);
        var service = new ReceiptThumbnailService(blobs, storage, NullLogger<ReceiptThumbnailService>.Instance);

        using var pdf = new MemoryStream(Pdf());
        var good = new ReimbursementRequestEntity { ReceiptContentType = "application/pdf", ReceiptBlobPath = await storage.UploadAsync("maria", "req1", pdf, "application/pdf") };
        using var junk = new MemoryStream("not a pdf"u8.ToArray());
        var bad = new ReimbursementRequestEntity { ReceiptContentType = "application/pdf", ReceiptBlobPath = await storage.UploadAsync("maria", "req2", junk, "application/pdf") };

        var first = await service.GetOrCreateAsync(good);
        Assert.NotNull(first);
        Assert.Equal([0xFF, 0xD8], first[..2]);
        Assert.Equal(first, await TestBlobStores.ReadAllAsync(blobs, TableNames.ReceiptThumbnailsContainer, "maria/req1.pdf.jpg"));

        // The second call reads the stored copy: the receipt itself can be gone and it still answers.
        await blobs.DeleteAsync(TableNames.ReceiptsContainer, good.ReceiptBlobPath);
        Assert.Equal(first, await service.GetOrCreateAsync(good));

        // A receipt that can't be drawn is remembered as such (an empty file) rather than retried.
        Assert.Null(await service.GetOrCreateAsync(bad));
        Assert.Empty((await TestBlobStores.ReadAllAsync(blobs, TableNames.ReceiptThumbnailsContainer, "maria/req2.pdf.jpg"))!);
        Assert.Null(await service.GetOrCreateAsync(bad));

        await storage.DeleteAsync(good.ReceiptBlobPath);
        Assert.False(await blobs.ExistsAsync(TableNames.ReceiptThumbnailsContainer, "maria/req1.pdf.jpg"));
    }
}
