using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FisPresentationPlatform.Models;
using FisPresentationPlatform.Services;
using Microsoft.Win32;

namespace FisPresentationPlatform;

public partial class MainWindow : Window
{
    private readonly PresentationService _presentationService = new();
    private readonly AssetInventoryService _assetInventoryService = new();
    private string? _sourceDirectory;
    private PresentationConfig? _currentConfig;
    private CancellationTokenSource? _buildCancellation;
    private bool _refreshingEditor;
    private bool _metadataDirty;
    private DemoLibraryService _libraryService = new();
    private List<LibraryDemo> _libraryDemos = [];
    private bool _refreshingLibrary;
    private bool _recentLibrary;
    private readonly WelcomeFeedService _welcomeFeed = new();
    private bool _switchingViews;
    private bool _refreshingWelcome;
    private string? _highlightedDemoId;
    private readonly ApplicationSettingsService _settingsService = new();
    private readonly Controls.DisplayScaler _display;
    private bool _renderingSettings;
    private ApplicationSettings _settings = new();
    private readonly System.Windows.Threading.DispatcherTimer _repositoryTimer = new();
    private readonly List<StartupCheck> _startupChecks = [];
    private DemoRepositoryService _repositoryService = new(@"C:\FISDemoRepository", Path.GetTempPath());
    private RepositorySnapshot? _repositorySnapshot;
    private List<DemoAvailability> _availability = [];
    private IReadOnlyList<string> _assignedSolutions = [];
    private bool _refreshingDownloads;
    private bool _downloading;
    private DemoLaunchContext? _lastLaunch;
    private UsageTrackingService _usage = new(new ApplicationSettings().LibraryLocation);

    // Demo IDs that earlier builds installed automatically; demos now come from the repository.
    private static readonly string[] RetiredBundledDemoIds =
        ["fis-example-payment-flow", "fis-example-risk-network", "fis-example-market-orbit", "fis-navigation-baseline", "fis-demo"];

    public MainWindow()
    {
        InitializeComponent();
        Controls.WindowFitter.Attach(this, new Size(1440, 920), new Size(1000, 640), fillRatio: 0.8);
        _display = new Controls.DisplayScaler(this);
        _display.Changed += (_, _) => ApplyDisplayDensity();
        PreviewKeyDown += DisplayShortcut_PreviewKeyDown;
        ConfigIdText.TextChanged += MetadataField_Changed;
        NameText.TextChanged += MetadataField_Changed;
        DescriptionText.TextChanged += MetadataField_Changed;
        VersionText.TextChanged += MetadataField_Changed;
        EntryPointText.TextChanged += MetadataField_Changed;
        AccentText.TextChanged += MetadataField_Changed;
        BrandLogoPathText.TextChanged += MetadataField_Changed;
        foreach (var field in new[] { OrgL4Text, OrgL5Text, OrgL6Text, OrgL7Text, OrgL8Text }) field.TextChanged += MetadataField_Changed;
        TileThemeCombo.ItemsSource = TileThemes.All;
        foreach (var field in new[] { NameText, AccentText, ConfigIdText }) field.TextChanged += (_, _) => UpdateTileThemePreview();
        Loaded += async (_, _) => await RunStartupChecksAsync();
        _repositoryTimer.Tick += async (_, _) => await RefreshWelcomeAsync();
        Closed += (_, _) => { _repositoryTimer.Stop(); _welcomeFeed.Dispose(); AppLog.Info("App", "Studio closed."); };
        AppLog.Written += _ => Dispatcher.BeginInvoke(() => { if (LogsSettingsTab.IsSelected && SettingsTab.IsSelected) RenderLogs(); });
        AppLog.Info("App", $"FIS Presents {UsageTrackingService.AppVersion} started by {DemoRepositoryService.CurrentUser} on {Environment.MachineName}.");
    }

    private async Task RunStartupChecksAsync()
    {
        StartupOverlay.Visibility = Visibility.Visible;
        StartupContinueButton.IsEnabled = false;
        ShowWorkspace("welcome");
        _startupChecks.Clear();
        _startupChecks.AddRange(new[] {
            new StartupCheck("Settings", true), new StartupCheck("Configuration schema", true),
            new StartupCheck("Local storage", true), new StartupCheck("Demo library", true),
            new StartupCheck("Demo repository", false), new StartupCheck("User assignments", false),
            new StartupCheck("Demo updates", false), new StartupCheck("Usage upload", false), new StartupCheck("WebView2 runtime", false),
            new StartupCheck("News feed", false), new StartupCheck("Welcome content", false)
        });
        StartupCheckList.ItemsSource = _startupChecks;
        var (settingsCheck, schemaCheck, storageCheck, libraryCheck, repositoryCheck, assignmentsCheck, updatesCheck, usageCheck, webViewCheck, newsCheck, welcomeCheck) =
            (_startupChecks[0], _startupChecks[1], _startupChecks[2], _startupChecks[3], _startupChecks[4], _startupChecks[5], _startupChecks[6], _startupChecks[7], _startupChecks[8], _startupChecks[9], _startupChecks[10]);

        await settingsCheck.RunAsync(async () =>
        {
            _settings = await _settingsService.LoadAsync();
            if (string.IsNullOrEmpty(_settings.RepositoryUrl)) _settings.RepositoryUrl = await _welcomeFeed.LoadUrlAsync();
            _libraryService = new DemoLibraryService(_settings.LibraryLocation);
            _repositoryService = CreateRepositoryService(_settings);
            _usage = new UsageTrackingService(_settings.LibraryLocation);
            return "Settings loaded and validated.";
        });
        RenderSettings();
        _display.SetPreference(_settings.UiScale);
        await schemaCheck.RunAsync(() =>
        {
            var json = _presentationService.Serialize(new PresentationConfig());
            if (ConfigurationSchemaValidator.Validate(json).Count > 0) throw new InvalidDataException("Embedded schema check failed.");
            return Task.FromResult("Embedded JSON Schema available.");
        });
        await storageCheck.RunAsync(() => Task.Run(() =>
        {
            foreach (var folder in new[] { _settings.LibraryLocation, _settings.CacheLocation, _settings.BackupLocation })
            {
                ContentFileSafety.EnsureNoReparseAncestors(folder); Directory.CreateDirectory(folder);
                var probe = Path.Combine(folder, ".startup-probe-" + Guid.NewGuid().ToString("N"));
                try { File.WriteAllText(probe, ""); } finally { if (File.Exists(probe)) File.Delete(probe); }
            }
            var drive = new DriveInfo(Path.GetPathRoot(_settings.CacheLocation)!);
            if (drive.AvailableFreeSpace < 64L * 1024 * 1024) throw new IOException("Less than 64 MiB free on the cache volume.");
            return "Library, cache and backup folders writable; cache volume has free space.";
        }));
        await libraryCheck.RunAsync(async () =>
        {
            var retired = await RetireBundledDemosOnceAsync();
            var demos = await _libraryService.LoadAsync();
            return $"{demos.Count(demo => demo.Available)} demos available locally" + (retired > 0 ? $" · {retired} legacy examples retired" : "") + ".";
        });

        var localReady = !_startupChecks.Take(4).Any(check => check.Status == "Failed");
        if (!localReady) repositoryCheck.Skip("Skipped because required local checks failed.");
        else await repositoryCheck.RunAsync(async () =>
        {
            _repositorySnapshot = await _repositoryService.LoadIndexAsync();
            if (_repositorySnapshot.FromCache) throw new IOException(_repositorySnapshot.Message);
            return _repositorySnapshot.Message;
        });
        if (!localReady) assignmentsCheck.Skip("Skipped because required local checks failed.");
        else await assignmentsCheck.RunAsync(async () =>
        {
            var (solutions, fromCache) = await _repositoryService.LoadAssignmentsAsync();
            _assignedSolutions = solutions;
            var summary = solutions.Count == 0 ? $"No solutions assigned to {DemoRepositoryService.CurrentUser}; showing all demos."
                : $"{DemoRepositoryService.CurrentUser}: {string.Join(", ", solutions)}";
            if (fromCache) throw new IOException("Using cached assignments · " + summary);
            return summary;
        });
        ApplyAssignmentDefaults();
        if (_repositorySnapshot is null) updatesCheck.Skip("No repository index available.");
        else await updatesCheck.RunAsync(async () =>
        {
            _availability = DemoRepositoryService.Compare(_repositorySnapshot.Index, await _libraryService.LoadAsync());
            var updates = _availability.Count(item => item.State == DemoAvailabilityState.UpdateAvailable);
            var available = _availability.Count(item => item.State == DemoAvailabilityState.NotInstalled);
            return $"{updates} updates · {available} new demos available to download.";
        });

        _usage.RecordAppStart();
        if (!_settings.UploadUsage) usageCheck.Skip("Usage upload is turned off in Settings.");
        else if (repositoryCheck.Status != "Passed") usageCheck.Skip("Repository not reachable; usage stays queued on this PC.");
        else _ = usageCheck.RunAsync(async () =>
        {
            var uploaded = await _usage.UploadAsync(_repositoryService.Root);
            AppLog.Info("Usage", $"{uploaded} usage files uploaded to the repository.");
            return uploaded == 0 ? "Nothing queued to upload." : $"{uploaded} usage files uploaded in the background.";
        });

        await webViewCheck.RunAsync(() => Task.Run(() =>
            $"Installed WebView2: {Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString()}"));
        WelcomeFeedResult? feed = null;
        if (!_settings.CheckOnLaunch || string.IsNullOrWhiteSpace(_settings.RepositoryUrl))
            newsCheck.Skip(_settings.CheckOnLaunch ? "No news feed configured; offline sample news will be used." : "Launch network check disabled in Settings.");
        else
            await newsCheck.RunAsync(async () =>
            {
                feed = await _welcomeFeed.RefreshAsync(_settings.RepositoryUrl);
                if (feed.Source != "Live") throw new IOException(feed.Message);
                return "Configured HTTPS endpoint responded with a valid published-content feed.";
            });
        await welcomeCheck.RunAsync(async () =>
        {
            feed ??= string.IsNullOrWhiteSpace(_settings.RepositoryUrl) ? await _welcomeFeed.LoadSampleAsync()
                : await _welcomeFeed.RefreshAsync(_settings.RepositoryUrl, checkRemote: false);
            WelcomeItems.ItemsSource = feed.Items;
            WelcomeEmptyState.Visibility = feed.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            WelcomeStatusText.Text = $"{feed.Source} · {feed.Message}";
            if (feed.Source == "Unavailable") throw new IOException(feed.Message);
            return $"{feed.Items.Count} posts · {feed.Source}";
        });
        await RefreshLibraryAsync();
        UpdateDownloadsView();
        ConfigureRepositoryTimer();

        var failures = _startupChecks.Count(check => check.Status == "Failed");
        var warnings = _startupChecks.Count(check => check.Status == "Warning");
        foreach (var check in _startupChecks.Where(check => check.Status is "Failed" or "Warning"))
            AppLog.Warn("Startup", $"{check.Name}: {check.Status} · {check.Message}");
        StartupSummaryText.Text = failures > 0 ? $"{failures} required checks failed. Open Settings to review the configuration."
            : $"Startup complete · {warnings} warnings. Hover a check for details.";
        if (failures == 0) await OfferStartupUpdatesAsync(updatesCheck);
        StartupContinueButton.Content = failures > 0 ? "Open Settings" : "Open Studio →";
        StartupContinueButton.IsEnabled = true;
    }

    private DemoRepositoryService CreateRepositoryService(ApplicationSettings settings) =>
        new(settings.DemoRepositoryPath, Path.Combine(settings.LibraryLocation, "repository-cache"));

