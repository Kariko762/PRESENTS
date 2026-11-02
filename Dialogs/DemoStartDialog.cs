using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FisPresentationPlatform.Models;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Dialogs;

/// <summary>Pre-launch page: choose the persona / data set, why the demo is being run, deal details and whether to track the session.</summary>
internal static class DemoStartDialog
{
    public static readonly string[] Reasons = ["Client demo", "Internal training", "Rehearsal", "Partner enablement", "Event / conference", "Other"];

    public static DemoLaunchContext? Show(Window owner, LibraryDemo demo, bool trackByDefault, DemoLaunchContext? previous)
    {
        DemoLaunchContext? result = null;
        var window = DialogKit.Create(owner, $"Start {demo.Name}", 720, 760, 560, 460);
        var body = new StackPanel();
        body.Children.Add(DialogKit.Kicker($"Start demo · {demo.Product}"));
        body.Children.Add(DialogKit.Title(demo.Name));
        body.Children.Add(DialogKit.Body($"{demo.VersionLabel} · {demo.OrganizationPath}", 12.5));

        body.Children.Add(DialogKit.Section("Persona & data"));
        var dataSets = demo.Config.DataSets;
        var choices = new List<(RadioButton Radio, DemoDataSet DataSet)>();
        if (dataSets.Count == 0)
            body.Children.Add(DialogKit.Card(DialogKit.Body("This demo has a single built-in data set. Nothing to choose.")));
        else
        {
            var initial = dataSets.FirstOrDefault(set => set.Id == previous?.DataSet?.Id) ?? dataSets.FirstOrDefault(set => set.Default) ?? dataSets[0];
            var list = new UniformGridPanel(dataSets.Count > 2 ? 2 : dataSets.Count);
            foreach (var set in dataSets)
            {
                var radio = new RadioButton { GroupName = "persona", IsChecked = set == initial, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0) };
                System.Windows.Automation.AutomationProperties.SetName(radio, set.Name);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = set.Name, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                if (!string.IsNullOrWhiteSpace(set.Persona))
                    text.Children.Add(new TextBlock { Text = set.Persona, Foreground = DialogKit.Accent, FontSize = 12, Margin = new Thickness(0, 2, 0, 4), TextWrapping = TextWrapping.Wrap });
                if (!string.IsNullOrWhiteSpace(set.Description)) text.Children.Add(DialogKit.Body(set.Description, 12.5));
                var row = new DockPanel();
                DockPanel.SetDock(radio, Dock.Left);
                row.Children.Add(radio);
                row.Children.Add(text);
                var card = DialogKit.Card(row);
                card.Margin = new Thickness(0, 0, 8, 8);
                card.Cursor = Cursors.Hand;
                void Sync() { foreach (var (r, _) in choices) ((Border)((DockPanel)r.Parent).Parent).BorderBrush = r.IsChecked == true ? DialogKit.Accent : DialogKit.Line; }
                card.MouseLeftButtonUp += (_, _) => radio.IsChecked = true;
                radio.Checked += (_, _) => Sync();
                choices.Add((radio, set));
                list.Add(card);
            }
            body.Children.Add(list.Root);
            window.Loaded += (_, _) => { foreach (var (r, _) in choices) ((Border)((DockPanel)r.Parent).Parent).BorderBrush = r.IsChecked == true ? DialogKit.Accent : DialogKit.Line; };
        }

        body.Children.Add(DialogKit.Section("Engagement"));
        var track = new CheckBox
        {
            Content = "Track this session (usage, pages viewed and deal details are logged and uploaded to the demo repository)",
            IsChecked = previous?.Track ?? trackByDefault, Margin = new Thickness(0, 0, 0, 12), Foreground = DialogKit.Muted
        };
        body.Children.Add(track);

        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var reason = new ComboBox { ItemsSource = Reasons, SelectedItem = Reasons.Contains(previous?.Reason) ? previous!.Reason : Reasons[0] };
        var client = new TextBox { Text = previous?.ClientName ?? "", MaxLength = 120 };
        var opportunity = new TextBox { Text = previous?.OpportunityId ?? "", MaxLength = 60 };
        void Place(string label, Control input, int row, int column, string? hint = null)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            cell.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = DialogKit.Faint, Margin = new Thickness(0, 0, 0, 5) });
            cell.Children.Add(input);
            if (hint is not null) cell.Children.Add(new TextBlock { Text = hint, FontSize = 11, Foreground = DialogKit.Faint, Margin = new Thickness(0, 4, 0, 0) });
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            Grid.SetRow(cell, row); Grid.SetColumn(cell, column);
            form.Children.Add(cell);
        }
        Place("Demo reason", reason, 0, 0);
        Place("Client name", client, 0, 2, "Shown in the demo as \"Prepared for…\"");
        Place("Opportunity ID (OID)", opportunity, 1, 0, "Logged only. Never shown on screen.");
        body.Children.Add(form);
        void SyncForm() => form.Opacity = track.IsChecked == true ? 1 : 0.85;
        track.Checked += (_, _) => SyncForm();
        track.Unchecked += (_, _) => SyncForm();

        var start = DialogKit.Primary(owner, "Start demo →");
        var cancel = DialogKit.Secondary("Cancel", cancel: true);
        start.Click += (_, _) =>
        {
            result = new DemoLaunchContext
            {
                Track = track.IsChecked == true, Reason = reason.SelectedItem as string ?? "", ClientName = client.Text.Trim(),
                OpportunityId = opportunity.Text.Trim(), DataSet = choices.FirstOrDefault(choice => choice.Radio.IsChecked == true).DataSet
            };
            window.Close();
        };
        window.Content = DialogKit.Layout(body, DialogKit.Buttons(cancel, start));
        window.Loaded += (_, _) => client.Focus();
        window.ShowDialog();
        return result;
    }

    /// <summary>Lays persona cards out in equal columns.</summary>
    private sealed class UniformGridPanel(int columns)
    {
        public System.Windows.Controls.Primitives.UniformGrid Root { get; } = new() { Columns = Math.Max(1, columns), Margin = new Thickness(0, 0, -8, 0) };
        public void Add(UIElement element) => Root.Children.Add(element);
    }
}
