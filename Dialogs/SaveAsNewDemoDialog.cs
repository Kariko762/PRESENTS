using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Dialogs;

internal sealed record NewDemoIdentity(string Name, string Id, string Version);

/// <summary>Asks for the identity of a copy made with "Save as new demo".</summary>
internal static class SaveAsNewDemoDialog
{
    private static readonly Regex IdPattern = new("^[a-zA-Z0-9][a-zA-Z0-9._-]{1,79}$", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant);

    public static NewDemoIdentity? Show(Window owner, string currentName, string currentId, Func<string, bool> idTaken)
    {
        NewDemoIdentity? result = null;
        var window = DialogKit.Create(owner, "Save as new demo", 600, 520, 480, 420);
        var body = new StackPanel();
        body.Children.Add(DialogKit.Kicker("Save as new demo"));
        body.Children.Add(DialogKit.Title($"Copy of {currentName}"));
        body.Children.Add(DialogKit.Body("Creates a new demo from the current settings and pages: a new .fisxpkg package that is added to your Demo Library. The original demo is not changed."));

        var name = new TextBox { Text = $"{currentName} (copy)", MaxLength = 120 };
        var id = new TextBox { Text = DemoLibraryService.SuggestId($"{currentName} copy", currentId), MaxLength = 80 };
        var version = new TextBox { Text = "1.0.0", MaxLength = 40 };
        var idEdited = false;
        name.TextChanged += (_, _) => { if (!idEdited) id.Text = DemoLibraryService.SuggestId(name.Text, currentId); };
        id.PreviewTextInput += (_, _) => idEdited = true;

        var form = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Place(string label, TextBox input, int row, int column, int span = 1, string? hint = null)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            cell.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = DialogKit.Faint, Margin = new Thickness(0, 0, 0, 5) });
            cell.Children.Add(input);
            if (hint is not null) cell.Children.Add(new TextBlock { Text = hint, FontSize = 11, Foreground = DialogKit.Faint, Margin = new Thickness(0, 4, 0, 0) });
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            Grid.SetRow(cell, row); Grid.SetColumn(cell, column); Grid.SetColumnSpan(cell, span);
            form.Children.Add(cell);
        }
        Place("Demo name", name, 0, 0, 3);
        Place("Demo ID", id, 1, 0, hint: "Unique · letters, numbers, dots, dashes");
        Place("Version", version, 1, 2);
        body.Children.Add(form);
        var error = new TextBlock { Foreground = DialogKit.Frozen(0xFF, 0x8B, 0x7D), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(error);

        var save = DialogKit.Primary(owner, "Create new demo");
        var cancel = DialogKit.Secondary("Cancel", cancel: true);
        save.Click += (_, _) =>
        {
            var problem = string.IsNullOrWhiteSpace(name.Text) ? "Enter a demo name."
                : !IdPattern.IsMatch(id.Text) ? "The ID must be 2–80 letters, numbers, dots, underscores or hyphens."
                : id.Text.Equals(currentId, StringComparison.OrdinalIgnoreCase) || idTaken(id.Text) ? $"'{id.Text}' is already used by another demo. Choose a different ID."
                : !VersionPattern.IsMatch(version.Text) ? "Use a version like 1.0.0."
                : null;
            if (problem is not null) { error.Text = problem; return; }
            result = new NewDemoIdentity(name.Text.Trim(), id.Text.Trim(), version.Text.Trim());
            window.Close();
        };
        window.Content = DialogKit.Layout(body, DialogKit.Buttons(cancel, save));
        window.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        window.ShowDialog();
        return result;
    }
}
