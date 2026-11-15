using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace BsaTroopFinances.Services;

/// <summary>
/// The home-screen icons, drawn for this troop: the app's mark (the fleur-de-lis with a gold coin,
/// taken from wwwroot/favicon.svg) with the troop's number beneath it (<see cref="TroopInfo.IconText"/>).
/// Drawn once per size and kept in memory.
/// </summary>
public sealed partial class TroopIcons
{
    /// <summary>The icon files the manifest, the iPhone tag and the service worker ask for.</summary>
    public static readonly IReadOnlyDictionary<string, (int Size, bool Maskable)> Files = new Dictionary<string, (int, bool)>
    {
        ["icon-192.png"] = (192, false),
        ["icon-512.png"] = (512, false),
        ["icon-maskable-512.png"] = (512, true),
        ["apple-touch-icon.png"] = (180, false),
    };

    private static readonly SKColor Red = SKColor.Parse("#ce1126");
    private static readonly SKColor Navy = SKColor.Parse("#003f87");
    private static readonly SKColor Gold = SKColor.Parse("#fcb711");
    private static readonly SKColor GoldEdge = SKColor.Parse("#d99a00");

    // The coin, in the SVG's own coordinates (see favicon.svg).
    private static readonly SKPoint CoinCenter = new(554, 422);

    private readonly string _fleurDeLis;
    private readonly string _caption;
    private readonly ConcurrentDictionary<string, byte[]> _drawn = new();

    public TroopIcons(string faviconSvg, string caption)
    {
        _fleurDeLis = FirstPath().Match(faviconSvg) is { Success: true } path
            ? path.Groups[1].Value
            : throw new InvalidOperationException("favicon.svg has no <path> for the fleur-de-lis.");
        _caption = caption;
    }

    public static TroopIcons FromWebRoot(IWebHostEnvironment environment, TroopInfo troop)
    {
        using var stream = environment.WebRootFileProvider.GetFileInfo("favicon.svg").CreateReadStream();
        using var reader = new StreamReader(stream);
        return new TroopIcons(reader.ReadToEnd(), troop.IconText);
    }

    /// <summary>The PNG for one of <see cref="Files"/>, or null for any other name.</summary>
    public byte[]? Png(string file) =>
        Files.TryGetValue(file, out var spec) ? _drawn.GetOrAdd(file, _ => Draw(spec.Size, spec.Maskable)) : null;

    private byte[] Draw(int size, bool maskable)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        // A maskable icon may be cropped to a circle, so everything stays inside its middle 80%.
        var scale = maskable ? 0.8f : 1f;
        canvas.Translate(size / 2f, size / 2f);
        canvas.Scale(scale * size / 512f);
        canvas.Translate(-256, -256);

        // Laid out on a 512 square: the mark above, the caption below (or the mark alone, centred).
        var hasCaption = _caption.Length > 0;
        DrawMark(canvas, hasCaption ? SKRect.Create(150, 92, 230, 250) : SKRect.Create(116, 116, 280, 280));
        if (hasCaption)
        {
            using var font = new SKFont(BoldTypeface(), 84) { Embolden = false, Subpixel = true };
            using var paint = new SKPaint { Color = Navy, IsAntialias = true };
            canvas.DrawText(_caption, 256, 412, SKTextAlign.Center, font, paint);
        }

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    /// <summary>The fleur-de-lis and coin, scaled to fit <paramref name="box"/>.</summary>
    private void DrawMark(SKCanvas canvas, SKRect box)
    {
        using var fleur = SKPath.ParseSvgPathData(_fleurDeLis);
        var bounds = fleur.Bounds;
        bounds.Union(SKRect.Create(CoinCenter.X - 176, CoinCenter.Y - 176, 352, 352));
        var fit = Math.Min(box.Width / bounds.Width, box.Height / bounds.Height);

        canvas.Save();
        canvas.Translate(box.MidX - bounds.MidX * fit, box.MidY - bounds.MidY * fit);
        canvas.Scale(fit);

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        fill.Color = Red;
        canvas.DrawPath(fleur, fill);
        fill.Color = SKColors.White;
        canvas.DrawCircle(CoinCenter, 176, fill);
        fill.Color = Gold;
        canvas.DrawCircle(CoinCenter, 152, fill);
        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 11, Color = GoldEdge };
        canvas.DrawCircle(CoinCenter, 125, edge);
        using var dollarFont = new SKFont(BoldTypeface(), 220);
        fill.Color = Navy;
        canvas.DrawText("$", CoinCenter.X, 501, SKTextAlign.Center, dollarFont, fill);
        canvas.Restore();
    }

    /// <summary>A bold sans-serif: DejaVu in the container (installed by the Dockerfile; read from
    /// its file, since the image's Skia doesn't search system fonts), Arial on Windows.</summary>
    private static SKTypeface BoldTypeface()
    {
        foreach (var file in (string[])["/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", @"C:\Windows\Fonts\arialbd.ttf"])
        {
            if (File.Exists(file) && SKTypeface.FromFile(file) is { } fromFile)
            {
                return fromFile;
            }
        }

        return SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    }

    [GeneratedRegex("""<path\b[^>]*\sd="([^"]+)" """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex FirstPath();
}
