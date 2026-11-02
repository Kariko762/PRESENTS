using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Dialogs;

/// <summary>Rich demo details: cover, organization path, catalog chips, personas, agenda and package facts.</summary>
internal static class DemoDetailsDialog
{
    /// <returns>True when the user chose to launch the demo.</returns>
    public static bool Show(Window owner, LibraryDemo demo)
    {
        var launch = false;
        var window = DialogKit.Create(owner, demo.Name, 760, 780, 560, 460);
        var body = new StackPanel();

        var branding = demo.Config.Branding;
        var cover = TileThemes.Render(branding.TileTheme, branding.AccentColor, demo.Config.Id, TileThemes.FisLogo, demo.Config.Catalog.Organization.L3, demo.Name);
        body.Children.Add(new Border
        {
            Height = 150, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 18), ClipToBounds = true,
            Background = new ImageBrush(cover) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Bottom }
        });
        body.Children.Add(DialogKit.Kicker($"{demo.Product} · {demo.VersionLabel}"));
        body.Children.Add(DialogKit.Title(demo.Name));
        body.Children.Add(DialogKit.Body(string.IsNullOrWhiteSpace(demo.Description) ? "No description provided." : demo.Description, 14));

        var organization = demo.Config.Catalog.Organization;
        body.Children.Add(DialogKit.Section("Organization"));
        if (organization.IsEmpty) body.Children.Add(DialogKit.Body("No organizational tags."));
        else
        {
            var path = new WrapPanel();
            var levels = new[] { ("L3", organization.L3), ("L4", organization.L4), ("L5", organization.L5), ("L6", organization.L6), ("L7", organization.L7), ("L8", organization.L8) }
                .Where(level => !string.IsNullOrWhiteSpace(level.Item2)).ToList();
            for (var i = 0; i < levels.Count; i++)
            {
                var (level, value) = levels[i];
                var solution = level == "L8";
                var chip = DialogKit.Chip($"{level}  {value}", solution ? DialogKit.Accent : null, solution ? Brushes.White : null);
                path.Children.Add(chip);
                if (i < levels.Count - 1) path.Children.Add(new TextBlock { Text = "›", Foreground = DialogKit.Faint, Margin = new Thickness(0, 3, 6, 0) });
            }
            body.Children.Add(path);
        }

        var catalog = demo.Config.Catalog;
        if (catalog.Functions.Count > 0 || catalog.Tags.Count > 0)
        {
            body.Children.Add(DialogKit.Section("Functions & tags"));
            var chips = DialogKit.Chips(catalog.Functions, DialogKit.Accent, Brushes.White);
            foreach (var tag in catalog.Tags) chips.Children.Add(DialogKit.Chip("#" + tag));
            body.Children.Add(chips);
        }

        if (demo.Config.DataSets.Count > 0)
        {
            body.Children.Add(DialogKit.Section("Personas & data"));
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = Math.Min(2, demo.Config.DataSets.Count), Margin = new Thickness(0, 0, -8, 0) };
            foreach (var set in demo.Config.DataSets)
            {
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = set.Name + (set.Default ? "  · default" : ""), FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap });
                if (!string.IsNullOrWhiteSpace(set.Persona)) text.Children.Add(new TextBlock { Text = set.Persona, Foreground = DialogKit.Accent, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
                var card = DialogKit.Card(text);
                card.Margin = new Thickness(0, 0, 8, 8);
                grid.Children.Add(card);
            }
            body.Children.Add(grid);
        }

        var sections = demo.Config.Navigation.Sections;
        body.Children.Add(DialogKit.Section($"Agenda · {sections.Sum(section => section.Pages.Count)} pages"));
        var agenda = new StackPanel();
        foreach (var section in sections)
        {
            agenda.Children.Add(new TextBlock { Text = section.Title, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 4, 0, 2) });
            agenda.Children.Add(DialogKit.Body(string.Join("  ·  ", section.Pages.Select(page => page.Title)), 12.5));
        }
        body.Children.Add(DialogKit.Card(agenda));

        body.Children.Add(DialogKit.Section("Package"));
        body.Children.Add(DialogKit.Card(DialogKit.Facts(
        [
            ("Version", demo.VersionLabel),
            ("Status", demo.StatusLabel),
            ("Package size", demo.PackageBytes >= 1024 * 1024 ? $"{demo.PackageBytes / 1048576d:F1} MB" : $"{demo.PackageBytes / 1024d:F1} KB"),
            ("Imported", demo.ImportedUtc.LocalDateTime.ToString("g")),
            ("Last launched", demo.LastLaunchedUtc?.LocalDateTime.ToString("g") ?? "Never"),
            ("Demo ID", demo.Config.Id),
            ("Location", demo.PackagePath)
        ])));

        var close = DialogKit.Secondary("Close", cancel: true);
        var start = DialogKit.Primary(owner, "Launch demo →");
        start.IsEnabled = demo.Available;
        start.Click += (_, _) => { launch = true; window.Close(); };
        window.Content = DialogKit.Layout(body, DialogKit.Buttons(close, start));
        window.ShowDialog();
        return launch;
    }
}
