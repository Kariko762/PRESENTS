using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Dialogs;

internal sealed record AboutFacts(
    string AppVersion, string DotNetVersion, string WebViewVersion, string User, string Machine, string LibraryPath, string RepositoryPath,
    int DemosInstalled, int Favorites, int AssignedSolutions, int UpdatesAvailable, string RepositoryStatus, UsageStats Usage);

/// <summary>About FIS Presents: versions, local stats, support contacts.</summary>
internal static class AboutDialog
{
    // Placeholder contacts until the platform team confirms the support model.
    public const string SupportEmail = "demo-platform-support@fisglobal.example";
    public const string SupportChannel = "Teams › FIS Presents › Support";
    public const string Owner = "Pre-Sales Enablement · Demo Platform team";

    /// <returns>True when the user asked to open the log viewer.</returns>
    public static bool Show(Window owner, AboutFacts facts)
    {
        var openLogs = false;
        var window = DialogKit.Create(owner, "About FIS Presents", 680, 720, 520, 420);
        var body = new StackPanel();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (TryLoadLogo() is { } logo)
        {
            var image = new Image { Source = logo, Width = 76, Height = 40, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(image, Dock.Left);
            header.Children.Add(image);
        }
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(DialogKit.Kicker("About"));
        var title = DialogKit.Title("FIS Presents", 26);
        title.Margin = new Thickness(0, 2, 0, 0);
        titles.Children.Add(title);
        header.Children.Add(titles);
        body.Children.Add(header);
        body.Children.Add(DialogKit.Body("Presentation Studio for pre-sales consultants: browse, download, configure and deliver branded digital demos from a governed repository, with the same confident experience every time."));

        var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 18, -8, 0) };
        void Stat(string value, string label)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = DialogKit.Accent });
            panel.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = DialogKit.Faint, TextWrapping = TextWrapping.Wrap });
            var card = DialogKit.Card(panel);
            card.Margin = new Thickness(0, 0, 8, 8);
            stats.Children.Add(card);
        }
        Stat(facts.DemosInstalled.ToString(), "Demos installed");
        Stat(facts.Favorites.ToString(), "Favorites");
        Stat(facts.Usage.TrackedSessions.ToString(), "Tracked sessions");
        Stat(facts.UpdatesAvailable.ToString(), "Updates available");
        body.Children.Add(stats);

        body.Children.Add(DialogKit.Section("Versions"));
        body.Children.Add(DialogKit.Card(DialogKit.Facts(
        [
            ("Studio", facts.AppVersion),
            (".NET runtime", facts.DotNetVersion),
            ("WebView2", facts.WebViewVersion),
            ("Package format", "fisxpkg · schema 1.0")
        ])));

        body.Children.Add(DialogKit.Section("This PC"));
        body.Children.Add(DialogKit.Card(DialogKit.Facts(
        [
            ("User", facts.User),
            ("Machine", facts.Machine),
            ("Assigned solutions", facts.AssignedSolutions == 0 ? "None (all demos shown)" : facts.AssignedSolutions.ToString()),
            ("Repository", $"{facts.RepositoryStatus} · {facts.RepositoryPath}"),
            ("Local library", facts.LibraryPath),
            ("Usage queue", $"{facts.Usage.PendingFiles} waiting · {facts.Usage.UploadedFiles} uploaded" + (facts.Usage.LastUploadUtc is { } last ? $" · last {last.LocalDateTime:g}" : "")),
            ("Logs", AppLog.Folder)
        ])));

        body.Children.Add(DialogKit.Section("Support"));
        body.Children.Add(DialogKit.Card(DialogKit.Facts(
        [
            ("Owner", Owner),
            ("Email", SupportEmail),
            ("Channel", SupportChannel),
            ("Reporting a problem", "Open View logs, then attach today's log file to your request.")
        ])));

        var copy = DialogKit.Secondary("Copy details");
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText($"FIS Presents {facts.AppVersion} · .NET {facts.DotNetVersion} · WebView2 {facts.WebViewVersion}\nUser {facts.User} on {facts.Machine}\nRepository: {facts.RepositoryStatus} ({facts.RepositoryPath})\nLibrary: {facts.LibraryPath} · {facts.DemosInstalled} demos");
                copy.Content = "Copied ✓";
            }
            catch (System.Runtime.InteropServices.ExternalException) { copy.Content = "Clipboard busy"; }
        };
        var logs = DialogKit.Secondary("View logs");
        logs.Click += (_, _) => { openLogs = true; window.Close(); };
        var close = DialogKit.Primary(owner, "Close");
        close.IsCancel = true;
        close.Click += (_, _) => window.Close();
        window.Content = DialogKit.Layout(body, DialogKit.Buttons(copy, logs, close));
        window.ShowDialog();
        return openLogs;
    }

    private static ImageSource? TryLoadLogo()
    {
        try { return new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/fis-logo.png")); }
        catch (Exception) { return null; }
    }
}