    private async Task<int> RetireBundledDemosOnceAsync()
    {
        var marker = Path.Combine(_libraryService.Root, ".legacy-examples-retired");
        if (File.Exists(marker)) return 0;
        var retired = await _libraryService.RemoveAsync(demo => RetiredBundledDemoIds.Contains(demo.Config.Id, StringComparer.OrdinalIgnoreCase));
        Directory.CreateDirectory(_libraryService.Root);
        await File.WriteAllTextAsync(marker, DateTimeOffset.UtcNow.ToString("O"));
        return retired;
    }

    private async Task OfferStartupUpdatesAsync(StartupCheck updatesCheck)
    {
        var updates = _availability.Where(item => item.State == DemoAvailabilityState.UpdateAvailable).ToList();
        if (updates.Count == 0) return;
        var lines = string.Join("\n", updates.Select(item => $"•  {item.Demo.Name}   v{item.Installed!.Config.Version} → v{item.Latest.Version}"
            + (string.IsNullOrWhiteSpace(item.Latest.ReleaseNotes) ? "" : $"\n     {item.Latest.ReleaseNotes}")));
        var title = updates.Count == 1 ? "A demo update is available" : $"{updates.Count} demo updates are available";
        if (!Confirm(title, $"{lines}\n\nUpdate now? The new version is downloaded from the repository, verified and replaces the older copy in your library.", "Yes, update", "Not now"))
        {
            StartupSummaryText.Text += " Updates postponed — available from Downloads.";
            return;
        }
        await updatesCheck.RunAsync(async () =>
        {
            var updated = await DownloadDemosAsync(updates, text => StartupSummaryText.Text = text);
            return $"{updated} of {updates.Count} demos updated.";
        });
        StartupSummaryText.Text = updatesCheck.Status == "Passed" ? $"Startup complete · {updatesCheck.Message}" : $"Update problem: {updatesCheck.Message}";
    }

    private async Task<int> DownloadDemosAsync(IReadOnlyList<DemoAvailability> items, Action<string> report)
    {
        _downloading = true;
        var completed = 0;
        Exception? firstError = null;
        try
        {
            foreach (var item in items)
            {
                var prefix = $"[{completed + 1}/{items.Count}] {item.Demo.Name} v{item.Latest.Version}";
                var progress = new Progress<int>(percent => report($"{prefix} · {(percent < 85 ? "downloading" : "verifying and installing")} {percent}%"));
                try
                {
                    var from = item.Installed?.Config.Version;
                    await Task.Run(() => _repositoryService.DownloadAsync(item.Demo, item.Latest, _libraryService, progress));
                    completed++;
                    AppLog.Info("Repository", from is null ? $"Downloaded {item.Demo.Name} v{item.Latest.Version}." : $"Updated {item.Demo.Name} v{from} → v{item.Latest.Version}.");
                }
                catch (Exception exception) { firstError ??= exception; AppLog.Error("Repository", $"{item.Demo.Name} v{item.Latest.Version} could not be installed.", exception); }
            }
        }
        finally
        {
            _downloading = false;
            await RefreshLibraryAsync();
            await RefreshAvailabilityAsync();
        }
        if (firstError is not null) throw new InvalidOperationException($"{completed} of {items.Count} completed. {firstError.Message}", firstError);
        return completed;
    }

    private bool Confirm(string title, string body, string yes, string no)
    {
        var accepted = false;
        var dialog = new Window
        {
            Owner = this, Title = title, Width = 600, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(16, 25, 37)), Foreground = Brushes.White, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var panel = new StackPanel { Margin = new Thickness(30) };
        panel.Children.Add(new TextBlock { Text = "FIS PRESENTS · UPDATES", Foreground = new SolidColorBrush(Color.FromRgb(105, 228, 60)), FontSize = 11, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 14) });
        panel.Children.Add(new TextBlock { Text = body, FontSize = 14, LineHeight = 22, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(190, 196, 194)), Margin = new Thickness(0, 0, 0, 22) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = no, IsCancel = true };
        var accept = new Button { Content = yes, IsDefault = true, Style = (Style)FindResource("PrimaryButtonStyle"), Margin = new Thickness(0) };
        accept.Click += (_, _) => { accepted = true; dialog.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.ShowDialog();
        return accepted;
    }

    private void StartupContinue_Click(object sender, RoutedEventArgs e)
    {
        StartupOverlay.Visibility = Visibility.Collapsed;
        if (_startupChecks.Any(check => check.Status == "Failed")) ShowWorkspace("settings");
    }

    private void BuilderNav_Click(object sender, RoutedEventArgs e) => ShowWorkspace("builder");
    private void WelcomeNav_Click(object sender, RoutedEventArgs e) => ShowWorkspace("welcome");
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowWorkspace("settings");

    private void ShowWorkspace(string workspace)
    {
        _switchingViews = true;
        // The startup/Welcome workspace exposes Welcome followed by Library. Sidebar workspaces expose one tab only.
        BuilderTab.Visibility = workspace == "builder" ? Visibility.Visible : Visibility.Collapsed;
        ConfigurationTab.Visibility = workspace == "config" ? Visibility.Visible : Visibility.Collapsed;
        WelcomeTab.Visibility = workspace == "welcome" ? Visibility.Visible : Visibility.Collapsed;
        LibraryTab.Visibility = workspace is "welcome" or "library" ? Visibility.Visible : Visibility.Collapsed;
        SettingsTab.Visibility = workspace == "settings" ? Visibility.Visible : Visibility.Collapsed;
        DownloadsTab.Visibility = workspace == "downloads" ? Visibility.Visible : Visibility.Collapsed;
        MainViews.SelectedIndex = workspace switch { "builder" => 0, "config" => 1, "welcome" => 2, "settings" => 4, "downloads" => 5, _ => 3 };
        _switchingViews = false;
        UpdateShell();
    }

    private void LibraryNav_Click(object sender, RoutedEventArgs e)
    {
        _highlightedDemoId = null;
        _recentLibrary = false;
        ShowWorkspace("library");
        UpdateLibraryView();
        UpdateShell();
    }

    private void RecentNav_Click(object sender, RoutedEventArgs e)
    {
        _highlightedDemoId = null;
        _recentLibrary = true;
        ShowWorkspace("library");
        UpdateLibraryView();
        UpdateShell();
    }

