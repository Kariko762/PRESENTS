using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FisPresentationPlatform.Controls;

public readonly record struct WindowPlacement(Rect Bounds, Size Minimum, bool Maximize);

/// <summary>Sizes and centres a window within the work area of the monitor it opens on.</summary>
public static class WindowFitter
{
    public const double ScreenMargin = 0.94;

    /// <param name="preferred">Size used when the screen has room for it.</param>
    /// <param name="minimum">Smallest window size the layout supports; lowered automatically on very small screens.</param>
    /// <param name="maximizeWhenTooSmall">Start maximized when the preferred size does not fit the screen; otherwise just shrink.</param>
    /// <param name="fillRatio">On large screens, grow to this fraction of the work area (0 keeps the preferred size).</param>
    public static void Attach(Window window, Size preferred, Size minimum, bool maximizeWhenTooSmall = true, double fillRatio = 0)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.SourceInitialized += (_, _) =>
        {
            var workArea = GetWorkArea(window);
            if (workArea.IsEmpty) return;
            var placement = Calculate(workArea, preferred, minimum, fillRatio);
            window.MinWidth = placement.Minimum.Width;
            window.MinHeight = placement.Minimum.Height;
            window.Left = placement.Bounds.Left;
            window.Top = placement.Bounds.Top;
            window.Width = placement.Bounds.Width;
            window.Height = placement.Bounds.Height;
            if (placement.Maximize && maximizeWhenTooSmall) window.WindowState = WindowState.Maximized;
        };
    }

    /// <summary>Centres the preferred size within the work area, shrinking it to fit. Maximize is recommended when the preferred size does not fit.</summary>
    public static WindowPlacement Calculate(Rect workArea, Size preferred, Size minimum, double fillRatio = 0)
    {
        var available = new Size(workArea.Width * ScreenMargin, workArea.Height * ScreenMargin);
        var maximize = preferred.Width > available.Width || preferred.Height > available.Height;
        if (fillRatio > 0)
            preferred = new Size(Math.Max(preferred.Width, workArea.Width * fillRatio), Math.Max(preferred.Height, workArea.Height * fillRatio));
        var size = new Size(Math.Min(preferred.Width, available.Width), Math.Min(preferred.Height, available.Height));
        var minimumSize = new Size(Math.Min(minimum.Width, Math.Floor(workArea.Width * 0.8)), Math.Min(minimum.Height, Math.Floor(workArea.Height * 0.8)));
        var bounds = new Rect(
            workArea.Left + Math.Round((workArea.Width - size.Width) / 2),
            workArea.Top + Math.Round((workArea.Height - size.Height) / 2),
            Math.Round(size.Width), Math.Round(size.Height));
        return new WindowPlacement(bounds, minimumSize, maximize);
    }

    /// <summary>Work area (excluding the taskbar) in device-independent units for the owner's monitor, or the monitor under the cursor.</summary>
    private static Rect GetWorkArea(Window window)
    {
        IntPtr monitor;
        if (window.Owner is not null && new WindowInteropHelper(window.Owner).Handle is var owner && owner != IntPtr.Zero)
            monitor = MonitorFromWindow(owner, MonitorDefaultToNearest);
        else
            monitor = GetCursorPos(out var cursor) ? MonitorFromPoint(cursor, MonitorDefaultToNearest) : MonitorFromWindow(new WindowInteropHelper(window).Handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(window);
        var area = info.Work;
        return new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY,
            (area.Right - area.Left) / dpi.DpiScaleX, (area.Bottom - area.Top) / dpi.DpiScaleY);
    }

    private const int MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, int flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
