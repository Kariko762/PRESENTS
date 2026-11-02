using System.Windows;
using System.Windows.Controls;
using FisPresentationPlatform.Models;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Dialogs;

internal sealed record NewDataSetRequest(string Name, string Id, string Persona, string? TemplateId, DataSetCreateMode Mode);

/// <summary>Collects the name, id, persona, template and copy mode for a new data set.</summary>
internal static class NewDataSetDialog
{
    public static NewDataSetRequest? Show(Window owner, PresentationConfig config, string? selectedId)
    {
        NewDataSetRequest? result = null;
        var window = DialogKit.Create(owner, "New data set", 640, 640, 500, 500);
        var body = new StackPanel();
        body.Children.Add(DialogKit.Kicker("New data set"));
        body.Children.Add(DialogKit.Title("Add a data set to this demo"));
        body.Children.Add(DialogKit.Body("A data set is the client, persona and figures the pages show. Start from an existing set so every page keeps finding the fields it expects."));

        var name = new TextBox { MaxLength = 120 };
        var id = new TextBox { MaxLength = 80 };
        var persona = new TextBox { MaxLength = 200 };
        var idEdited = false;
        name.TextChanged += (_, _) => { if (!idEdited) id.Text = DataSetService.SuggestId(config, name.Text); };
        id.PreviewTextInput += (_, _) => idEdited = true;

        var templates = config.DataSets.Where(set => !string.IsNullOrWhiteSpace(set.File)).ToList();
        var template = new ComboBox();
        foreach (var set in templates)
            template.Items.Add(new ComboBoxItem { Content = string.IsNullOrWhiteSpace(set.Name) ? set.Id : set.Name, Tag = set });
        var initial = templates.FirstOrDefault(set => set.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
            ?? templates.FirstOrDefault(set => set.Default) ?? templates.FirstOrDefault();
        template.SelectedIndex = initial is null ? -1 : templates.IndexOf(initial);

        var modes = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        RadioButton Mode(string title, string detail, bool isChecked)
        {
            var radio = new RadioButton { GroupName = "mode", IsChecked = isChecked, Margin = new Thickness(0, 0, 0, 8), Foreground = System.Windows.Media.Brushes.White };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            text.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, Foreground = DialogKit.Faint, TextWrapping = TextWrapping.Wrap });
            radio.Content = text;
            modes.Children.Add(radio);
            return radio;
        }
        var overrideMode = Mode("Override (recommended)", "A small file that $extends the template. Only change what differs — client name, currency, a few rows. Template fixes flow through automatically.", templates.Count > 0);
        var duplicate = Mode("Full copy", "A standalone copy of the template's JSON (and any $files it owns). Use when most of the data will change.", false);
        var blank = Mode("Blank", "Start from an empty object. Pages will fall back to their defaults until you add fields.", templates.Count == 0);
        overrideMode.IsEnabled = duplicate.IsEnabled = templates.Count > 0;
        template.IsEnabled = templates.Count > 0;
        blank.Checked += (_, _) => template.IsEnabled = false;
        overrideMode.Checked += (_, _) => template.IsEnabled = true;
        duplicate.Checked += (_, _) => template.IsEnabled = true;

        var form = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 3; row++) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Place(string label, FrameworkElement input, int row, int column, int span = 1)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            cell.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = DialogKit.Faint, Margin = new Thickness(0, 0, 0, 5) });
            cell.Children.Add(input);
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            Grid.SetRow(cell, row); Grid.SetColumn(cell, column); Grid.SetColumnSpan(cell, span);
            form.Children.Add(cell);
        }
        Place("Data set name (shown on the start dialog)", name, 0, 0);
        Place("ID", id, 0, 2);
        Place("Persona", persona, 1, 0);
        Place("Template", template, 1, 2);
        body.Children.Add(form);
        body.Children.Add(modes);
        var error = new TextBlock { Foreground = DialogKit.Frozen(0xFF, 0x8B, 0x7D), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        body.Children.Add(error);

        var create = DialogKit.Primary(owner, "Create data set");
        var cancel = DialogKit.Secondary("Cancel", cancel: true);
        create.Click += (_, _) =>
        {
            var mode = blank.IsChecked == true ? DataSetCreateMode.Blank : duplicate.IsChecked == true ? DataSetCreateMode.Duplicate : DataSetCreateMode.Override;
            var templateSet = (template.SelectedItem as ComboBoxItem)?.Tag as DemoDataSet;
            var problem = string.IsNullOrWhiteSpace(name.Text) ? "Enter a name."
                : !DataSetService.IsValidId(id.Text) ? "The ID uses letters, numbers, dots, underscores or hyphens."
                : config.DataSets.Any(set => set.Id.Equals(id.Text, StringComparison.OrdinalIgnoreCase)) ? $"'{id.Text}' is already used by another data set."
                : mode != DataSetCreateMode.Blank && templateSet is null ? "Choose a template, or pick Blank."
                : null;
            if (problem is not null) { error.Text = problem; return; }
            result = new NewDataSetRequest(name.Text.Trim(), id.Text.Trim(), persona.Text.Trim(), mode == DataSetCreateMode.Blank ? null : templateSet!.Id, mode);
            window.Close();
        };
        window.Content = DialogKit.Layout(body, DialogKit.Buttons(cancel, create));
        window.Loaded += (_, _) => name.Focus();
        window.ShowDialog();
        return result;
    }
}
