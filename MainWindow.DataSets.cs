using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FisPresentationPlatform.Models;
using FisPresentationPlatform.Services;
using Microsoft.Win32;

namespace FisPresentationPlatform;

/// <summary>Configuration Studio "Data Sets" tab: create, edit, check and preview the data sets of the demo being edited.</summary>
public partial class MainWindow
{
    private sealed record DataSetRow(DemoDataSet Set, string Title, string Meta, string Badge, Brush BadgeBrush);

    private static readonly Brush BadgeOk = Frozen(0x4B, 0xCD, 0x3E);
    private static readonly Brush BadgeWarn = Frozen(0xFF, 0xC4, 0x5C);
    private static readonly Brush BadgeError = Frozen(0xFF, 0x8B, 0x7D);
    private static readonly Brush BadgeNone = Frozen(0x86, 0x93, 0x9E);

    private string _dataSetJsonOriginal = "";
    private string? _selectedDataSetId;
    private bool _renderingDataSets;

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private bool DataSetDirty => DataSetDetailGrid.IsEnabled && DataSetJsonText.Text != _dataSetJsonOriginal;

    private void RenderDataSets(PresentationConfig config, string? selectId = null)
    {
        if (_sourceDirectory is null) return;
        _renderingDataSets = true;
        try
        {
            var rows = config.DataSets.Select(set =>
            {
                var health = DataSetService.Inspect(_sourceDirectory, set);
                var badge = string.IsNullOrWhiteSpace(set.File) ? "no file"
                    : !health.Ok ? "error"
                    : DataSetResolver.FormatBytes(health.Bytes);
                var brush = string.IsNullOrWhiteSpace(set.File) ? BadgeNone : !health.Ok ? BadgeError : health.Warnings.Count > 0 ? BadgeWarn : BadgeOk;
                var meta = $"{(set.Default ? "★ default · " : "")}{set.Id}{(string.IsNullOrWhiteSpace(set.Persona) ? "" : " · " + set.Persona)}";
                return new DataSetRow(set, set.Name, meta, badge, brush);
            }).ToList();
            DataSetsListBox.ItemsSource = rows;
            DataSetsSummaryText.Text = rows.Count == 0
                ? "No data sets yet. Pages use their built-in defaults. Add one to offer personas on the start dialog."
                : $"{rows.Count} data set{(rows.Count == 1 ? "" : "s")} · offered on the demo start dialog.";
            var target = selectId ?? _selectedDataSetId;
            var index = rows.FindIndex(row => row.Set.Id.Equals(target, StringComparison.OrdinalIgnoreCase));
            DataSetsListBox.SelectedIndex = rows.Count == 0 ? -1 : Math.Max(index, 0);
        }
        finally { _renderingDataSets = false; }
        ShowSelectedDataSet();
    }

