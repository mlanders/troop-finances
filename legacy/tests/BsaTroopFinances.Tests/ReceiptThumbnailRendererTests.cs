using System.Text;
using BsaTroopFinances.Services;
using SkiaSharp;

namespace BsaTroopFinances.Tests;

public class ReceiptThumbnailRendererTests
{
    private static byte[] Image(int width, int height, SKEncodedImageFormat format, bool transparent = false)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(transparent ? SKColors.Transparent : SKColors.LightGray);
            // A red block in the top-left corner, to tell which way up the result is.
            canvas.DrawRect(0, 0, width / 4f, height / 4f, new SKPaint { Color = SKColors.Red });
        }

        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    private static byte[] Pdf(float width, float height)
    {
        using var stream = new MemoryStream();
        using (var document = SKDocument.CreatePdf(stream))
        {
            var canvas = document.BeginPage(width, height);
            canvas.DrawRect(20, 20, width - 40, 60, new SKPaint { Color = SKColors.Navy });
            document.EndPage();
            document.Close();
        }

        return stream.ToArray();
    }

    /// <summary>Adds the EXIF "orientation" tag a phone camera writes, right after the JPEG's opening marker.</summary>
    private static byte[] WithExifOrientation(byte[] jpeg, byte orientation)
    {
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8,              // big-endian TIFF header, first IFD at offset 8
            0, 1,                                                // one entry
            0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, orientation, 0, 0,  // tag 0x0112 (orientation), SHORT, count 1
            0, 0, 0, 0,                                          // no further IFD
        ];
        return [.. jpeg[..2], .. exif, .. jpeg[2..]];
    }

    private static SKBitmap Decode(byte[]? jpeg)
    {
        Assert.NotNull(jpeg);
        Assert.Equal([0xFF, 0xD8], jpeg[..2]);
        return SKBitmap.Decode(jpeg);
    }

    private static bool IsRed(SKColor color) => color.Red > 180 && color.Green < 90 && color.Blue < 90;

    [Fact]
    public void A_pdf_becomes_a_jpeg_of_its_first_page_no_bigger_than_the_limit()
    {
        using var thumbnail = Decode(ReceiptThumbnailRenderer.Render(Pdf(612, 792), "application/pdf"));

        Assert.Equal(ReceiptThumbnailRenderer.MaxSize, thumbnail.Height);
        Assert.InRange(thumbnail.Width, 360, 380);                      // letter page: 8.5 x 11
        Assert.True(thumbnail.GetPixel(5, 5).Red > 240);                // white paper...
        Assert.True(thumbnail.GetPixel(thumbnail.Width / 2, 35).Blue > 80 && thumbnail.GetPixel(thumbnail.Width / 2, 35).Red < 60);   // ...with its content drawn
    }

    [Fact]
    public void A_large_photo_is_shrunk_and_keeps_its_shape()
    {
        using var thumbnail = Decode(ReceiptThumbnailRenderer.Render(Image(4000, 2000, SKEncodedImageFormat.Jpeg), "image/jpeg"));

        Assert.Equal((480, 240), (thumbnail.Width, thumbnail.Height));
        Assert.True(IsRed(thumbnail.GetPixel(20, 20)));
    }

    [Fact]
    public void A_small_image_is_not_enlarged_and_transparency_becomes_white()
    {
        using var thumbnail = Decode(ReceiptThumbnailRenderer.Render(Image(200, 100, SKEncodedImageFormat.Png, transparent: true), "image/png"));

        Assert.Equal((200, 100), (thumbnail.Width, thumbnail.Height));
        Assert.True(thumbnail.GetPixel(150, 80).Red > 240 && thumbnail.GetPixel(150, 80).Green > 240);
    }

    [Theory]
    [InlineData(6, 100, 200, 90, 10)]     // phone held upright: the corner block ends up top-right
    [InlineData(3, 200, 100, 190, 90)]    // upside down: bottom-right
    [InlineData(8, 100, 200, 10, 190)]    // turned the other way: bottom-left
    public void A_photo_is_turned_the_way_the_camera_recorded_it(byte orientation, int width, int height, int redX, int redY)
    {
        var photo = WithExifOrientation(Image(200, 100, SKEncodedImageFormat.Jpeg), orientation);

        using var thumbnail = Decode(ReceiptThumbnailRenderer.Render(photo, "image/jpeg"));

        Assert.Equal((width, height), (thumbnail.Width, thumbnail.Height));
        Assert.True(IsRed(thumbnail.GetPixel(redX, redY)));
    }

    [Theory]
    [InlineData("application/pdf", "%PDF-1.4\n% sample receipt\n")]
    [InlineData("application/pdf", "not a pdf at all")]
    [InlineData("image/jpeg", "not an image")]
    [InlineData("image/png", "")]
    public void A_file_that_cannot_be_read_gives_no_thumbnail_instead_of_an_error(string contentType, string content)
    {
        Assert.Null(ReceiptThumbnailRenderer.Render(Encoding.ASCII.GetBytes(content), contentType));
    }
}
