using System.Windows;
using FisPresentationPlatform.Controls;

namespace FisPresentationPlatform.Tests;

public sealed class WindowFitterTests
{
    private static readonly Size Preferred = new(1440, 920);
    private static readonly Size Design = new(1120, 760);

    [Fact]
    public void LargeScreen_UsesPreferredSizeCentredInWorkArea()
    {
        var placement = WindowFitter.Calculate(new Rect(0, 0, 2560, 1400), Preferred, Design);
        Assert.False(placement.Maximize);
        Assert.Equal(new Rect(560, 240, 1440, 920), placement.Bounds);
        Assert.Equal(Design, placement.Minimum);
    }

    [Theory]
    [InlineData(1366, 728)]   // 1366Ã—768 laptop, 100% scaling
    [InlineData(1280, 672)]   // 1920Ã—1080 laptop, 150% scaling
    [InlineData(1536, 824)]   // 1920Ã—1080 laptop, 125% scaling
    public void SmallScreen_FitsInsideWorkAreaAndStartsMaximized(double width, double height)
    {
        var work = new Rect(0, 0, width, height);
        var placement = WindowFitter.Calculate(work, Preferred, Design);
        Assert.True(placement.Maximize);
        Assert.True(work.Contains(placement.Bounds));
        Assert.True(placement.Minimum.Width <= width && placement.Minimum.Height <= height);
        Assert.True(placement.Bounds.Width >= placement.Minimum.Width && placement.Bounds.Height >= placement.Minimum.Height);
    }

    [Fact]
    public void SecondaryMonitorOffset_IsRespected()
    {
        var work = new Rect(-1920, 100, 1920, 1040);
        var placement = WindowFitter.Calculate(work, new Size(1400, 850), new Size(960, 640));
        Assert.False(placement.Maximize);
        Assert.True(work.Contains(placement.Bounds));
        Assert.Equal(-1660, placement.Bounds.Left);
    }

    [Fact]
    public void FillRatio_GrowsWindowOnLargeScreensOnly()
    {
        var large = WindowFitter.Calculate(new Rect(0, 0, 2560, 1400), Preferred, Design, fillRatio: 0.8);
        Assert.Equal(new Rect(256, 140, 2048, 1120), large.Bounds);
        var laptop = WindowFitter.Calculate(new Rect(0, 0, 1366, 728), Preferred, Design, fillRatio: 0.8);
        Assert.True(laptop.Maximize);
        Assert.True(new Rect(0, 0, 1366, 728).Contains(laptop.Bounds));
    }

    [Theory]
    [InlineData(1366, 697, 0.85, true)]    // 1366?768 laptop, maximized
    [InlineData(1280, 649, 0.79, true)]    // 1920?1080 at 150% scaling
    [InlineData(1536, 801, 0.98, false)]   // 1920?1080 at 125% scaling
    [InlineData(1920, 1009, 1.04, false)]  // 1080p desktop, maximized
    [InlineData(1426, 889, 1.0, false)]    // default window on a 1080p desktop
    [InlineData(2560, 1369, 1.25, false)]  // 1440p desktop, maximized
    [InlineData(3840, 2089, 1.25, false)]  // 4K at 100%
    [InlineData(900, 500, DisplayScaler.MinimumScale, true)]
    public void AutoScale_ShrinksOnLaptopsAndGrowsModestlyOnLargeDesktops(double width, double height, double scale, bool compact)
    {
        var client = new Size(width, height);
        Assert.Equal(scale, DisplayScaler.AutoScale(client), 2);
        Assert.Equal(compact, DisplayScaler.IsCompact(client, DisplayScaler.AutoScale(client)));
    }

    [Theory]
    [InlineData(0, 0.85)]
    [InlineData(1.1, 1.1)]
    [InlineData(3, DisplayScaler.MaximumScale)]
    [InlineData(0.2, DisplayScaler.MinimumScale)]
    [InlineData(-1, 0.85)]
    public void ManualPreference_OverridesAutoWithinLimits(double preference, double expected) =>
        Assert.Equal(expected, DisplayScaler.Resolve(new Size(1366, 697), preference), 2);

    [Fact]
    public void LargeManualZoomOnMediumScreen_UsesCompactLayout() =>
        Assert.True(DisplayScaler.IsCompact(new Size(1426, 889), 1.25));
}
