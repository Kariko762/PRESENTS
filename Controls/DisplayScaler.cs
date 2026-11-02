using System.Windows;
using System.Windows.Media;

namespace FisPresentationPlatform.Controls;

/// <summary>
/// Scales a window's whole UI to suit the screen and reports when a compact layout should be used.
/// Auto scale shrinks the layout on small screens (laptops) and enlarges it modestly on large desktops;
/// a saved user preference (Settings › Display size, Ctrl +/−/0) overrides Auto.
/// </summary>
public sealed class DisplayScaler
{
    /// <summary>Client size (DIPs) the Studio layout is designed to look right at 100%.</summary>
    public static readonly Size Design = new(1360, 820);
    public const double MinimumScale = 0.7;
    public const double MaximumScale = 1.4;
    public const double Step = 0.05;
    /// <summary>Display size choices offered in Settings; 0 means Auto.</summary>
    public static readonly double[] Presets = [0, 0.8, 0.9, 1.0, 1.1, 1.25];

    private readonly ScaleTransform _transform = new(1, 1);
    private FrameworkElement? _host;

    /// <summary>0 = Auto; otherwise a fixed scale.</summary>
    public double Preference { get; private set; }
    public double Scale { get; private set; } = 1;
    public bool Compact { get; private set; }
    public event EventHandler? Changed;

    public DisplayScaler(Window window, double preference = 0)
    {
        Preference = Normalize(preference);
        window.Loaded += (_, _) =>
        {
            if (window.Content is not FrameworkElement content || VisualTreeHelper.GetParent(content) is not FrameworkElement host) return;
            content.LayoutTransform = _transform;
            _host = host;
            host.SizeChanged += (_, _) => Update();
            Update();
        };
    }

    public void SetPreference(double preference)
    {
        Preference = Normalize(preference);
        Update();
    }

    /// <summary>Steps the effective scale up or down and keeps it as a fixed preference.</summary>
    public double Nudge(int direction)
    {
        SetPreference(Math.Round((Scale + direction * Step) / Step) * Step);
        return Preference;
    }

    private void Update()
    {
        if (_host is null || _host.ActualWidth <= 0 || _host.ActualHeight <= 0) return;
        var client = new Size(_host.ActualWidth, _host.ActualHeight);
        var scale = Resolve(client, Preference);
        var compact = IsCompact(client, scale);
        if (Math.Abs(scale - Scale) < 0.001 && compact == Compact && Math.Abs(_transform.ScaleX - scale) < 0.001) return;
        Scale = scale;
        Compact = compact;
        _transform.ScaleX = _transform.ScaleY = scale;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static double Normalize(double preference) =>
        preference <= 0 || double.IsNaN(preference) ? 0 : Math.Round(Math.Clamp(preference, MinimumScale, MaximumScale), 2);

    public static double Resolve(Size client, double preference) => Normalize(preference) is > 0 and var fixedScale ? fixedScale : AutoScale(client);

    /// <summary>
    /// Fits the design size into the client area. Below the design size the UI shrinks proportionally;
    /// on large screens it grows at half rate after a 15% margin so normal desktops stay at about 100%.
    /// </summary>
    public static double AutoScale(Size client)
    {
        if (client.Width <= 0 || client.Height <= 0) return 1;
        var fit = Math.Min(client.Width / Design.Width, client.Height / Design.Height);
        var scale = fit <= 1 ? fit : fit <= 1.15 ? 1 : 1 + (fit - 1.15) / 2;
        return Math.Round(Math.Clamp(scale, MinimumScale, 1.25), 2);
    }

    /// <summary>Short or narrow screens get the compact layout (smaller header, slimmer sidebar), as does a large manual zoom on a medium screen.</summary>
    public static bool IsCompact(Size client, double scale) =>
        client.Height < 800 || client.Width < 1280 || client.Height / scale < 720 || client.Width / scale < 1150;
}