    private void MainViews_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Ignore routed selection events from the editor tabs and page/section lists.
        if (!ReferenceEquals(e.Source, MainViews) || HeroVerbRun is null || _switchingViews || LibraryTab is null) return;
        if (MainViews.SelectedIndex == 3 && WelcomeTab.Visibility == Visibility.Visible) _recentLibrary = false;
        UpdateShell();
    }

    private void UpdateShell()
    {
        var welcome = MainViews.SelectedIndex == 2;
        var settings = MainViews.SelectedIndex == 4;
        var library = MainViews.SelectedIndex == 3;
        var config = MainViews.SelectedIndex == 1;
        var downloads = MainViews.SelectedIndex == 5;
        DownloadsNavButton.Tag = downloads ? "Active" : null;
        BuilderNavButton.Tag = MainViews.SelectedIndex == 0 ? "Active" : null;
        WelcomeNavButton.Tag = welcome ? "Active" : null;
        SettingsNavButton.Tag = settings ? "Active" : null;
        LibraryNavButton.Tag = library && !_recentLibrary ? "Active" : null;
        RecentNavButton.Tag = library && _recentLibrary ? "Active" : null;
        var browsing = library || welcome || settings || downloads;
        // The source folder picker belongs to Build Package; the Configuration Studio is reached from a library demo.
        SourceToolbar.Visibility = MainViews.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyWorkspaceDensity();
        LibraryActions.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
        // Keep toasts clear of the Configuration Studio footer.
        ToastHost.Margin = new Thickness(0, 0, 0, config ? 78 : 26);
        ApplySettingsChrome(settings, config);
        HeroVerbRun.Text = downloads ? "Download " : settings ? "Configure " : welcome ? "Welcome to " : library ? (_recentLibrary ? "Recently opened " : "Explore the ") : config ? "Configure " : "Build ";
        HeroSubjectRun.Text = downloads ? "Published Demos" : settings ? "Your Workspace" : welcome ? "FIS Presents" : library ? "FIS Demo Library" : config ? "Presentation" : "Presentation Packages";
        HeroDescriptionText.Text = downloads ? "Browse the demo repository, install new demos and update the ones you already have." : settings ? "Manage local storage, backups, the demo repository and Windows startup." : welcome ? "Stay up to date with news, newly published demos and presentation updates." : library ? "Explore your local demonstrations, filter by business or your assigned solutions, and launch." : config
            ? "Define your presentation settings, menu structure and page order."
            : "Bring existing pages, images and video together in one verified package.";
    }

    /// <summary>Settings and the Configuration Studio show only their own sub-tabs: the outer workspace tab strip and frame are hidden.</summary>
    private void ApplySettingsChrome(bool settings, bool config = false)
    {
        MainViews.ApplyTemplate();
        var ownTabs = settings || config;
        if (MainViews.Template?.FindName("TabHeaderScroll", MainViews) is FrameworkElement header)
            header.Visibility = ownTabs ? Visibility.Collapsed : Visibility.Visible;
        MainViews.BorderThickness = new Thickness(ownTabs ? 0 : 1);
        if (settings) { RenderUsage(); RenderLogs(); }
    }

    private async Task RefreshWelcomeAsync(bool checkRemote = true)
    {
        if (_refreshingWelcome) return;
        _refreshingWelcome = true;
        RefreshWelcomeButton.IsEnabled = false;
        WelcomeStatusText.Text = "Checking published content…";
        try
        {
            var result = string.IsNullOrWhiteSpace(_settings.RepositoryUrl)
                ? await _welcomeFeed.LoadSampleAsync()
                : await _welcomeFeed.RefreshAsync(_settings.RepositoryUrl, checkRemote: checkRemote);
            WelcomeItems.ItemsSource = result.Items;
            WelcomeEmptyState.Visibility = result.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            WelcomeStatusText.Text = $"{result.Source} · {result.Message}" + (result.CachedUtc.HasValue ? $" Last retrieved: {result.CachedUtc.Value.LocalDateTime:g}" : "");
            AppLog.Info("News", $"{result.Source} · {result.Items.Count} posts.");
        }
        catch (Exception exception) { WelcomeStatusText.Text = exception.Message; }
        finally { _refreshingWelcome = false; RefreshWelcomeButton.IsEnabled = true; }
    }

    private async void RefreshWelcome_Click(object sender, RoutedEventArgs e) => await RefreshWelcomeAsync();
    private async void SaveWelcomeFeed_Click(object sender, RoutedEventArgs e)
    {
        try { await _welcomeFeed.SaveUrlAsync(WelcomeFeedUrlText.Text.Trim()); await RefreshWelcomeAsync(); }
        catch (Exception exception) { WelcomeStatusText.Text = exception.Message; }
    }

    private void RenderSettings()
    {
        SettingsLibraryPath.Text = _settings.LibraryLocation;
        SettingsCachePath.Text = _settings.CacheLocation;
        SettingsBackupPath.Text = _settings.BackupLocation;
        SettingsAutoBackup.IsChecked = _settings.BackupBeforeImport;
        SettingsBackupCount.Text = _settings.BackupsToKeep.ToString();
        WelcomeFeedUrlText.Text = _settings.RepositoryUrl;
        SettingsCheckOnLaunch.IsChecked = _settings.CheckOnLaunch;
        SettingsPeriodicChecks.IsChecked = _settings.PeriodicChecks;
        SettingsCheckMinutes.Text = _settings.CheckIntervalMinutes.ToString();
        SettingsStartWindows.IsChecked = _settings.StartWithWindows;
        SettingsRepositoryPath.Text = _settings.DemoRepositoryPath;
        SettingsTrackDefault.IsChecked = _settings.TrackingEnabledByDefault;
        SettingsUploadUsage.IsChecked = _settings.UploadUsage;
        RenderDisplayScaleOptions(_settings.UiScale);
    }

    private void ConfigureRepositoryTimer()
    {
        _repositoryTimer.Stop();
        _repositoryTimer.Interval = TimeSpan.FromMinutes(_settings.CheckIntervalMinutes);
        if (_settings.PeriodicChecks && !string.IsNullOrWhiteSpace(_settings.RepositoryUrl)) _repositoryTimer.Start();
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(SettingsBackupCount.Text, out var retention) || !int.TryParse(SettingsCheckMinutes.Text, out var interval))
                throw new InvalidDataException("Backup count and check interval must be whole numbers.");
            var settings = new ApplicationSettings
            {
                LibraryLocation = SettingsLibraryPath.Text.Trim(), CacheLocation = SettingsCachePath.Text.Trim(), BackupLocation = SettingsBackupPath.Text.Trim(),
                BackupBeforeImport = SettingsAutoBackup.IsChecked == true, BackupsToKeep = retention, RepositoryUrl = WelcomeFeedUrlText.Text.Trim(),
                CheckOnLaunch = SettingsCheckOnLaunch.IsChecked == true, PeriodicChecks = SettingsPeriodicChecks.IsChecked == true, CheckIntervalMinutes = interval,
                StartWithWindows = SettingsStartWindows.IsChecked == true, DemoRepositoryPath = SettingsRepositoryPath.Text.Trim(),
                UiScale = _display.Preference,
                TrackingEnabledByDefault = SettingsTrackDefault.IsChecked == true, UploadUsage = SettingsUploadUsage.IsChecked == true
            };
            await _settingsService.SaveAsync(settings);
            _settings = settings;
            _libraryService = new DemoLibraryService(settings.LibraryLocation);
            _repositoryService = CreateRepositoryService(settings);
            _usage = new UsageTrackingService(settings.LibraryLocation);
            AppLog.Info("Settings", "Settings saved.");
            ConfigureRepositoryTimer();
            await RefreshLibraryAsync();
            await RefreshRepositoryAsync();
            try { ApplicationSettingsService.SetWindowsStartup(settings.StartWithWindows); SettingsStatusText.Text = "Settings saved. Storage and polling updated; Windows startup registration applied."; }
            catch (Exception exception) { SettingsStatusText.Text = $"Settings saved, but Windows startup registration failed: {exception.Message}"; }
        }
        catch (Exception exception) { SettingsStatusText.Text = $"Settings not saved: {exception.Message}"; }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var demos = _libraryDemos;
        var stats = _usage.GetStats();
        var facts = new Dialogs.AboutFacts(
            UsageTrackingService.AppVersion, Environment.Version.ToString(), SafeWebViewVersion(),
            DemoRepositoryService.CurrentUser, Environment.MachineName, _libraryService.Root, _settings.DemoRepositoryPath,
            demos.Count, demos.Count(demo => demo.Favorite), _assignedSolutions.Count,
            _availability.Count(item => item.State == DemoAvailabilityState.UpdateAvailable),
            _repositorySnapshot is { FromCache: false } ? "Connected" : _repositorySnapshot is null ? "Not checked" : "Offline (cached index)",
            stats);
        if (Dialogs.AboutDialog.Show(this, facts)) { ShowWorkspace("settings"); LogsSettingsTab.IsSelected = true; }
    }

    private static string SafeWebViewVersion()
    {
        try { return Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString() ?? "Not installed"; }
        catch (Exception) { return "Not installed"; }
    }

    private void RenderUsage()
    {
        try
        {
            var stats = _usage.GetStats();
            UsageSessionsList.ItemsSource = _usage.LoadSessions();
            UsageSummaryText.Text = $"{stats.TrackedSessions} tracked sessions · {stats.PendingFiles} files waiting to upload · {stats.UploadedFiles} uploaded"
                + (stats.LastUploadUtc is { } last ? $" · last upload {last.LocalDateTime:g}" : "");
        }
        catch (Exception exception) { UsageSummaryText.Text = $"Usage data unavailable: {exception.Message}"; }
    }

    private void OpenUsageFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(_usage.Root);
    private void OpenLogFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(AppLog.Folder);

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private async void UploadUsageNow_Click(object sender, RoutedEventArgs e)
    {
        UploadUsageButton.IsEnabled = false;
        try
        {
            if (!Directory.Exists(_repositoryService.Root)) throw new IOException("The demo repository is not reachable.");
            var uploaded = await _usage.UploadAsync(_repositoryService.Root);
            SettingsStatusText.Text = uploaded == 0 ? "Nothing waiting to upload." : $"{uploaded} usage files uploaded.";
            AppLog.Info("Usage", SettingsStatusText.Text);
        }
        catch (Exception exception) { SettingsStatusText.Text = $"Upload failed: {exception.Message}"; AppLog.Warn("Usage", SettingsStatusText.Text); }
        finally { UploadUsageButton.IsEnabled = true; RenderUsage(); }
    }

    private bool _renderingLogs;

    private void RefreshLogs_Click(object sender, RoutedEventArgs e) => RenderLogs();

    private void LogFilters_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _renderingLogs || LogEntriesList is null) return;
        RenderLogs();
    }

    private void RenderLogs()
    {
        _renderingLogs = true;
        try
        {
            var files = AppLog.LogFiles();
            var selected = LogFileSelector.SelectedItem as string;
            LogFileSelector.ItemsSource = files.Select(Path.GetFileName).ToList();
            LogFileSelector.SelectedItem = selected is not null && files.Any(file => Path.GetFileName(file) == selected) ? selected : files.Select(Path.GetFileName).FirstOrDefault();
            var file = files.FirstOrDefault(path => Path.GetFileName(path) == LogFileSelector.SelectedItem as string);
            IEnumerable<LogEntry> entries = file is null ? AppLog.Snapshot() : AppLog.Read(file);
            var level = (LogLevelFilter.SelectedItem as ComboBoxItem)?.Content as string ?? "All levels";
            if (level.StartsWith("Warn")) entries = entries.Where(entry => entry.Level is "WARN" or "ERROR");
            else if (level.StartsWith("Err")) entries = entries.Where(entry => entry.Level == "ERROR");
            var search = LogSearchText.Text.Trim();
            if (search.Length > 0) entries = entries.Where(entry => entry.Line.Contains(search, StringComparison.OrdinalIgnoreCase));
            LogEntriesList.ItemsSource = entries.Reverse().Take(1500).ToList();
        }
        catch (Exception exception) { SettingsStatusText.Text = $"Could not read logs: {exception.Message}"; }
        finally { _renderingLogs = false; }
    }

    private void RenderDisplayScaleOptions(double preference)
    {
        _renderingSettings = true;
        try
        {
            var values = Controls.DisplayScaler.Presets.Append(Controls.DisplayScaler.Normalize(preference)).Distinct().Order().ToList();
            SettingsDisplayScale.Items.Clear();
            foreach (var value in values)
                SettingsDisplayScale.Items.Add(new ComboBoxItem { Content = value == 0 ? "Auto (fit to screen)" : $"{value:P0}", Tag = value });
            SettingsDisplayScale.SelectedItem = SettingsDisplayScale.Items.Cast<ComboBoxItem>().First(item => (double)item.Tag == Controls.DisplayScaler.Normalize(preference));
        }
        finally { _renderingSettings = false; }
        UpdateDisplayScaleText();
    }

    private async void SettingsDisplayScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_renderingSettings || SettingsDisplayScale.SelectedItem is not ComboBoxItem { Tag: double value }) return;
        _display.SetPreference(value);
        await SaveDisplayPreferenceAsync();
    }

    private async void DisplayShortcut_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.OemPlus or Key.Add: _display.Nudge(1); break;
            case Key.OemMinus or Key.Subtract: _display.Nudge(-1); break;
            case Key.D0 or Key.NumPad0: _display.SetPreference(0); break;
            default: return;
        }
        e.Handled = true;
        RenderDisplayScaleOptions(_display.Preference);
        await SaveDisplayPreferenceAsync();
    }

    /// <summary>Persists only the display preference so other unsaved Settings edits are not applied implicitly.</summary>
    private async Task SaveDisplayPreferenceAsync()
    {
        if (_settings.UiScale == _display.Preference) return;
        _settings.UiScale = _display.Preference;
        try { await _settingsService.SaveAsync(_settings); }
        catch (Exception exception) { SettingsStatusText.Text = $"Display size applied but not saved: {exception.Message}"; }
    }

    private void UpdateDisplayScaleText() =>
        SettingsDisplayScaleText.Text = $"Showing at {_display.Scale:P0}{(_display.Compact ? " · compact layout" : "")}";

    private void ApplyDisplayDensity()
    {
        var compact = _display.Compact;
        SidebarColumn.Width = new GridLength(compact ? 172 : 200);
        SidebarFooterRow.Height = new GridLength(compact ? 52 : 64);
        SidebarLogo.Width = compact ? 72 : 84;
        SidebarLogo.Height = compact ? 38 : 44;
        SidebarHeaderPanel.Margin = compact ? new Thickness(10, 12, 10, 10) : new Thickness(14, 18, 14, 14);
        SidebarStatusCard.Margin = new Thickness(0, compact ? 10 : 16, 0, 0);
        foreach (var button in SidebarNavPanel.Children.OfType<Button>())
        {
            if (compact) { button.Padding = new Thickness(18, 10, 18, 10); button.Margin = new Thickness(0, 0, 0, 2); }
            else { button.ClearValue(PaddingProperty); button.Margin = new Thickness(0); }
        }
        SidebarNavSeparator.Margin = compact ? new Thickness(18, 6, 18, 6) : new Thickness(22, 12, 22, 12);
        AboutButton.Margin = compact ? new Thickness(0, 0, 10, 10) : new Thickness(0, 0, 14, 14);
        HeroDescriptionText.FontSize = compact ? 13 : 14;
        HeroDescriptionText.Margin = new Thickness(0, compact ? 4 : 9, 0, 0);
        HeroArtwork.Width = compact ? 600 : 560;
        HeroArtwork.Height = compact ? 225 : 210;
        // Compact header is shorter than the artwork; anchor to the top so only the bottom is cropped.
        HeroArtwork.VerticalAlignment = compact ? VerticalAlignment.Top : VerticalAlignment.Center;
        MainViews.Margin = compact ? new Thickness(14, 8, 14, 10) : new Thickness(22, 12, 22, 16);
        LibraryActions.Margin = new Thickness(0, compact ? 8 : 12, compact ? 16 : 24, 0);
        StartupOverlay.Padding = new Thickness(compact ? 20 : 36);
        ApplyWorkspaceDensity();
        UpdateDisplayScaleText();
    }

    private Style? _compactTextBoxStyle;
    private Style CompactTextBoxStyle => _compactTextBoxStyle ??= new Style(typeof(TextBox), (Style)FindResource(typeof(TextBox)))
    {
        Setters = { new Setter(PaddingProperty, new Thickness(8, 4, 8, 4)) }
    };

    /// <summary>
    /// On compact screens the authoring workspaces (Build Package, Configuration Studio) trade header
    /// chrome for editing room: slim hero, one-line source toolbar, no intro text and tighter padding.
    /// </summary>
    private void ApplyWorkspaceDensity()
    {
        var compact = _display?.Compact == true;
        var authoring = MainViews.SelectedIndex is 0 or 1;
        var slim = compact && authoring;
        HeroRow.Height = new GridLength(slim ? 60 : compact ? 100 : 174);
        // The header column is at least as tall as the hero so the menu starts below the banner.
        SidebarLogoRow.MinHeight = HeroRow.Height.Value;
        HeroTextPanel.Margin = compact ? new Thickness(28, 6, 28, 6) : new Thickness(36, 18, 36, 18);
        HeroTitleText.FontSize = slim ? 22 : compact ? 26 : 35;
        HeroDescriptionText.Visibility = slim ? Visibility.Collapsed : Visibility.Visible;
        SourceToolbar.Margin = compact ? new Thickness(0, 2, 8, 0) : new Thickness(42, 66, 42, 0);
        SourceToolbarIntro.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        MainViews.Padding = !authoring ? new Thickness(7, 7, 7, 0) : new Thickness(7, compact ? 7 : 88, 7, 7);
        if (MainViews.SelectedIndex is 1 or 4) MainViews.Padding = new Thickness(0);
        foreach (var root in new FrameworkElement[] { BuilderRoot, ConfigRoot })
        {
            if (compact) root.Resources[typeof(TextBox)] = CompactTextBoxStyle;
            else root.Resources.Remove(typeof(TextBox));
        }
        BuilderIntro.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BuilderRoot.Margin = new Thickness(compact ? 6 : 12);
        ConfigRoot.Margin = new Thickness(0, compact ? 4 : 8, 0, compact ? 8 : 12);
        ConfigEditorTabs.Padding = new Thickness(compact ? 8 : 12);
        foreach (var card in new[] { ConfigDetailsCard, ConfigBrandingCard, ConfigOrgCard }) card.Padding = new Thickness(compact ? 16 : 22);
        foreach (var card in new[] { MenuSectionsCard, MenuPagesCard }) card.Padding = new Thickness(compact ? 14 : 18);
        ConfigFooter.Margin = new Thickness(0, compact ? 8 : 12, 0, 0);
        ConfigFooter.Padding = compact ? new Thickness(12, 6, 12, 6) : new Thickness(15, 10, 15, 10);
    }

    private TextBox SettingsFolderField(object? tag) => tag?.ToString() switch { "cache" => SettingsCachePath, "backup" => SettingsBackupPath, "repository" => SettingsRepositoryPath, _ => SettingsLibraryPath };
    private void BrowseSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose storage folder" };
        if (dialog.ShowDialog(this) == true) SettingsFolderField((sender as Button)?.Tag).Text = dialog.FolderName;
    }
    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = SettingsFolderField((sender as Button)?.Tag).Text.Trim();
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Save settings first to create this folder, or choose an existing location.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = Path.GetFullPath(folder), UseShellExecute = true });
        }
        catch (Exception exception) { SettingsStatusText.Text = exception.Message; }
    }
    private async void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        try { SettingsStatusText.Text = "Creating backup…"; var path = await Task.Run(() => ApplicationSettingsService.CreateBackup(_settings)); SettingsStatusText.Text = $"Backup saved: {path}"; }
        catch (Exception exception) { SettingsStatusText.Text = $"Backup failed: {exception.Message}"; }
    }
    private async void CheckRepositoryNow_Click(object sender, RoutedEventArgs e)
    {
        await RefreshWelcomeAsync();
        SettingsStatusText.Text = WelcomeStatusText.Text + " (checks use saved repository settings)";
    }

    private void ReadWelcomePost_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WelcomeFeedItem post }) return;
        var reader = new Window
        {
            Owner = this, Title = post.Title, Width = 780, Height = 650,
            Background = new SolidColorBrush(Color.FromRgb(16, 25, 37)), Foreground = Brushes.White,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        Controls.WindowFitter.Attach(reader, new Size(780, 650), new Size(560, 420), maximizeWhenTooSmall: false);
        var panel = new StackPanel { Margin = new Thickness(34) };
        panel.Children.Add(new TextBlock { Text = $"{post.TypeLabel}  ·  {post.DateLabel}", Foreground = new SolidColorBrush(Color.FromRgb(105, 228, 60)), FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = post.Title, TextWrapping = TextWrapping.Wrap, FontSize = 30, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 16) });
        panel.Children.Add(new TextBlock { Text = post.Summary, TextWrapping = TextWrapping.Wrap, FontSize = 17, Foreground = new SolidColorBrush(Color.FromRgb(190, 209, 195)), Margin = new Thickness(0, 0, 0, 20) });
        panel.Children.Add(new TextBlock { Text = post.Body, TextWrapping = TextWrapping.Wrap, FontSize = 15, LineHeight = 25, Margin = new Thickness(0, 0, 0, 24) });
        if (post.HasDemoLink)
        {
            var link = new Button { Content = "View demo in Library →", HorizontalAlignment = HorizontalAlignment.Left };
            link.Click += async (_, _) => { reader.Close(); await OpenDemoFromNewsAsync(post.DemoId); };
            panel.Children.Add(link);
        }
        reader.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        reader.ShowDialog();
    }

    private async void WelcomeDemoLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WelcomeFeedItem post }) await OpenDemoFromNewsAsync(post.DemoId);
    }

    private async Task OpenDemoFromNewsAsync(string demoId)
    {
        _recentLibrary = false;
        LibrarySearchText.Text = "";
        LibraryFavoritesOnly.IsChecked = false;
        LibraryBusinessFilter.SelectedIndex = 0;
        LibraryMySolutions.IsChecked = false;
        _highlightedDemoId = demoId;
        ShowWorkspace("library");
        await RefreshLibraryAsync();
        var matches = _libraryDemos.Where(demo => demo.Config.Id.Equals(demoId, StringComparison.OrdinalIgnoreCase)).ToList();
        Notify(matches.Count == 0
            ? $"This post references demo '{demoId}', which is not installed locally. Download it from Downloads or import its .fisxpkg."
            : $"Highlighted '{matches[0].Name}' from the Welcome post. Select Launch Demo when ready.");
    }

    private void ClearLibraryFocus_Click(object sender, RoutedEventArgs e)
    {
        _highlightedDemoId = null;
        _recentLibrary = false;
        LibrarySearchText.Text = "";
        LibraryFavoritesOnly.IsChecked = false;
        LibraryBusinessFilter.SelectedIndex = 0;
        LibraryMySolutions.IsChecked = false;
        UpdateLibraryView();
        Notify("Showing all locally installed demos.");
    }

    private void ApplyAssignmentDefaults()
    {
        var assigned = _assignedSolutions.Count > 0;
        var tip = assigned ? "Assigned to you: " + string.Join(", ", _assignedSolutions) : "No solutions are assigned to " + DemoRepositoryService.CurrentUser;
        _refreshingLibrary = _refreshingDownloads = true;
        foreach (var box in new[] { LibraryMySolutions, DownloadsMySolutions })
        {
            box.IsEnabled = assigned;
            box.IsChecked = assigned;
            box.ToolTip = tip;
        }
        _refreshingLibrary = _refreshingDownloads = false;
    }

    private IReadOnlyCollection<string>? SolutionFilter(CheckBox box) => box.IsChecked == true && _assignedSolutions.Count > 0 ? _assignedSolutions.ToList() : null;

    private static List<string> BusinessOptions(IEnumerable<string> present) =>
        new[] { DemoLibraryService.AllBusinesses }.Concat(OrganizationConfig.BusinessLines)
            .Concat(present.Where(business => !OrganizationConfig.BusinessLines.Contains(business, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Order())
            .ToList();

    private static void SetOptions(ComboBox box, List<string> options)
    {
        var selection = box.SelectedItem as string;
        box.ItemsSource = options;
        box.SelectedItem = selection;
        if (box.SelectedIndex < 0) box.SelectedIndex = 0;
    }

    private async Task RefreshLibraryAsync()
    {
        try
        {
            _libraryDemos = await _libraryService.LoadAsync();
            _refreshingLibrary = true;
            SetOptions(LibraryBusinessFilter, BusinessOptions(_libraryDemos.Select(demo => demo.Business)));
            _refreshingLibrary = false;
            UpdateLibraryView();
            UpdateSidebarStatus();
            AppLog.Info("Library", $"{_libraryDemos.Count} demos loaded from {_libraryService.Root}.");
        }
        catch (Exception exception)
        {
            _refreshingLibrary = false;
            Notify($"Could not read the library: {exception.Message}", error: true);
        }
    }

    private void LibraryFilters_Changed(object sender, RoutedEventArgs e)
    {
        if (!_refreshingLibrary && LibraryCards is not null && LibrarySort is not null)
        {
            _highlightedDemoId = null;
            UpdateLibraryView();
        }
    }

    private void UpdateLibraryView()
    {
        if (LibrarySearchText is null) return;
        var sort = (LibrarySort.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Name";
        var candidates = _highlightedDemoId is null ? _libraryDemos
            : _libraryDemos.Where(demo => demo.Config.Id.Equals(_highlightedDemoId, StringComparison.OrdinalIgnoreCase)).ToList();
        var filtered = DemoLibraryService.Filter(candidates, LibrarySearchText.Text, null, LibraryFavoritesOnly.IsChecked == true, _recentLibrary, sort,
            LibraryBusinessFilter.SelectedItem as string, _highlightedDemoId is null ? SolutionFilter(LibraryMySolutions) : null);
        LibraryCards.ItemsSource = filtered.Select(demo => new DemoCard(demo, _highlightedDemoId)).ToList();
        LibraryCountText.Text = $"{(_recentLibrary ? "Recently Opened" : LibraryMySolutions.IsChecked == true && _assignedSolutions.Count > 0 ? "My Solutions" : "Available Demonstrations")} ({filtered.Count})";
        LibraryEmptyState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryEmptyText.Text = _libraryDemos.Count == 0
            ? "Download demos from the repository to start your local library, or import a verified .fisxpkg."
            : "No demos match this view. Clear the filters, untick My solutions, or browse Downloads for more.";
    }

    private async void RefreshLibrary_Click(object sender, RoutedEventArgs e) => await RefreshLibraryAsync();

    private async void DownloadsNav_Click(object sender, RoutedEventArgs e) => await OpenDownloadsAsync();

    private async void RefreshDownloads_Click(object sender, RoutedEventArgs e) => await RefreshRepositoryAsync();

    private async Task RefreshRepositoryAsync()
    {
        if (_downloading) return;
        DownloadsStatusText.Text = $"Reading demo repository {_repositoryService.Root}…";
        try
        {
            _repositorySnapshot = await _repositoryService.LoadIndexAsync();
            var status = _repositorySnapshot.Message;
            try
            {
                var (solutions, fromCache) = await _repositoryService.LoadAssignmentsAsync();
                if (!solutions.SequenceEqual(_assignedSolutions, StringComparer.OrdinalIgnoreCase))
                {
                    _assignedSolutions = solutions;
                    ApplyAssignmentDefaults();
                    UpdateLibraryView();
                }
                if (fromCache) status += " · cached assignments";
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException) { status += $" · assignments unavailable ({exception.Message})"; }
            await RefreshAvailabilityAsync();
            DownloadsStatusText.Text = $"{_repositoryService.Root} · {status}";
        }
        catch (Exception exception)
        {
            _repositorySnapshot = null;
            _availability = [];
            UpdateDownloadsView();
            DownloadsStatusText.Text = $"Demo repository unavailable: {exception.Message} Check the folder in Settings › Remote Repository.";
        }
    }

    private async Task RefreshAvailabilityAsync()
    {
        if (_repositorySnapshot is null) return;
        _availability = DemoRepositoryService.Compare(_repositorySnapshot.Index, await _libraryService.LoadAsync());
        UpdateDownloadsView();
    }

    private void DownloadsFilters_Changed(object sender, RoutedEventArgs e)
    {
        if (!_refreshingDownloads) UpdateDownloadsView();
    }

    private bool IsAssigned(DemoAvailability item) => _assignedSolutions.Contains(item.Demo.Organization.L8, StringComparer.OrdinalIgnoreCase);

    private void UpdateDownloadsView()
    {
        if (DownloadsSearchText is null || DownloadCards is null) return;
        _refreshingDownloads = true;
        SetOptions(DownloadsBusinessFilter, BusinessOptions(_availability.Select(item => DownloadCard.BusinessOf(item.Demo))));
        _refreshingDownloads = false;
        var business = DownloadsBusinessFilter.SelectedItem as string;
        var solutions = SolutionFilter(DownloadsMySolutions);
        var words = DownloadsSearchText.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var visible = _availability
            .Where(item => business is null || business == DemoLibraryService.AllBusinesses || DownloadCard.BusinessOf(item.Demo).Equals(business, StringComparison.OrdinalIgnoreCase))
            .Where(item => solutions is null || solutions.Contains(item.Demo.Organization.L8, StringComparer.OrdinalIgnoreCase))
            .Where(item => DownloadsUpdatesOnly.IsChecked != true || item.State is DemoAvailabilityState.NotInstalled or DemoAvailabilityState.UpdateAvailable)
            .Where(item => words.All(word => string.Join(" ", item.Demo.Name, item.Demo.Description, item.Demo.Organization.Path).Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(IsAssigned)
            .ThenBy(item => item.State switch { DemoAvailabilityState.UpdateAvailable => 0, DemoAvailabilityState.NotInstalled => 1, _ => 2 })
            .ThenBy(item => item.Demo.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new DownloadCard(item, IsAssigned(item), _downloading))
            .ToList();
        DownloadCards.ItemsSource = visible;
        DownloadsCountText.Text = $"{(solutions is null ? "Published Demonstrations" : "Published for My Solutions")} ({visible.Count})";
        var updates = _availability.Count(item => item.State == DemoAvailabilityState.UpdateAvailable);
        DownloadAllUpdatesButton.Content = updates == 0 ? "No updates" : $"Update all ({updates})";
        DownloadAllUpdatesButton.IsEnabled = updates > 0 && !_downloading;
        DownloadsEmptyState.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadsEmptyText.Text = _repositorySnapshot is null
            ? "The demo repository could not be read. Check the repository folder in Settings › Remote Repository, then Refresh."
            : _availability.Count == 0 ? "No demos have been published to this repository yet."
            : "No published demos match these filters. Untick My solutions or choose All businesses.";
        UpdateSidebarStatus();
    }

    private static readonly Brush StatusOnline = Frozen(0x69, 0xE4, 0x3C);
    private static readonly Brush StatusWarn = Frozen(0xF2, 0xC1, 0x5B);
    private static readonly Brush StatusOffline = Frozen(0xFF, 0x8B, 0x7D);

    /// <summary>Sidebar session card: signed-in user, repository reachability, pending updates and library size.</summary>
    private void UpdateSidebarStatus()
    {
        if (SidebarStatusCard is null) return;
        var user = Environment.UserName;
        SidebarUserText.Text = user;
        SidebarUserInitial.Text = string.IsNullOrEmpty(user) ? "?" : char.ToUpperInvariant(user[0]).ToString();
        SidebarMachineText.Text = Environment.MachineName;
        var (repoText, repoBrush) = _repositorySnapshot switch
        {
            null => ("Offline", StatusOffline),
            { FromCache: true } => ("Offline · cached", StatusWarn),
            _ => ("Online", StatusOnline)
        };
        SidebarRepoText.Text = repoText;
        SidebarRepoDot.Fill = repoBrush;
        var updates = _availability.Count(item => item.State == DemoAvailabilityState.UpdateAvailable);
        var newDemos = _availability.Count(item => item.State == DemoAvailabilityState.NotInstalled);
        SidebarUpdatesText.Text = _repositorySnapshot is null ? "—" : updates == 0 ? "None pending" : $"{updates} pending";
        SidebarUpdatesText.Foreground = updates > 0 ? StatusWarn : Brushes.White;
        SidebarLibraryText.Text = $"{_libraryDemos.Count(demo => demo.Available)} installed" + (newDemos > 0 ? $" · {newDemos} new" : "");
        SidebarStatusCard.ToolTip = $"{DemoRepositoryService.CurrentUser} on {Environment.MachineName}\nRepository: {_settings.DemoRepositoryPath} ({repoText})"
            + $"\n{updates} updates · {newDemos} new demos available\nClick to open Downloads";
    }

    private async void SidebarStatusCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => await OpenDownloadsAsync();

    private async Task OpenDownloadsAsync()
    {
        ShowWorkspace("downloads");
        await RefreshRepositoryAsync();
    }

    private async void DownloadDemo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadCard card } || _downloading) return;
        await RunDownloadsAsync([card.Item]);
    }

    private async void DownloadAllUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_downloading) return;
        await RunDownloadsAsync(_availability.Where(item => item.State == DemoAvailabilityState.UpdateAvailable).ToList());
    }

    private async Task RunDownloadsAsync(IReadOnlyList<DemoAvailability> items)
    {
        if (items.Count == 0) return;
        _downloading = true;
        UpdateDownloadsView();
        try
        {
            var count = await DownloadDemosAsync(items, text => DownloadsStatusText.Text = text);
            DownloadsStatusText.Text = count == 1
                ? $"{items[0].Demo.Name} v{items[0].Latest.Version} verified and installed · open it from the Demo Library."
                : $"{count} demos verified and installed · open them from the Demo Library.";
        }
        catch (Exception exception) { DownloadsStatusText.Text = $"Download failed: {exception.Message}"; }
        finally { UpdateDownloadsView(); }
    }

    private sealed class DownloadCard(DemoAvailability item, bool assigned, bool busy)
    {
        public DemoAvailability Item => item;
        public string Name => item.Demo.Name;
        public string Description => item.Demo.Description;
        public string Business => BusinessOf(item.Demo).ToUpperInvariant();
        public string Classification => item.Demo.Organization.IsEmpty ? "No organizational tags" : item.Demo.Organization.Path;
        public BitmapSource Banner => TileThemes.Banner(item.Demo.TileTheme, item.Demo.AccentColor, item.Demo.Id);
        public Brush LineBrush => Frozen(TileThemes.LineColor(item.Demo.TileTheme, item.Demo.AccentColor));
        public string LatestLabel => $"v{item.Latest.Version}  ·  Updated {item.Latest.PublishedUtc.LocalDateTime:d}  ·  {FormatBytes(item.Latest.Bytes)}";
        public string InstalledLabel => (item.Installed is null ? "Not installed" : $"Installed v{item.Installed.Config.Version}") + (assigned ? "  ·  ★ Assigned to you" : "");
        public string ReleaseNotes => item.Latest.ReleaseNotes;
        public string StateLabel => item.State switch
        {
            DemoAvailabilityState.NotInstalled => "AVAILABLE", DemoAvailabilityState.UpdateAvailable => "UPDATE",
            DemoAvailabilityState.UpToDate => "INSTALLED", _ => "LOCAL IS NEWER"
        };
        public Brush StateBrush => Frozen(item.State switch
        {
            DemoAvailabilityState.UpdateAvailable => Color.FromRgb(0xF2, 0xC1, 0x5B), DemoAvailabilityState.NotInstalled => Color.FromRgb(0x69, 0xE4, 0x3C),
            _ => Color.FromRgb(0xBE, 0xC4, 0xC2)
        });
        public string ActionLabel => busy ? "Working…" : item.State switch
        {
            DemoAvailabilityState.NotInstalled => "↓ Download", DemoAvailabilityState.UpdateAvailable => $"↻ Update to v{item.Latest.Version}",
            DemoAvailabilityState.UpToDate => "✓ Installed", _ => "Newer version installed"
        };
        public bool CanDownload => !busy && item.State is DemoAvailabilityState.NotInstalled or DemoAvailabilityState.UpdateAvailable;

        public static string BusinessOf(RepositoryDemo demo) => string.IsNullOrWhiteSpace(demo.Organization.L3) ? "Unassigned" : demo.Organization.L3;
        private static string FormatBytes(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576d:F1} MB" : $"{Math.Max(1, bytes / 1024)} KB";
        private static Brush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
    }

    private async void ImportDemo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import demo package into the local library", Filter = "FIS demo package (*.fisxpkg)|*.fisxpkg" };
        if (dialog.ShowDialog(this) != true) return;
        ImportDemoButton.IsEnabled = false;
        Notify("Verifying and importing package…");
        try
        {
            var demo = await Task.Run(() => _libraryService.ImportAsync(dialog.FileName));
            await RefreshLibraryAsync();
            Notify($"'{demo.Name}' is in your local library. Identical packages are not imported twice.");
        }
        catch (Exception exception) { Notify($"Import failed: {exception.Message}", error: true); }
        finally { ImportDemoButton.IsEnabled = true; }
    }

    private async void FavoriteDemo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LibraryDemo demo }) return;
        try { await _libraryService.UpdateAsync(demo.Hash, favorite: !demo.Favorite); await RefreshLibraryAsync(); }
        catch (Exception exception) { Notify(exception.Message, error: true); }
    }

    private async void LaunchDemo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LibraryDemo demo }) return;
        if (!File.Exists(demo.PackagePath)) { Notify("The demo package is missing. Reimport it to restore the local copy.", error: true); return; }
        var context = Dialogs.DemoStartDialog.Show(this, demo, _settings.TrackingEnabledByDefault, _lastLaunch);
        if (context is null) return;
        _lastLaunch = context;
        try
        {
            OpenPlayer(demo.PackagePath, context);
            await _libraryService.UpdateAsync(demo.Hash, launched: true);
            await RefreshLibraryAsync();
        }
        catch (Exception exception) { Notify(exception.Message, error: true); }
    }

    private void DemoDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LibraryDemo demo }) return;
        if (Dialogs.DemoDetailsDialog.Show(this, demo)) LaunchDemo_Click(new Button { Tag = demo }, new RoutedEventArgs());
    }

    private void DemoMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LibraryDemo demo } button) return;
        var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenuStyle"), PlacementTarget = button, Placement = PlacementMode.Bottom };
        MenuItem Item(string header, Action action, Brush? foreground = null)
        {
            var item = new MenuItem { Header = header, Style = (Style)FindResource("DarkMenuItemStyle") };
            if (foreground is not null) item.Foreground = foreground;
            item.Click += (_, _) => action();
            return item;
        }
        menu.Items.Add(Item("Edit configuration", async () => await EditLibraryDemoAsync(demo)));
        menu.Items.Add(Item("View details", () => DemoDetails_Click(new Button { Tag = demo }, new RoutedEventArgs())));
        menu.Items.Add(new Separator { Style = (Style)FindResource("DarkMenuSeparatorStyle") });
        menu.Items.Add(Item("Uninstall demo…", async () => await UninstallLibraryDemoAsync(demo), new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x7D))));
        menu.IsOpen = true;
    }

    private async Task EditLibraryDemoAsync(LibraryDemo demo)
    {
        try
        {
            var workspace = _libraryService.WorkspacePath(demo.Config.Id);
            var replace = false;
            var existingConfig = Path.Combine(workspace, PresentationService.ConfigurationFileName);
            if (File.Exists(existingConfig))
            {
                string? workingVersion;
                try { workingVersion = _presentationService.Deserialize(await File.ReadAllTextAsync(existingConfig)).Version; }
                catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException) { workingVersion = null; }
                if (workingVersion != demo.Config.Version)
                    replace = Confirm("Replace your working copy?",
                        $"You already have a working copy of '{demo.Name}'{(workingVersion is null ? "" : $" from v{workingVersion}")}, but v{demo.Config.Version} is installed. Replace the working copy with the installed version? Unbuilt changes in the working copy will be lost.",
                        $"Use installed v{demo.Config.Version}", "Keep my working copy");
            }
            else if (Directory.Exists(workspace)) replace = true;
            Notify($"Preparing an editable copy of '{demo.Name}'…");
            workspace = await Task.Run(() => _libraryService.CreateEditableCopyAsync(demo, replace));
            await LoadSourceFolderAsync(workspace);
            if (_sourceDirectory is null || !Path.GetFullPath(workspace).Equals(_sourceDirectory, StringComparison.OrdinalIgnoreCase)) return;
            ShowWorkspace("config");
            EditorStatusText.Text = $"Editing '{demo.Name}' v{demo.Config.Version}. Save Configuration saves your changes and updates this demo in your Demo Library; Save as new demo creates a separate .fisxpkg demo.";
            Notify($"'{demo.Name}' is open in the Configuration Editor.");
            SetStatus("EDITING LIBRARY DEMO", "#4BCD3E");
        }
        catch (Exception exception) { Notify($"Could not open '{demo.Name}' for editing: {exception.Message}", error: true); }
    }

    private async Task UninstallLibraryDemoAsync(LibraryDemo demo)
    {
        try
        {
            var workspace = _libraryService.WorkspacePath(demo.Config.Id);
            var hasWorkingCopy = Directory.Exists(workspace);
            var body = $"This removes '{demo.Name}' (v{demo.Config.Version}) from this PC: the installed package, its cover image, and its favorite and launch history"
                + (hasWorkingCopy ? ", plus your editable working copy and any unbuilt changes in it" : "")
                + ". If the demo is published in the demo repository, you can download it again from Downloads.";
            if (!ConfirmTyped($"Uninstall '{demo.Name}'?", body, "delete", "Uninstall demo")) return;
            if (_sourceDirectory is not null && Path.GetFullPath(workspace).Equals(_sourceDirectory, StringComparison.OrdinalIgnoreCase)) ClearSourceFolder();
            var removed = await Task.Run(() => _libraryService.UninstallAsync(demo.Config.Id));
            await RefreshLibraryAsync();
            try { await RefreshAvailabilityAsync(); }
            catch (Exception) { /* Downloads refreshes itself on next visit. */ }
            Notify(removed == 0 ? $"'{demo.Name}' was already removed." : $"'{demo.Name}' was uninstalled from this PC.");
        }
        catch (Exception exception) { Notify($"Uninstall failed: {exception.Message}", error: true); }
    }

    private void ClearSourceFolder()
    {
        _sourceDirectory = null;
        SourcePathText.Text = "";
        AssetListBox.ItemsSource = null;
        AssetSummaryText.Text = "Choose a source folder to scan files.";
        PackageSourceSummary.Text = "No source folder selected";
        EditorStatusText.Text = "The demo you were editing was uninstalled. Choose a source folder to continue.";
    }

    /// <summary>Destructive confirmation: the action only enables once <paramref name="word"/> is typed.</summary>
    private bool ConfirmTyped(string title, string body, string word, string action)
    {
        var accepted = false;
        var dialog = new Window
        {
            Owner = this, Title = title, Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(16, 25, 37)), Foreground = Brushes.White, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var danger = new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x7D));
        var panel = new StackPanel { Margin = new Thickness(30) };
        panel.Children.Add(new TextBlock { Text = "FIS PRESENTS · DEMO LIBRARY", Foreground = danger, FontSize = 11, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 14) });
        panel.Children.Add(new TextBlock { Text = body, FontSize = 14, LineHeight = 22, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(190, 196, 194)), Margin = new Thickness(0, 0, 0, 18) });
        var prompt = new TextBlock { FontSize = 13, Margin = new Thickness(0, 0, 0, 8) };
        prompt.Inlines.Add(new System.Windows.Documents.Run("Type "));
        prompt.Inlines.Add(new System.Windows.Documents.Run(word) { FontWeight = FontWeights.Bold, Foreground = danger });
        prompt.Inlines.Add(new System.Windows.Documents.Run(" to confirm."));
        panel.Children.Add(prompt);
        var input = new TextBox { Margin = new Thickness(0, 0, 0, 22), VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(input, $"Type {word} to confirm");
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var accept = new Button
        {
            Content = action, IsDefault = true, IsEnabled = false, Style = (Style)FindResource("PrimaryButtonStyle"), Margin = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x4F, 0x45)), Foreground = Brushes.White
        };
        input.TextChanged += (_, _) => accept.IsEnabled = input.Text.Trim().Equals(word, StringComparison.OrdinalIgnoreCase);
        accept.Click += (_, _) => { accepted = true; dialog.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => input.Focus();
        dialog.ShowDialog();
        return accepted;
    }

    private sealed class DemoCard(LibraryDemo record, string? highlightedId = null)
    {
        private BrandingConfig Branding => record.Config.Branding;
        public bool Highlighted => record.Config.Id.Equals(highlightedId, StringComparison.OrdinalIgnoreCase);
        public LibraryDemo Record => record;
        public string Name => record.Name;
        public string Description => record.Description;
        public string Business => record.Business.ToUpperInvariant();
        public string Classification => record.OrganizationPath;
        public string FooterLabel => record.Available ? $"{record.VersionLabel}  ·  Updated {record.ImportedUtc.LocalDateTime:d}" : $"{record.VersionLabel}  ·  Package missing";
        public bool IsFavorite => record.Favorite;
        public string FavoriteTip => record.Favorite ? "Remove from favorites" : "Add to favorites";
        public bool Available => record.Available;
        public BitmapSource Banner => TileThemes.Banner(Branding.TileTheme, Branding.AccentColor, record.Config.Id);
        public Brush LineBrush
        {
            get { var brush = new SolidColorBrush(TileThemes.LineColor(Branding.TileTheme, Branding.AccentColor)); brush.Freeze(); return brush; }
        }
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select a presentation source folder" };
        if (dialog.ShowDialog(this) == true) await LoadSourceFolderAsync(dialog.FolderName);
    }

    private async void LoadSample_Click(object sender, RoutedEventArgs e)
    {
        var samplePath = Path.Combine(AppContext.BaseDirectory, "samples", "BasicPresentation");
        if (!Directory.Exists(samplePath))
            samplePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "samples", "BasicPresentation"));
        if (!Directory.Exists(samplePath))
        {
            ShowError("The bundled sample was not found. Build the project again or choose a source folder.");
            return;
        }
        await LoadSourceFolderAsync(samplePath);
    }

    private async Task LoadSourceFolderAsync(string folder)
    {
        try
        {
            var result = await _presentationService.LoadOrCreateConfigAsync(folder);
            _sourceDirectory = Path.GetFullPath(folder);
            SourcePathText.Text = _sourceDirectory;
            var assets = _assetInventoryService.Scan(_sourceDirectory);
            AssetListBox.ItemsSource = FormatInventory(assets);
            AssetSummaryText.Text = BuildInventorySummary(assets);
            PackageSourceSummary.Text = $"{assets.Count} files · {Path.GetFileName(_sourceDirectory)}";
            RenderConfig(result.Config);
            ValidationText.Text = result.Created
                ? "Starter presentation.json created from existing HTML pages. Review the menu in Configuration Editor."
                : "Source loaded. Validate the package or edit the runtime configuration.";
            EditorStatusText.Text = result.Created
                ? "A starter menu was generated from existing HTML files. No HTML was created or changed."
                : "Configuration loaded. Sections and page order match presentation.json.";
            SetStatus("SOURCE FOLDER LOADED", "#4BCD3E");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void RescanAssets_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceDirectory is null)
        {
            ShowError("Choose a source folder first.");
            return;
        }

        try
        {
            var assets = _assetInventoryService.Scan(_sourceDirectory);
            AssetListBox.ItemsSource = FormatInventory(assets);
            AssetSummaryText.Text = BuildInventorySummary(assets);
            PackageSourceSummary.Text = $"{assets.Count} files · {Path.GetFileName(_sourceDirectory)}";
            SetStatus("SOURCE INVENTORY UPDATED", "#3BCFF0");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = GetEditorConfig(applyMetadata: true);
            var errors = _presentationService.ValidateConfiguration(config, RequireSourceDirectory());
            ShowValidationResult(config, errors);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private async void BuildPackage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sourceDirectory = RequireSourceDirectory();
            var config = GetEditorConfig(applyMetadata: true);
            var errors = _presentationService.ValidateConfiguration(config, sourceDirectory);
            if (errors.Count > 0)
            {
                ShowValidationResult(config, errors);
                SetStatus("BUILD BLOCKED", "#FF8B7D");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save FIS presentation package",
                Filter = "FIS presentation package (*.fisxpkg)|*.fisxpkg",
                FileName = $"{config.Id}-{config.Version}.fisxpkg",
                AddExtension = true,
                DefaultExt = ".fisxpkg"
            };
            if (dialog.ShowDialog(this) != true) return;

            SetStatus("BUILDING PACKAGE", "#4BCD3E");
            ValidationText.Text = "Preparing package…";
            BuildProgressBar.Visibility = Visibility.Visible;
            BuildProgressBar.Value = 0;
            BuildPackageButton.IsEnabled = false;
            CancelBuildButton.Visibility = Visibility.Visible;
            _buildCancellation = new CancellationTokenSource();
            var progress = new Progress<PackageBuildProgress>(status =>
            {
                BuildProgressBar.Value = status.Percent;
                ValidationText.Text = status.Operation;
            });

            await File.WriteAllTextAsync(Path.Combine(sourceDirectory, PresentationService.ConfigurationFileName), _presentationService.Serialize(config));
            var manifest = await _presentationService.BuildPackageAsync(sourceDirectory, dialog.FileName, config, progress, _buildCancellation.Token);
            ValidationText.Text = $"Package verified · {manifest.Files.Count} files · saved to {dialog.FileName}";
            SetStatus("PACKAGE READY", "#4BCD3E");
            if (AddBuildToLibrary.IsChecked == true)
            {
                try
                {
                    var imported = await Task.Run(() => _libraryService.ImportAsync(dialog.FileName));
                    // The library keeps one version per demo, matching repository updates.
                    await _libraryService.RetireOtherVersionsAsync(imported.Config.Id, imported.Hash);
                    await RefreshLibraryAsync();
                    ValidationText.Text = $"Package ready and added to Demo Library · {manifest.Files.Count} files.";
                }
                catch (Exception exception)
                {
                    ValidationText.Text = $"Package saved successfully, but library import failed: {exception.Message}";
                }
            }
            if (MessageBox.Show(this, "The package was built and verified. Open it now in the shared runtime?", "Package ready", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                OpenPlayer(dialog.FileName);
        }
        catch (OperationCanceledException)
        {
            ValidationText.Text = "Package build cancelled. The previous output package was left untouched.";
            SetStatus("BUILD CANCELLED", "#3BCFF0");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            BuildProgressBar.Visibility = Visibility.Collapsed;
            BuildPackageButton.IsEnabled = true;
            CancelBuildButton.Visibility = Visibility.Collapsed;
            _buildCancellation?.Dispose();
            _buildCancellation = null;
        }
    }

    private void CancelBuild_Click(object sender, RoutedEventArgs e) => _buildCancellation?.Cancel();

    private void OpenPackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open FIS presentation package",
            Filter = "FIS presentation package (*.fisxpkg)|*.fisxpkg|ZIP archive (*.zip)|*.zip"
        };
        if (dialog.ShowDialog(this) == true) OpenPlayer(dialog.FileName);
    }

    private void ApplyMetadata_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = GetEditorConfig(applyMetadata: true);
            RenderConfig(config);
            EditorStatusText.Text = "Presentation identity and branding applied to the JSON configuration.";
            SetStatus("CONFIGURATION UPDATED", "#4BCD3E");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void ApplyJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = _presentationService.Deserialize(ConfigTextBox.Text);
            RenderConfig(config);
            EditorStatusText.Text = "JSON applied. The visual editor now reflects this configuration.";
            SetStatus("JSON APPLIED", "#3BCFF0");
        }
        catch (Exception exception)
        {
            ShowError($"Could not apply JSON: {exception.Message}");
        }
    }

    private async void ReloadConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sourceDirectory = RequireSourceDirectory();
            var result = await _presentationService.LoadOrCreateConfigAsync(sourceDirectory);
            RenderConfig(result.Config);
            EditorStatusText.Text = "Reloaded presentation.json from the selected source folder.";
            SetStatus("CONFIGURATION RELOADED", "#3BCFF0");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private bool _savingConfig;

    private async void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        if (_savingConfig) return;
        try
        {
            var sourceDirectory = RequireSourceDirectory();
            if (DataSetDirty && MessageBox.Show(this, "Save the open data set first?", "Unsaved data set", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                SaveSelectedDataSet();
            var config = GetEditorConfig(applyMetadata: true);
            var errors = _presentationService.ValidateConfiguration(config, sourceDirectory);
            if (errors.Count > 0)
            {
                EditorStatusText.Text = string.Join("  •  ", errors);
                SetStatus("CONFIG NEEDS ATTENTION", "#FF8B7D");
                return;
            }

            var json = _presentationService.Serialize(config);
            File.WriteAllText(Path.Combine(sourceDirectory, PresentationService.ConfigurationFileName), json);
            RenderConfig(config);
            EditorStatusText.Text = "presentation.json saved in the selected source folder.";
            SetStatus("CONFIGURATION SAVED", "#4BCD3E");
            if (!IsLibraryWorkingCopy(config, sourceDirectory)) return;
            _savingConfig = true;
            if (await UpdateLibraryDemoAsync(config, sourceDirectory))
            {
                EditorStatusText.Text = $"Saved and updated in your Demo Library: '{config.Name}' v{config.Version}. A newer repository version will still be offered as an update.";
                Notify($"'{config.Name}' was saved and updated in your Demo Library.");
            }
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally { _savingConfig = false; }
    }

    private async void SaveAsNewDemo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sourceDirectory = RequireSourceDirectory();
            var config = GetEditorConfig(applyMetadata: true);
            var errors = _presentationService.ValidateConfiguration(config, sourceDirectory);
            if (errors.Count > 0)
            {
                EditorStatusText.Text = "Fix these before saving as a new demo:  " + string.Join("  •  ", errors);
                return;
            }
            var identity = Dialogs.SaveAsNewDemoDialog.Show(this, config.Name, config.Id, id =>
                _libraryDemos.Any(demo => demo.Config.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                || (DemoLibraryService.IsSafeId(id) && Directory.Exists(_libraryService.WorkspacePath(id))));
            if (identity is null) return;

            var exports = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FIS Presents");
            Directory.CreateDirectory(exports);
            var dialog = new SaveFileDialog
            {
                Title = "Save the new demo package",
                Filter = "FIS presentation package (*.fisxpkg)|*.fisxpkg",
                InitialDirectory = exports,
                FileName = $"{identity.Id}-{identity.Version}.fisxpkg",
                AddExtension = true,
                DefaultExt = ".fisxpkg"
            };
            if (dialog.ShowDialog(this) != true) return;

            var copy = _presentationService.Deserialize(_presentationService.Serialize(config));
            copy.Id = identity.Id;
            copy.Name = identity.Name;
            copy.Version = identity.Version;
            SaveAsNewDemoButton.IsEnabled = false;
            var progress = new Progress<PackageBuildProgress>(status => EditorStatusText.Text = $"Creating '{identity.Name}' · {status.Operation}");
            var (created, workspace) = await Task.Run(() => _libraryService.SaveAsNewDemoAsync(sourceDirectory, copy, dialog.FileName, progress));
            AppLog.Info("Library", $"Saved '{config.Name}' v{config.Version} as new demo '{created.Name}' ({created.Config.Id}) v{created.Config.Version} → {dialog.FileName}.");
            await RefreshLibraryAsync();
            await LoadSourceFolderAsync(workspace);
            EditorStatusText.Text = $"Now editing the new demo '{created.Name}' v{created.Config.Version}. Package saved to {dialog.FileName} and added to your Demo Library.";
            Notify($"New demo '{created.Name}' was created and added to your Demo Library.");
        }
        catch (Exception exception)
        {
            AppLog.Error("Library", "Save as new demo failed.", exception);
            Notify($"Could not save as a new demo: {exception.Message}", error: true);
        }
        finally { SaveAsNewDemoButton.IsEnabled = true; }
    }

    private void ValidateEditorConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = GetEditorConfig(applyMetadata: true);
            var errors = _presentationService.ValidateConfiguration(config, RequireSourceDirectory());
            if (errors.Count == 0)
            {
                EditorStatusText.Text = $"Valid · {config.Navigation.Sections.Count} sections · {config.Navigation.Sections.Sum(section => section.Pages.Count)} menu pages.";
                SetStatus("CONFIGURATION VALID", "#4BCD3E");
            }
            else
            {
                EditorStatusText.Text = string.Join("  •  ", errors);
                SetStatus("CONFIG NEEDS ATTENTION", "#FF8B7D");
            }
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void AddSection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = GetEditorConfig(applyMetadata: true);
            var id = CreateUniqueId("section", config.Navigation.Sections.Select(section => section.Id));
            var section = new NavigationSection { Id = id, Title = "New section" };
            config.Navigation.Sections.Add(section);
            RenderConfig(config, id);
            SectionTitleText.Focus();
            SectionTitleText.SelectAll();
            EditorStatusText.Text = "Section added. Set its title, then add existing HTML pages to it.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void ApplySection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = GetEditorConfig(applyMetadata: true);
            var section = GetSelectedSection(config) ?? throw new InvalidOperationException("Select a menu section first.");
            section.Title = SectionTitleText.Text.Trim();
            if (string.IsNullOrWhiteSpace(section.Title)) throw new InvalidOperationException("Section title is required.");
            RenderConfig(config, section.Id, (PagesListBox.SelectedItem as NavigationPage)?.Id);
            EditorStatusText.Text = $"Section '{section.Title}' updated.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void RemoveSection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = GetEditorConfig(applyMetadata: true);
            var section = GetSelectedSection(config) ?? throw new InvalidOperationException("Select a menu section first.");
            if (MessageBox.Show(this, $"Remove '{section.Title}' and its menu entries? The referenced content files will remain in the package.", "Remove section", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            config.Navigation.Sections.Remove(section);
            RenderConfig(config, config.Navigation.Sections.FirstOrDefault()?.Id);
            EditorStatusText.Text = "Section removed from navigation. Its files remain in the source folder and package.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void MoveSectionUp_Click(object sender, RoutedEventArgs e) => MoveSelectedSection(-1);
    private void MoveSectionDown_Click(object sender, RoutedEventArgs e) => MoveSelectedSection(1);

    private void MoveSelectedSection(int offset)
    {
        try
        {
            var config = GetEditorConfig(applyMetadata: true);
            var section = GetSelectedSection(config) ?? throw new InvalidOperationException("Select a menu section first.");
            var index = config.Navigation.Sections.IndexOf(section);
            var destination = index + offset;
            if (destination < 0 || destination >= config.Navigation.Sections.Count) return;
            config.Navigation.Sections.RemoveAt(index);
            config.Navigation.Sections.Insert(destination, section);
            RenderConfig(config, section.Id);
            EditorStatusText.Text = "Section order updated. This is the order used by the runtime menu.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void AddPage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sourceDirectory = RequireSourceDirectory();
            var config = GetEditorConfig(applyMetadata: true);
            var section = GetSelectedSection(config) ?? throw new InvalidOperationException("Create or select a menu section first.");
            var dialog = new OpenFileDialog
            {
                Title = "Add an existing HTML page to this menu section",
                Filter = "Existing HTML pages (*.html;*.htm)|*.html;*.htm",
                InitialDirectory = sourceDirectory
            };
            if (dialog.ShowDialog(this) != true) return;
            var fullPath = Path.GetFullPath(dialog.FileName);
            var relativePath = Path.GetRelativePath(sourceDirectory, fullPath);
            if (relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                throw new InvalidOperationException("Choose an existing HTML page inside the selected source folder.");

            var title = Path.GetFileNameWithoutExtension(fullPath).Replace('-', ' ').Replace('_', ' ');
            section.Pages.Add(new NavigationPage
            {
                Id = CreateUniqueId(title, config.Navigation.Sections.SelectMany(item => item.Pages).Select(page => page.Id)),
                Title = title,
                Path = relativePath.Replace('\\', '/')
            });
            RenderConfig(config, section.Id, section.Pages[^1].Id);
            EditorStatusText.Text = $"Added existing page '{title}'. No HTML file was created or edited.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void ApplyPage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = GetEditorConfig(applyMetadata: true);
            var section = GetSelectedSection(config) ?? throw new InvalidOperationException("Select a menu section first.");
            var page = GetSelectedPage(section) ?? throw new InvalidOperationException("Select a menu page first.");
            page.Title = PageTitleText.Text.Trim();
            page.Path = PagePathText.Text.Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(page.Title) || string.IsNullOrWhiteSpace(page.Path))
                throw new InvalidOperationException("Page title and relative file path are required.");
            RenderConfig(config, section.Id, page.Id);
            EditorStatusText.Text = "Menu label and page path updated in the configuration. Validate before packaging.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void RemovePage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = GetEditorConfig(applyMetadata: true);
            var section = GetSelectedSection(config) ?? throw new InvalidOperationException("Select a menu section first.");
            var page = GetSelectedPage(section) ?? throw new InvalidOperationException("Select a menu page first.");
            section.Pages.Remove(page);
            RenderConfig(config, section.Id);
            EditorStatusText.Text = $"Removed '{page.Title}' from the menu. The source file remains in the package.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void MovePageUp_Click(object sender, RoutedEventArgs e) => MoveSelectedPage(-1);
    private void MovePageDown_Click(object sender, RoutedEventArgs e) => MoveSelectedPage(1);

    private void MoveSelectedPage(int offset)
    {
        try
        {
            var config = GetEditorConfig(applyMetadata: true);
            var section = GetSelectedSection(config) ?? throw new InvalidOperationException("Select a menu section first.");
            var page = GetSelectedPage(section) ?? throw new InvalidOperationException("Select a menu page first.");
            var index = section.Pages.IndexOf(page);
            var destination = index + offset;
            if (destination < 0 || destination >= section.Pages.Count) return;
            section.Pages.RemoveAt(index);
            section.Pages.Insert(destination, page);
            RenderConfig(config, section.Id, page.Id);
            EditorStatusText.Text = "Page order updated. This is the order used by the runtime menu.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void SectionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingEditor || SectionsListBox.SelectedItem is not NavigationSection section) return;
        SectionTitleText.Text = section.Title;
        PagesListBox.ItemsSource = section.Pages;
        PagesListBox.SelectedIndex = section.Pages.Count > 0 ? 0 : -1;
    }

    private void PagesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingEditor || PagesListBox.SelectedItem is not NavigationPage page) return;
        PageTitleText.Text = page.Title;
        PagePathText.Text = page.Path;
    }

    private void NavigationEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshingEditor || _sourceDirectory is null) return;
        try
        {
            var config = GetEditorConfig(applyMetadata: true);
            config.Navigation.Enabled = NavigationEnabledCheckBox.IsChecked == true;
            _currentConfig = config;
            ConfigTextBox.Text = _presentationService.Serialize(config);
            EditorStatusText.Text = config.Navigation.Enabled
                ? "Runtime navigation enabled."
                : "Runtime navigation disabled; pages remain in the package and can still be opened as the entry point.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void BrowseLogo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sourceDirectory = RequireSourceDirectory();
            var dialog = new OpenFileDialog
            {
                Title = "Choose an existing local logo image",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                InitialDirectory = sourceDirectory
            };
            if (dialog.ShowDialog(this) != true) return;
            var fullPath = Path.GetFullPath(dialog.FileName);
            var relativePath = Path.GetRelativePath(sourceDirectory, fullPath);
            if (relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                throw new InvalidOperationException("The logo must already be inside the selected source folder.");
            BrandLogoPathText.Text = relativePath.Replace('\\', '/');
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void MetadataField_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_refreshingEditor) _metadataDirty = true;
    }

    private PresentationConfig GetEditorConfig(bool applyMetadata = false)
    {
        if (_sourceDirectory is null) throw new InvalidOperationException("Choose a source folder first.");
        var config = _presentationService.Deserialize(ConfigTextBox.Text);
        if (applyMetadata && _metadataDirty)
        {
            ApplyMetadata(config);
            _currentConfig = config;
            ConfigTextBox.Text = _presentationService.Serialize(config);
            _metadataDirty = false;
        }
        _currentConfig = config;
        return config;
    }

    private void ApplyMetadata(PresentationConfig config)
    {
        config.Id = ConfigIdText.Text.Trim();
        config.Name = NameText.Text.Trim();
        config.Description = DescriptionText.Text.Trim();
        config.Version = VersionText.Text.Trim();
        config.EntryPoint = EntryPointText.Text.Trim().Replace('\\', '/');
        config.Branding.AccentColor = AccentText.Text.Trim();
        config.Branding.Logo = BrandLogoPathText.Text.Trim().Replace('\\', '/');
        config.Branding.TileTheme = (TileThemeCombo.SelectedItem as TileTheme)?.Id ?? config.Branding.TileTheme;
        config.Catalog.Organization = new OrganizationConfig
        {
            L3 = OrgL3Combo.SelectedItem as string ?? "", L4 = OrgL4Text.Text.Trim(), L5 = OrgL5Text.Text.Trim(),
            L6 = OrgL6Text.Text.Trim(), L7 = OrgL7Text.Text.Trim(), L8 = OrgL8Text.Text.Trim()
        };
    }

    private void OrgL3Combo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingEditor) _metadataDirty = true;
        UpdateTileThemePreview();
    }

    private void TileThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingEditor) _metadataDirty = true;
        UpdateTileThemePreview();
    }

    private void UpdateTileThemePreview()
    {
        if (TileThemePreview is null || TileThemeCombo.SelectedItem is not TileTheme theme) return;
        var accent = AccentText.Text.Trim();
        var seed = string.IsNullOrWhiteSpace(ConfigIdText.Text) ? "preview" : ConfigIdText.Text.Trim();
        TileThemePreview.Source = TileThemes.Banner(theme.Id, accent, seed);
        var business = OrgL3Combo.SelectedItem as string;
        TileThemePreviewBusiness.Text = (string.IsNullOrWhiteSpace(business) ? "Unassigned" : business).ToUpperInvariant();
        var line = new SolidColorBrush(TileThemes.LineColor(theme.Id, accent));
        line.Freeze();
        TileThemePreviewBusiness.Foreground = line;
        TileThemePreviewTitle.Text = NameText.Text.Trim();
    }

    private void RenderConfig(PresentationConfig config, string? selectedSectionId = null, string? selectedPageId = null)
    {
        _currentConfig = config;
        _refreshingEditor = true;
        ConfigTextBox.Text = _presentationService.Serialize(config);
        ConfigIdText.Text = config.Id;
        NameText.Text = config.Name;
        DescriptionText.Text = config.Description;
        VersionText.Text = config.Version;
        EntryPointText.Text = config.EntryPoint;
        AccentText.Text = config.Branding.AccentColor;
        BrandLogoPathText.Text = config.Branding.Logo;
        TileThemeCombo.SelectedItem = TileThemes.Get(config.Branding.TileTheme);
        var organization = config.Catalog.Organization;
        OrgL3Combo.ItemsSource ??= new[] { "" }.Concat(OrganizationConfig.BusinessLines).ToList();
        OrgL3Combo.SelectedItem = organization.L3 ?? "";
        OrgL4Text.Text = organization.L4; OrgL5Text.Text = organization.L5; OrgL6Text.Text = organization.L6;
        OrgL7Text.Text = organization.L7; OrgL8Text.Text = organization.L8;
        NavigationEnabledCheckBox.IsChecked = config.Navigation.Enabled;
        SectionsListBox.ItemsSource = null;
        SectionsListBox.ItemsSource = config.Navigation.Sections;
        var sectionIndex = selectedSectionId is null
            ? 0
            : config.Navigation.Sections.FindIndex(section => section.Id.Equals(selectedSectionId, StringComparison.OrdinalIgnoreCase));
        SectionsListBox.SelectedIndex = config.Navigation.Sections.Count == 0 ? -1 : Math.Clamp(sectionIndex < 0 ? 0 : sectionIndex, 0, config.Navigation.Sections.Count - 1);
        if (SectionsListBox.SelectedItem is NavigationSection selectedSection)
        {
            SectionTitleText.Text = selectedSection.Title;
            PagesListBox.ItemsSource = selectedSection.Pages;
            var pageIndex = selectedPageId is null
                ? 0
                : selectedSection.Pages.FindIndex(page => page.Id.Equals(selectedPageId, StringComparison.OrdinalIgnoreCase));
            PagesListBox.SelectedIndex = selectedSection.Pages.Count == 0 ? -1 : Math.Clamp(pageIndex < 0 ? 0 : pageIndex, 0, selectedSection.Pages.Count - 1);
            if (PagesListBox.SelectedItem is NavigationPage selectedPage)
            {
                PageTitleText.Text = selectedPage.Title;
                PagePathText.Text = selectedPage.Path;
            }
            else
            {
                PageTitleText.Text = "";
                PagePathText.Text = "";
            }
        }
        else
        {
            SectionTitleText.Text = "";
            PagesListBox.ItemsSource = null;
            PagesListBox.SelectedIndex = -1;
            PageTitleText.Text = "";
            PagePathText.Text = "";
        }
        _metadataDirty = false;
        _refreshingEditor = false;
        RenderDataSets(config);
    }

    private NavigationSection? GetSelectedSection(PresentationConfig config)
    {
        var selected = SectionsListBox.SelectedItem as NavigationSection;
        return selected is null ? null : config.Navigation.Sections.FirstOrDefault(section => section.Id == selected.Id);
    }

    private NavigationPage? GetSelectedPage(NavigationSection section)
    {
        var selected = PagesListBox.SelectedItem as NavigationPage;
        return selected is null ? null : section.Pages.FirstOrDefault(page => page.Id == selected.Id);
    }

    private string RequireSourceDirectory() => _sourceDirectory is not null
        ? _sourceDirectory
        : throw new InvalidOperationException("Choose a source folder first.");

    private void ShowValidationResult(PresentationConfig config, List<string> errors)
    {
        if (errors.Count > 0)
        {
            ValidationText.Text = string.Join("  •  ", errors);
            ValidationText.Foreground = new SolidColorBrush(Color.FromRgb(255, 139, 125));
            SetStatus("SOURCE NEEDS ATTENTION", "#FF8B7D");
            return;
        }

        var references = new AssetReferenceScanner().Scan(RequireSourceDirectory());
        var localCount = references.Count(reference => reference.Status == "Local");
        var externalCount = references.Count(reference => reference.Status == "External");
        ValidationText.Text = $"Ready · {config.Navigation.Sections.Count} menu sections · {config.Navigation.Sections.Sum(section => section.Pages.Count)} pages · {localCount} local references verified · {externalCount} remote references noted.";
        ValidationText.Foreground = new SolidColorBrush(Color.FromRgb(185, 204, 216));
        SetStatus("SOURCE VALIDATED", "#4BCD3E");
    }

    private static List<string> FormatInventory(IReadOnlyList<AssetInventoryItem> assets) => assets
        .Select(asset => $"{asset.Kind,-14} {asset.SizeLabel,8}   {asset.RelativePath}")
        .ToList();

    private static string BuildInventorySummary(IReadOnlyList<AssetInventoryItem> assets)
    {
        var images = assets.Count(asset => asset.Kind == "Image");
        var videos = assets.Count(asset => asset.Kind == "Video");
        var audio = assets.Count(asset => asset.Kind == "Audio");
        var bytes = assets.Sum(asset => asset.SizeBytes);
        var size = bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:F1} MB" : bytes >= 1024 ? $"{bytes / 1024d:F0} KB" : $"{bytes} B";
        return $"{assets.Count} files · {images} images · {videos} videos · {audio} audio · {size} total";
    }

    private static string CreateUniqueId(string value, IEnumerable<string> existingIds)
    {
        var baseId = new string(value.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(baseId)) baseId = "item";
        var ids = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var id = baseId;
        for (var suffix = 2; ids.Contains(id); suffix++) id = $"{baseId}-{suffix}";
        return id;
    }

    private void OpenPlayer(string packagePath, DemoLaunchContext? context = null)
    {
        AppLog.Info("Player", $"Opening {Path.GetFileName(packagePath)}" + (context is null ? "." : context.Track ? " (tracked)." : " (untracked)."));
        var player = new PlayerWindow(packagePath, context, context is null ? null : _usage) { Owner = this };
        player.Closed += (_, _) => { if (SettingsTab.Visibility == Visibility.Visible) RenderUsage(); };
        player.Show();
    }

    /// <summary>Formerly drove the header status pill; status changes are now written to the application log only.</summary>
    private static void SetStatus(string text, string color) =>
        AppLog.Write(color.Equals("#FF8B7D", StringComparison.OrdinalIgnoreCase) ? "WARN" : "INFO", "Studio", text);

    private readonly System.Windows.Threading.DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4.5) };

    /// <summary>Transient feedback toast at the bottom of the workspace. Every message is also logged.</summary>
    private void Notify(string message, bool error = false)
    {
        if (error) AppLog.Warn("Library", message); else AppLog.Info("Library", message);
        ToastText.Text = message;
        ToastHost.BorderBrush = new SolidColorBrush(error ? Color.FromRgb(0xFF, 0x8B, 0x7D) : Color.FromRgb(0x69, 0xE4, 0x3C));
        ToastHost.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(error ? 8 : 4.5);
        _toastTimer.Tick -= HideToast;
        _toastTimer.Tick += HideToast;
        _toastTimer.Start();
    }

    private void HideToast(object? sender, EventArgs e) { _toastTimer.Stop(); ToastHost.Visibility = Visibility.Collapsed; }

    private void ShowError(string message)
    {
        ValidationText.Text = message;
        EditorStatusText.Text = message;
        ValidationText.Foreground = new SolidColorBrush(Color.FromRgb(255, 139, 125));
        AppLog.Error("Studio", message);
        MessageBox.Show(this, message, "FIS Presentation Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
