using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FisPresentationPlatform.Dialogs;

/// <summary>Shared building blocks for the code-built dark modal dialogs.</summary>
internal static class DialogKit
{
    public static readonly Brush Surface = Frozen(0x10, 0x19, 0x25);
    public static readonly Brush Panel = Frozen(0x16, 0x23, 0x33);
    public static readonly Brush Line = Frozen(0x2A, 0x3A, 0x4C);
    public static readonly Brush Accent = Frozen(0x69, 0xE4, 0x3C);
    public static readonly Brush Muted = Frozen(0xBE, 0xC4, 0xC2);
    public static readonly Brush Faint = Frozen(0x86, 0x93, 0x9E);

    public static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static Window Create(Window owner, string title, double width, double height, double minWidth = 520, double minHeight = 420)
    {
        var window = new Window
        {
            Owner = owner, Title = title, Background = Surface, Foreground = Brushes.White,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, ResizeMode = ResizeMode.CanResize,
            FontFamily = owner.FontFamily
        };
        Controls.WindowFitter.Attach(window, new Size(width, height), new Size(minWidth, minHeight), maximizeWhenTooSmall: false);
        return window;
    }

    public static TextBlock Kicker(string text, Brush? brush = null) =>
        new() { Text = text.ToUpperInvariant(), Foreground = brush ?? Accent, FontSize = 11, FontWeight = FontWeights.Bold };

    public static TextBlock Title(string text, double size = 24) =>
        new() { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };

    public static TextBlock Body(string text, double size = 13.5) =>
        new() { Text = text, FontSize = size, LineHeight = size * 1.55, TextWrapping = TextWrapping.Wrap, Foreground = Muted };

    public static TextBlock Section(string text) =>
        new() { Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Faint, Margin = new Thickness(0, 18, 0, 8) };

    public static Border Chip(string text, Brush? border = null, Brush? foreground = null) => new()
    {
        BorderBrush = border ?? Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
        Background = Panel, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6),
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = foreground ?? Muted }
    };

    public static WrapPanel Chips(IEnumerable<string> values, Brush? border = null, Brush? foreground = null)
    {
        var panel = new WrapPanel();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value))) panel.Children.Add(Chip(value, border, foreground));
        return panel;
    }

    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Background = Panel, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
        Padding = padding ?? new Thickness(14, 12, 14, 12), Child = child
    };

    /// <summary>Two-column label/value grid used for facts such as version, size and dates.</summary>
    public static Grid Facts(IEnumerable<(string Label, string Value)> rows, double labelWidth = 130)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var index = 0;
        foreach (var (label, value) in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = label, Foreground = Faint, FontSize = 12.5, Margin = new Thickness(0, 3, 10, 3) };
            var text = new TextBlock { Text = value, Foreground = Brushes.White, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(name, index); Grid.SetRow(text, index); Grid.SetColumn(text, 1);
            grid.Children.Add(name); grid.Children.Add(text);
            index++;
        }
        return grid;
    }

    /// <summary>Scrollable body with a fixed footer button row, so actions stay visible on short screens.</summary>
    public static Grid Layout(UIElement body, UIElement footer)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(28, 24, 28, 8) };
        var bar = new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(28, 14, 28, 16), Child = footer };
        Grid.SetRow(bar, 1);
        root.Children.Add(scroll);
        root.Children.Add(bar);
        return root;
    }

    public static StackPanel Buttons(params UIElement[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in buttons) panel.Children.Add(button);
        return panel;
    }

    public static Button Primary(Window window, string text)
    {
        var button = new Button { Content = text, IsDefault = true, Margin = new Thickness(10, 0, 0, 0) };
        if (window.TryFindResource("PrimaryButtonStyle") is Style style) button.Style = style;
        button.Margin = new Thickness(10, 0, 0, 0);
        return button;
    }

    public static Button Secondary(string text, bool cancel = false) => new() { Content = text, IsCancel = cancel, Margin = new Thickness(10, 0, 0, 0) };
}
