using SkiaSharp;

namespace BsaTroopFinances.Services;

/// <summary>
/// Draws a small JPEG of a receipt: the first page of a PDF, or a scaled-down photo turned the
/// right way up. Receipts are files guardians upload, so anything that can't be read (a damaged
/// file, a password-protected PDF, an absurdly large image) yields null rather than an error.
/// </summary>
public static class ReceiptThumbnailRenderer
{
    /// <summary>Longest side in pixels: twice the size it is shown at, so it stays sharp on a phone.</summary>
    public const int MaxSize = 480;

    // A photo is decoded before it is shrunk; this caps the memory one upload can ask for (~240 MB).
    private const long MaxDecodedPixels = 60_000_000;

    public static byte[]? Render(byte[] content, string contentType)
    {
        try
        {
            return contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ? RenderPdf(content) : RenderImage(content);
        }
        // A drawing library that failed to load is the server's problem, not the file's: let that
        // through, so the receipt isn't written off as undrawable.
        catch (Exception ex) when (ex is not (DllNotFoundException or TypeInitializationException or BadImageFormatException or EntryPointNotFoundException))
        {
            return null;
        }
    }

    private static byte[]? RenderPdf(byte[] content)
    {
#pragma warning disable CA1416 // PDFium ships for the platforms this app runs on (Windows locally, Linux in Azure).
        using var page = PDFtoImage.Conversion.ToImage(content, page: 0,
            options: new PDFtoImage.RenderOptions(Width: MaxSize, WithAspectRatio: true, BackgroundColor: SKColors.White));
#pragma warning restore CA1416
        return Encode(page, SKEncodedOrigin.TopLeft);
    }

    private static byte[]? RenderImage(byte[] content)
    {
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        // Phone photos are far larger than a thumbnail. JPEG can be decoded straight to a fraction
        // of its size, which keeps a 48-megapixel photo from being held in memory at full size.
        var longest = Math.Max(codec.Info.Width, codec.Info.Height);
        var size = codec.GetScaledDimensions(Math.Min(1f, MaxSize * 2f / longest));
        if ((long)size.Width * size.Height > MaxDecodedPixels)
        {
            return null;
        }

        using var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return null;
        }

        return Encode(bitmap, codec.EncodedOrigin);
    }

    /// <summary>Shrinks to <see cref="MaxSize"/>, applies the camera's stored rotation, and puts
    /// it on white (JPEG has no transparency).</summary>
    private static byte[] Encode(SKBitmap source, SKEncodedOrigin origin)
    {
        var sideways = origin is SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom;
        var (uprightWidth, uprightHeight) = sideways ? (source.Height, source.Width) : (source.Width, source.Height);
        var scale = Math.Min(1f, MaxSize / (float)Math.Max(uprightWidth, uprightHeight));
        var info = new SKImageInfo(
            Math.Max(1, (int)Math.Round(uprightWidth * scale)), Math.Max(1, (int)Math.Round(uprightHeight * scale)),
            SKColorType.Rgba8888, SKAlphaType.Opaque);

        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);
        switch (origin)
        {
            case SKEncodedOrigin.RightTop:      // held upright in portrait: turn a quarter clockwise
                canvas.Translate(uprightWidth, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.BottomRight:   // upside down
                canvas.Translate(uprightWidth, uprightHeight);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.LeftBottom:    // a quarter anticlockwise
                canvas.Translate(0, uprightHeight);
                canvas.RotateDegrees(270);
                break;
        }

        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, 82);
        return encoded.ToArray();
    }
}
