using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FisPresentationPlatform.Services;

/// <summary>A Demo Library / Downloads tile colour theme built from the FIS brand palette.</summary>
public sealed record TileTheme(string Id, string Name, Color Dark, Color Mid, Color? Line)
{
    /// <summary>Solid brand colour shown in the theme picker (Core Green for Green, the banner colour otherwise).</summary>
    public Brush Swatch { get; } = Freeze(new SolidColorBrush(Line is null ? Color.FromRgb(0x4B, 0xCD, 0x3E) : Mid));
    public override string ToString() => Name;
    private static Brush Freeze(Brush brush) { brush.Freeze(); return brush; }
}

/// <summary>
/// Tile themes and the shared banner art (gradient, swoosh and dots) used by library tiles, download tiles,
/// the details dialog and the baked package cover, so every surface draws the same banner.
/// </summary>
public static class TileThemes
{
    public const int ArtWidth = 640;
    public const int ArtHeight = 292;
    public const string DefaultId = "green";

    /// <summary>Green keeps the demo accent colour for its swoosh and dots; the others use a tint of their brand colour.</summary>
    public static readonly IReadOnlyList<TileTheme> All =
    [
        new("green", "Green", Rgb(0x09, 0x16, 0x21), Rgb(0x11, 0x34, 0x26), null),
        new("raspberry", "Raspberry", Rgb(0x24, 0x08, 0x16), Rgb(0xB2, 0x1A, 0x53), Rgb(0xFF, 0x8A, 0xB3)),
        new("eggplant", "Eggplant", Rgb(0x14, 0x0A, 0x1E), Rgb(0x43, 0x1C, 0x5B), Rgb(0xC1, 0x8B, 0xEA)),
        new("navy", "Navy", Rgb(0x09, 0x0B, 0x1C), Rgb(0x1D, 0x1F, 0x48), Rgb(0x8A, 0x93, 0xFF)),
        new("charcoal", "Charcoal", Rgb(0x16, 0x16, 0x18), Rgb(0x3D, 0x3D, 0x40), Rgb(0x4B, 0xCD, 0x3E))
    ];

    public static bool IsKnown(string? id) => string.IsNullOrWhiteSpace(id) || All.Any(theme => theme.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));

    public static TileTheme Get(string? id) =>
        All.FirstOrDefault(theme => theme.Id.Equals(id?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>The swoosh/dot/label colour for a theme: the theme tint, or the demo accent for Green.</summary>
    public static Color LineColor(string? themeId, string? accentColor)
    {
        var theme = Get(themeId);
        if (theme.Line is { } line) return line;
        try { if (!string.IsNullOrWhiteSpace(accentColor)) return (Color)ColorConverter.ConvertFromString(accentColor); }
        catch (FormatException) { }
        return Rgb(0x4B, 0xCD, 0x3E);
    }

    /// <summary>Draws the 640×292 banner: theme gradient, swoosh, dots, optional logo and optional business line + title.</summary>
    public static void Draw(DrawingContext drawing, string? themeId, string? accentColor, string seedId,
        ImageSource? logo = null, string? business = null, string? title = null)
    {
        var theme = Get(themeId);
        var line = LineColor(themeId, accentColor);
        drawing.DrawRectangle(new LinearGradientBrush(theme.Dark, theme.Mid, 20), null, new Rect(0, 0, ArtWidth, ArtHeight));
        var swoosh = new StreamGeometry();
        using (var context = swoosh.Open())
        {
            context.BeginFigure(new Point(250, 320), false, false);
            context.BezierTo(new Point(380, 120), new Point(480, 260), new Point(680, 40), true, true);
        }
        swoosh.Freeze();
        drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(150, line.R, line.G, line.B)), 10), swoosh);
        drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(70, line.R, line.G, line.B)), 3), swoosh);
        var random = new Random(StableSeed(seedId));
        for (var dot = 0; dot < 70; dot++)
        {
            var x = 360 + random.NextDouble() * 280;
            var y = random.NextDouble() * ArtHeight;
            drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)random.Next(40, 160), line.R, line.G, line.B)), null, new Point(x, y), 2.2, 2.2);
        }
        if (logo is not null && logo.Width > 0)
            drawing.DrawImage(logo, new Rect(32, 28, 72, 72 * logo.Height / logo.Width));
        if (!string.IsNullOrWhiteSpace(business))
        {
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            drawing.DrawText(new FormattedText(business.ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 13,
                new SolidColorBrush(line), 1.0), new Point(32, 168));
        }
        if (!string.IsNullOrWhiteSpace(title))
        {
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            drawing.DrawText(new FormattedText(title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 30, Brushes.White, 1.0)
                { MaxTextWidth = 420, MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis }, new Point(32, 190));
        }
    }

    public static BitmapSource Render(string? themeId, string? accentColor, string seedId, ImageSource? logo = null, string? business = null, string? title = null)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) Draw(drawing, themeId, accentColor, seedId, logo, business, title);
        var bitmap = new RenderTargetBitmap(ArtWidth, ArtHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static readonly ConcurrentDictionary<string, BitmapSource> BannerCache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _fisLogo;

    /// <summary>Cached tile banner art (no text — tiles overlay the business line and title so they stay live and crisp).</summary>
    public static BitmapSource Banner(string? themeId, string? accentColor, string seedId)
    {
        if (BannerCache.Count > 200) BannerCache.Clear();
        return BannerCache.GetOrAdd($"{Get(themeId).Id}|{accentColor}|{seedId}", _ => Render(themeId, accentColor, seedId, FisLogo));
    }

    /// <summary>The app's FIS logo (the logo every bundled demo uses), or null when not running inside the app.</summary>
    public static ImageSource? FisLogo
    {
        get
        {
            if (_fisLogo is not null || Application.Current is null) return _fisLogo;
            try
            {
                var logo = new BitmapImage(new Uri("pack://application:,,,/FisPresentationPlatform;component/Assets/fis-logo.png"));
                logo.Freeze();
                _fisLogo = logo;
            }
            catch (Exception exception) when (exception is System.IO.IOException or NotSupportedException or UriFormatException) { }
            return _fisLogo;
        }
    }

    /// <summary>FNV-1a hash so the dot pattern is identical across runs (string.GetHashCode is randomised per process).</summary>
    public static int StableSeed(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in value ?? "") { hash ^= character; hash *= 16777619u; }
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