    private void DataSetsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_renderingDataSets) return;
        if (DataSetDirty && e.RemovedItems.Count > 0 && e.RemovedItems[0] is DataSetRow previous
            && MessageBox.Show(this, $"Discard unsaved changes to '{previous.Set.Name}'?", "Unsaved data set", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            _renderingDataSets = true;
            DataSetsListBox.SelectedItem = previous;
            _renderingDataSets = false;
            return;
        }
        ShowSelectedDataSet();
    }

    private void ShowSelectedDataSet()
    {
        if (DataSetsListBox.SelectedItem is not DataSetRow row || _sourceDirectory is null)
        {
            _selectedDataSetId = null;
            DataSetDetailGrid.IsEnabled = false;
            DataSetNameText.Text = DataSetIdText.Text = DataSetPersonaText.Text = DataSetDescriptionText.Text = DataSetJsonText.Text = _dataSetJsonOriginal = "";
            DataSetDefaultCheckBox.IsChecked = false;
            DataSetFileText.Text = "DATA FILE";
            DataSetHealthText.Text = "Select or create a data set.";
            DataSetStructureText.Text = "";
            return;
        }
        var set = row.Set;
        var preserveEdits = DataSetDirty && set.Id.Equals(_selectedDataSetId, StringComparison.OrdinalIgnoreCase);
        _selectedDataSetId = set.Id;
        DataSetDetailGrid.IsEnabled = true;
        DataSetNameText.Text = set.Name;
        DataSetIdText.Text = set.Id;
        DataSetPersonaText.Text = set.Persona;
        DataSetDescriptionText.Text = set.Description;
        DataSetDefaultCheckBox.IsChecked = set.Default;
        var file = string.IsNullOrWhiteSpace(set.File) ? "" : set.File;
        DataSetFileText.Text = file.Length == 0 ? $"DATA FILE · none yet (saving creates data/{set.Id}.json)" : $"DATA FILE · {file}";
        if (preserveEdits) { ShowDataSetHealth(set); return; }
        try
        {
            DataSetJsonText.Text = file.Length > 0 && File.Exists(DataSetResolver.Combine(_sourceDirectory, file))
                ? DataSetService.ReadText(_sourceDirectory, file)
                : "{\n  \"persona\": \"\"\n}";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            DataSetJsonText.Text = "";
            DataSetHealthText.Text = exception.Message;
        }
        _dataSetJsonOriginal = DataSetJsonText.Text;
        ShowDataSetHealth(set);
    }

    private void ShowDataSetHealth(DemoDataSet set)
    {
        if (_sourceDirectory is null || _currentConfig is null) return;
        var health = DataSetService.Inspect(_sourceDirectory, set);
        var parts = new List<string>();
        if (health.Ok && !string.IsNullOrWhiteSpace(set.File))
        {
            parts.Add($"✓ Valid · {DataSetResolver.FormatBytes(health.Bytes)} injected per page · {health.Fields} fields · {health.Tables} tables · {health.Rows:N0} rows");
            if (health.Chain.Count > 1) parts.Add("Extends " + string.Join(" → ", health.Chain.Skip(1)));
            if (health.Files.Count > 0) parts.Add($"$files: {string.Join(", ", health.Files.Select(file => $"{file.Key} ({DataSetResolver.FormatBytes(new FileInfo(DataSetResolver.Combine(_sourceDirectory, file.Value)).Length)})"))}");
        }
        parts.AddRange(health.Errors.Select(error => "✕ " + error));
        parts.AddRange(health.Warnings.Select(warning => "⚠ " + warning));
        DataSetHealthText.Text = string.Join("   ·   ", parts);
        DataSetHealthText.Foreground = !health.Ok ? BadgeError : health.Warnings.Count > 0 ? BadgeWarn : Brushes.White;

        var baseline = _currentConfig.DataSets.FirstOrDefault(item => item.Default) ?? _currentConfig.DataSets.FirstOrDefault();
        if (baseline is null || ReferenceEquals(baseline, set) || baseline.Id.Equals(set.Id, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(baseline.File) || !health.Ok)
        {
            DataSetStructureText.Text = baseline is not null && baseline.Id.Equals(set.Id, StringComparison.OrdinalIgnoreCase)
                ? "This is the default set — other sets are checked against its structure."
                : "";
            return;
        }
        var expected = DataSetResolver.Resolve(_sourceDirectory, baseline.File).Data;
        var actual = DataSetResolver.Resolve(_sourceDirectory, set.File).Data;
        var comparison = DataSetService.Compare(expected, actual);
        DataSetStructureText.Text = comparison.Matches
            ? $"Structure matches '{baseline.Name}'" + (comparison.Extra.Count > 0 ? $" (+{comparison.Extra.Count} extra field{(comparison.Extra.Count == 1 ? "" : "s")})." : ".")
            : $"Compared with '{baseline.Name}': " + string.Join("; ", new[]
            {
                comparison.Missing.Count > 0 ? $"missing {Preview(comparison.Missing)}" : null,
                comparison.TypeChanged.Count > 0 ? $"type changed {Preview(comparison.TypeChanged)}" : null,
                comparison.Extra.Count > 0 ? $"extra {Preview(comparison.Extra)}" : null
            }.Where(text => text is not null)) + ". Pages that read missing fields fall back to their defaults.";
        DataSetStructureText.Foreground = comparison.Matches ? (Brush)FindResource("MutedBrush") : BadgeWarn;
    }

    private static string Preview(IReadOnlyList<string> items) =>
        string.Join(", ", items.Take(5)) + (items.Count > 5 ? $" … (+{items.Count - 5})" : "");

    /// <summary>Writes the config to the editor and, when it validates, to presentation.json in the working copy.</summary>
    private bool PersistDataSetConfig(PresentationConfig config, string? selectId, string message)
    {
        var directory = RequireSourceDirectory();
        _selectedDataSetId = selectId;
        RenderConfig(config);
        var errors = _presentationService.ValidateConfiguration(config, directory);
        if (errors.Count > 0)
        {
            EditorStatusText.Text = $"{message} presentation.json was not saved until these are fixed:  " + string.Join("  •  ", errors);
            return false;
        }
        File.WriteAllText(Path.Combine(directory, PresentationService.ConfigurationFileName), _presentationService.Serialize(config));
        EditorStatusText.Text = $"{message} presentation.json saved. Click Save Configuration to update the Demo Library copy.";
        return true;
    }

    private void NewDataSet_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = RequireSourceDirectory();
            if (!ConfirmDiscardDataSet()) return;
            var config = GetEditorConfig(applyMetadata: true);
            var request = Dialogs.NewDataSetDialog.Show(this, config, _selectedDataSetId);
            if (request is null) return;
            var created = DataSetService.Create(directory, config, request.TemplateId, request.Id, request.Name, request.Persona, request.Mode);
            AppLog.Info("Studio", $"Created data set '{created.Id}' ({request.Mode}) in '{config.Id}'.");
            PersistDataSetConfig(config, created.Id, $"Data set '{created.Name}' created as {created.File}.");
        }
        catch (Exception exception) { Notify($"Could not create the data set: {exception.Message}", error: true); }
    }

    private void ImportDataSet_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = RequireSourceDirectory();
            if (!ConfirmDiscardDataSet()) return;
            var dialog = new OpenFileDialog { Title = "Import a data set", Filter = "JSON data (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            var config = GetEditorConfig(applyMetadata: true);
            var name = Path.GetFileNameWithoutExtension(dialog.FileName).Replace('-', ' ').Replace('_', ' ');
            name = name.Length == 0 ? "Imported" : char.ToUpperInvariant(name[0]) + name[1..];
            var created = DataSetService.Import(directory, config, dialog.FileName, DataSetService.SuggestId(config, name), name, "");
            AppLog.Info("Studio", $"Imported data set '{created.Id}' from {dialog.FileName} into '{config.Id}'.");
            PersistDataSetConfig(config, created.Id, $"Imported {Path.GetFileName(dialog.FileName)} as '{created.Name}'.");
        }
        catch (Exception exception) { Notify($"Could not import the data set: {exception.Message}", error: true); }
    }

    private void RemoveDataSet_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = RequireSourceDirectory();
            if (DataSetsListBox.SelectedItem is not DataSetRow row) return;
            if (MessageBox.Show(this, $"Remove data set '{row.Set.Name}'? Its data file is deleted from the working copy if no other data set uses it.",
                    "Remove data set", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var config = GetEditorConfig(applyMetadata: true);
            var deleted = DataSetService.Remove(directory, config, row.Set.Id);
            _dataSetJsonOriginal = DataSetJsonText.Text;
            AppLog.Info("Studio", $"Removed data set '{row.Set.Id}' from '{config.Id}'" + (deleted.Count > 0 ? $" and deleted {string.Join(", ", deleted)}." : "."));
            PersistDataSetConfig(config, null, $"Data set '{row.Set.Name}' removed.");
        }
        catch (Exception exception) { Notify(exception.Message, error: true); }
    }

    /// <summary>Applies the detail fields and JSON text of the selected data set, writing the data file. Returns the saved set.</summary>
    private DemoDataSet? SaveSelectedDataSet()
    {
        var directory = RequireSourceDirectory();
        var config = GetEditorConfig(applyMetadata: true);
        var set = config.DataSets.FirstOrDefault(item => item.Id.Equals(_selectedDataSetId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Select a data set first.");
        var newId = DataSetIdText.Text.Trim();
        if (!DataSetService.IsValidId(newId)) throw new InvalidOperationException("Data set ids use letters, numbers, '.', '_' or '-' and start with a letter or number.");
        if (!newId.Equals(set.Id, StringComparison.OrdinalIgnoreCase) && config.DataSets.Any(item => item.Id.Equals(newId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Another data set already uses the id '{newId}'.");
        if (string.IsNullOrWhiteSpace(DataSetNameText.Text)) throw new InvalidOperationException("Enter a data set name.");

        // Parse first so nothing is written when the JSON is broken.
        DataSetService.Parse(DataSetJsonText.Text);
        if (string.IsNullOrWhiteSpace(set.File))
        {
            set.File = $"{DataSetService.DataFolder}/{newId}.json";
            if (File.Exists(DataSetResolver.Combine(directory, set.File))) throw new InvalidOperationException($"{set.File} already exists; choose another id.");
        }
        DataSetJsonText.Text = DataSetService.SaveText(directory, set.File, DataSetJsonText.Text);
        _dataSetJsonOriginal = DataSetJsonText.Text;

        set.Id = newId;
        set.Name = DataSetNameText.Text.Trim();
        set.Persona = DataSetPersonaText.Text.Trim();
        set.Description = DataSetDescriptionText.Text.Trim();
        if (DataSetDefaultCheckBox.IsChecked == true) foreach (var item in config.DataSets) item.Default = ReferenceEquals(item, set);
        else set.Default = false;
        _selectedDataSetId = set.Id;
        PersistDataSetConfig(config, set.Id, $"Data set '{set.Name}' saved to {set.File}.");
        return set;
    }

    private void SaveDataSet_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var set = SaveSelectedDataSet();
            if (set is not null) AppLog.Info("Studio", $"Saved data set '{set.Id}' ({set.File}).");
        }
        catch (Exception exception) { Notify(exception.Message, error: true); }
    }

    private void FormatDataSet_Click(object sender, RoutedEventArgs e)
    {
        try { DataSetJsonText.Text = DataSetService.Parse(DataSetJsonText.Text).ToJsonString(DataSetService.WriteOptions); }
        catch (Exception exception) { Notify(exception.Message, error: true); }
    }

    private void CheckDataSet_Click(object sender, RoutedEventArgs e)
    {
        if (_currentConfig is null || _sourceDirectory is null) return;
        try
        {
            var node = DataSetService.Parse(DataSetJsonText.Text);
            var bytes = System.Text.Encoding.UTF8.GetByteCount(node.ToJsonString());
            var set = _currentConfig.DataSets.FirstOrDefault(item => item.Id.Equals(_selectedDataSetId, StringComparison.OrdinalIgnoreCase));
            if (set is not null) ShowDataSetHealth(set);
            DataSetHealthText.Text = $"✓ JSON parses · {DataSetResolver.FormatBytes(bytes)} in this file{(DataSetDirty ? " (unsaved — the summary below reflects the saved file)" : "")}.   ·   " + DataSetHealthText.Text;
        }
        catch (Exception exception)
        {
            DataSetHealthText.Text = "✕ " + exception.Message;
            DataSetHealthText.Foreground = BadgeError;
        }
    }

    private void RevertDataSet_Click(object sender, RoutedEventArgs e) => DataSetJsonText.Text = _dataSetJsonOriginal;

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.Combine(RequireSourceDirectory(), DataSetService.DataFolder);
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception exception) { Notify(exception.Message, error: true); }
    }

    /// <summary>Saves, builds a throw-away package and opens it untracked in the Player with the selected data set.</summary>
    private async void PreviewDataSet_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var set = SaveSelectedDataSet();
            if (set is null) return;
            var directory = RequireSourceDirectory();
            var config = GetEditorConfig();
            var errors = _presentationService.ValidateConfiguration(config, directory);
            if (errors.Count > 0) { Notify("Fix validation errors before previewing: " + errors[0], error: true); return; }
            var previews = Path.Combine(Path.GetTempPath(), "FISPresents", "preview");
            Directory.CreateDirectory(previews);
            foreach (var old in Directory.EnumerateFiles(previews, "*.fisxpkg")) { try { File.Delete(old); } catch (IOException) { } }
            var package = Path.Combine(previews, $"{config.Id}-{DateTime.Now:HHmmss}.fisxpkg");
            EditorStatusText.Text = $"Building preview with '{set.Name}'…";
            await _presentationService.BuildPackageAsync(directory, package, config);
            EditorStatusText.Text = $"Previewing with '{set.Name}' (untracked). Close the Player to return.";
            var player = new PlayerWindow(package, new DemoLaunchContext { Track = false, DataSet = set, ClientName = "", Reason = "Data set preview" }) { Owner = this };
            player.Show();
        }
        catch (Exception exception) { Notify($"Preview failed: {exception.Message}", error: true); }
    }

    /// <summary>True when the open source folder is the working copy of a demo installed in the Demo Library.</summary>
    private bool IsLibraryWorkingCopy(PresentationConfig config, string directory) =>
        _libraryDemos.Any(demo => demo.Config.Id.Equals(config.Id, StringComparison.OrdinalIgnoreCase))
        && Path.GetFullPath(_libraryService.WorkspacePath(config.Id)).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    /// <summary>Rebuilds the (already saved) working copy and replaces this demo in the Demo Library (same id; the version is whatever General says).</summary>
    private async Task<bool> UpdateLibraryDemoAsync(PresentationConfig config, string directory)
    {
        var package = Path.Combine(Path.GetTempPath(), "FISPresents", $"{config.Id}-{config.Version}-{Guid.NewGuid():N}.fisxpkg");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(package)!);
            var progress = new Progress<PackageBuildProgress>(status => EditorStatusText.Text = $"Updating '{config.Name}' in the Demo Library · {status.Operation}");
            await _presentationService.BuildPackageAsync(directory, package, config, progress);
            var imported = await Task.Run(() => _libraryService.ImportAsync(package));
            await _libraryService.RetireOtherVersionsAsync(imported.Config.Id, imported.Hash);
            await RefreshLibraryAsync();
            AppLog.Info("Library", $"Updated library demo '{config.Name}' ({config.Id}) v{config.Version} from the Configuration Studio ({config.DataSets.Count} data sets).");
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("Library", "Update library demo failed.", exception);
            Notify($"Saved, but the Demo Library copy could not be updated: {exception.Message}", error: true);
            return false;
        }
        finally { try { if (File.Exists(package)) File.Delete(package); } catch (IOException) { } }
    }

    private bool ConfirmDiscardDataSet() =>
        !DataSetDirty || MessageBox.Show(this, "Discard unsaved changes to the open data set?", "Unsaved data set", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
