using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using FisPresentationPlatform.Models;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform;

public partial class PlayerWindow : Window
{
    private const string VirtualHostName = "fis.example";
    private readonly string _packagePath;
    private readonly PresentationService _presentationService = new();
    private readonly List<NavigationPage> _pages = [];
    private string? _contentDirectory;
    private PresentationConfig? _config;
    private int _currentPageIndex;
    private long _navigationStartedAt;
    private TimeSpan? _domContentLoadedAfter;
    private string _pendingPageTitle = "Presentation";
    private WindowState _windowedState = WindowState.Normal;
    private WindowStyle _windowedStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _windowedResizeMode = ResizeMode.CanResize;
    private bool _isFullscreen;
    private bool _navigationVisible = true;
    private readonly PreflightRunner _preflight = new();
    private readonly RuntimeDiagnostics _diagnostics = new();
    private readonly DispatcherTimer _navigationTimeout = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly CancellationTokenSource _lifetime = new();
    private TaskCompletionSource<bool>? _navigationCompletion;
    private bool _loading;
    private bool _ready;
    private int _entryIndex;
    private readonly PageReadinessGate _pageReadiness = new();
    private ulong _activeNavigationId;
    private readonly NavigationTrace _navigationTrace;
    private bool _nativeNavigationPending;
    private bool _awaitingDocumentResponse;
    private readonly DemoLaunchContext? _launchContext;
    private readonly UsageTrackingService? _usageTracking;
    private UsageSession? _usageSession;

    public PlayerWindow(string packagePath, DemoLaunchContext? launchContext = null, UsageTrackingService? usageTracking = null)
    {
        InitializeComponent();
        Controls.WindowFitter.Attach(this, new Size(1400, 850), new Size(960, 640));
        _navigationTrace = new NavigationTrace(_diagnostics);
        _packagePath = packagePath;
        _launchContext = launchContext;
        _usageTracking = usageTracking;
        Loaded += PlayerWindow_Loaded;
        Closed += (_, _) =>
        {
            _usageSession?.End();
            _lifetime.Cancel();
            _pageReadiness.Fail();
            _navigationTimeout.Stop();
            _navigationCompletion?.TrySetCanceled();
            Browser.Dispose();
            _diagnostics.Record("Application", "PlayerClosed");
            if (_contentDirectory is not null)
            {
                try { Directory.Delete(_contentDirectory, true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        };
        _navigationTimeout.Tick += (_, _) =>
        {
            _pageReadiness.Fail();
            Browser.CoreWebView2?.Stop();
            _navigationTimeout.Stop();
            _loading = false;
            _navigationStartedAt = 0;
            _diagnostics.Record("Browser", "NavigationTimeout", "Error");
            _navigationTrace.Mark("TimedOut");
            var seconds = _config?.Loading.TimeoutSeconds ?? 15;
            ShowPlayerError($"The page did not report readiness within {seconds} seconds. Retry the page or open another page.");
            _navigationCompletion?.TrySetException(new TimeoutException($"The page readiness exceeded the {seconds}-second budget."));
            SetNavigationControlsEnabled(true);
        };
    }

    private async void PlayerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Browser.Visibility = Visibility.Hidden;
            DemoLoader.Visibility = Visibility.Visible;
            DemoLoader.Message = "Checking package integrity and configuration…";
            SetNavigationControlsEnabled(false);
            _diagnostics.Record("Application", "PlayerOpened");
            PageStatus.Text = "Pre-flight: checking package and configuration…";
            var packageOk = await _preflight.RunAsync("package", "Package integrity and configuration", async () =>
            {
                var package = await Task.Run(() => _presentationService.ExtractAndValidateAsync(_packagePath, _lifetime.Token), _lifetime.Token);
                _contentDirectory = package.Directory;
                _config = package.Config;
            });
            if (!packageOk) throw new InvalidDataException(_preflight.Results.Last().Message);
            if (_lifetime.IsCancellationRequested) return;
            if (_config is null || _contentDirectory is null) throw new InvalidDataException("Package pre-flight did not return content.");
            _navigationTimeout.Interval = TimeSpan.FromSeconds(_config.Loading.TimeoutSeconds);
            if (_launchContext is not null && _usageTracking is not null) _usageSession = _usageTracking.StartSession(_config, _launchContext);
            var navigation = RuntimeNavigationPlan.Create(_config);
            _pages.AddRange(navigation.Pages);
            _entryIndex = navigation.EntryIndex;
            Title = _config.Name;
            PresentationTitle.Text = _config.Name;
            PresentationVersion.Text = $"VERSION {_config.Version}";
            if (PresentationService.IsValidColor(_config.Branding.AccentColor))
            {
                var accent = (Color)ColorConverter.ConvertFromString(_config.Branding.AccentColor);
                BrandMark.Background = new SolidColorBrush(accent);
            }
            if (!string.IsNullOrWhiteSpace(_config.Branding.Logo))
            {
                var logoPath = Path.GetFullPath(Path.Combine(_contentDirectory, _config.Branding.Logo));
                var logo = new BitmapImage();
                logo.BeginInit();
                logo.CacheOption = BitmapCacheOption.OnLoad;
                logo.UriSource = new Uri(logoPath);
                logo.EndInit();
                logo.Freeze();
                BrandLogo.Source = logo;
                BrandLetter.Visibility = Visibility.Collapsed;
                BrandLogo.Visibility = Visibility.Visible;
            }
            if (!_config.Navigation.Enabled)
            {
                NavigationPane.Visibility = Visibility.Collapsed;
                NavigationColumn.Width = new GridLength(0);
                PreviousButton.Visibility = Visibility.Collapsed;
                NextButton.Visibility = Visibility.Collapsed;
                ProgressBar.Visibility = Visibility.Collapsed;
                ToggleNavigationButton.Visibility = Visibility.Collapsed;
                _navigationVisible = false;
            }

            foreach (var section in _config.Navigation.Sections)
            {
                NavigationPanel.Children.Add(new TextBlock
                {
                    Text = section.Title.ToUpperInvariant(),
                    Foreground = new SolidColorBrush(Color.FromRgb(131, 147, 161)),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(10, 14, 8, 7)
                });

                foreach (var page in section.Pages)
                {
                    var pageIndex = _pages.IndexOf(page);
                    var button = new Button
                    {
                        Content = page.Title,
                        Tag = pageIndex,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Background = Brushes.Transparent,
                        Foreground = new SolidColorBrush(Color.FromRgb(205, 215, 223)),
                        Padding = new Thickness(11, 10, 8, 10),
                        Margin = new Thickness(0, 1, 0, 2),
                        BorderThickness = new Thickness(0),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    button.Click += NavigationPage_Click;
                    NavigationPanel.Children.Add(button);
                }
            }

            PageStatus.Text = "Pre-flight: initializing WebView2…";
            DemoLoader.Message = "Preparing the presentation browser…";
            var browserOk = await _preflight.RunAsync("browser", "WebView2 initialization", InitializeBrowserAsync);
            if (!browserOk) throw new InvalidOperationException(_preflight.Results.Last().Message);
            PageStatus.Text = "Pre-flight: loading configured entry page…";
            var entryOk = await _preflight.RunAsync("entry", "Entry-page navigation", async () =>
            {
                await ShowPageAsync(_entryIndex);
                if (_navigationCompletion is not null) await _navigationCompletion.Task;
            });
            _diagnostics.Record("Preflight", _preflight.CanLaunch ? "Ready" : "Blocked", _preflight.CanLaunch ? "Information" : "Error");
            if (!entryOk || !_preflight.CanLaunch) throw new InvalidOperationException(_preflight.Results.Last().Message);
            _ready = true;
            Browser.Visibility = Visibility.Visible;
            DemoLoader.Visibility = Visibility.Collapsed;
            PlayerErrorPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            if (_lifetime.IsCancellationRequested) return;
            _diagnostics.Record("Preflight", "LaunchBlocked", "Error");
            ShowPlayerError(exception.Message);
            PageStatus.Text = "Pre-flight failed — launch blocked";
        }
    }

    private async Task InitializeBrowserAsync()
    {
        var initializationStarted = Stopwatch.GetTimestamp();
        var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FIS", "PresentationRuntime", "BrowserProfiles", _diagnostics.SessionId);
        ContentFileSafety.EnsureNoReparseAncestors(profile);
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
        await Browser.EnsureCoreWebView2Async(environment);
        _diagnostics.Record("Browser", "WebViewInitialized", durationMilliseconds: Stopwatch.GetElapsedTime(initializationStarted).TotalMilliseconds);
        _diagnostics.Record("Browser", "StandardWebViewLoading");
        Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        Browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
        Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHostName, _contentDirectory!, CoreWebView2HostResourceAccessKind.DenyCors);
        Browser.CoreWebView2.NavigationStarting += (_, args) =>
        {
            var handlerStarted = Stopwatch.GetTimestamp();
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals(VirtualHostName, StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
                _diagnostics.Record("Security", "NavigationRejected", "Warning");
                return;
            }

            var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            var matchingIndex = _pages.FindIndex(page => GetBrowserPath(page.Path).Equals(path, StringComparison.OrdinalIgnoreCase));
            if (matchingIndex < 0)
            {
                args.Cancel = true;
                _diagnostics.Record("Security", "UnconfiguredPageRejected", "Warning");
                return;
            }
            _currentPageIndex = matchingIndex;
            if (!_nativeNavigationPending)
            {
                _navigationTrace.Begin(_pages[matchingIndex].Id);
                _navigationTrace.Mark("DirectBrowserNavigation");
            }
            _nativeNavigationPending = false;
            _awaitingDocumentResponse = true;
            _navigationTrace.Mark("BrowserLoading");
            _activeNavigationId = args.NavigationId;
            _pendingPageTitle = _pages[matchingIndex].Title;
            _pageReadiness.Begin(_config?.Loading.ReadyMode == "message");
            Browser.Visibility = Visibility.Hidden;
            DemoLoader.Message = $"Loading {_pendingPageTitle}…";
            DemoLoader.Visibility = Visibility.Visible;
            PlayerErrorPanel.Visibility = Visibility.Collapsed;

            _navigationStartedAt = Stopwatch.GetTimestamp();
            _loading = true;
            _navigationTimeout.Stop();
            _navigationTimeout.Start();
            _domContentLoadedAfter = null;
            PageStatus.Text = $"Loading {_pendingPageTitle}…";
            SetNavigationControlsEnabled(false);
            _diagnostics.Record("NavigationHandler", "NavigationStartingCompleted", durationMilliseconds: Stopwatch.GetElapsedTime(handlerStarted).TotalMilliseconds,
                requestId: _navigationTrace.RequestId, pageId: _pages[matchingIndex].Id);
        };
        Browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
        Browser.CoreWebView2.DownloadStarting += (_, args) => args.Cancel = true;
        Browser.CoreWebView2.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        Browser.CoreWebView2.WebResourceResponseReceived += (_, args) =>
        {
            if (!_awaitingDocumentResponse || !Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri)
                || uri.Scheme != "https" || uri.Host != VirtualHostName) return;
            if (!Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Equals(GetBrowserPath(_pages[_currentPageIndex].Path), StringComparison.OrdinalIgnoreCase)) return;
            _awaitingDocumentResponse = false;
            _navigationTrace.Mark("DocumentResponse", args.Response.StatusCode);
        };
        // Runtime-generated instrumentation reports only fixed error kinds, never page text/URLs.
        await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
            (() => {
              const report = type => { if (window === window.top) window.chrome?.webview?.postMessage({type}); };
              window.addEventListener('error', e => report(e.target !== window ? 'resource-error' : 'javascript-error'), true);
              window.addEventListener('unhandledrejection', () => report('unhandled-rejection'));
                            if (window !== window.top) return;
                            let token = null, pending = false;
                            window.PresentationRuntime = window.PresentationRuntime || {};
                            window.PresentationRuntime.ready = () => {
                                pending = true;
                                if (token) window.chrome?.webview?.postMessage({type: 'presentation-ready', token});
                            };
                            window.__fisBindReady = value => {
                                token = value;
                                if (pending) window.PresentationRuntime.ready();
                            };
            })();
            """);
        await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BuildContextScript());
        Browser.CoreWebView2.WebMessageReceived += (_, args) =>
        {
            try
            {
                if (!Uri.TryCreate(args.Source, UriKind.Absolute, out var source) || source.Scheme != "https" || source.Host != VirtualHostName) return;
                using var json = System.Text.Json.JsonDocument.Parse(args.WebMessageAsJson);
                if (json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return;
                if (json.RootElement.TryGetProperty("type", out var messageType)
                    && messageType.ValueKind == System.Text.Json.JsonValueKind.String && messageType.GetString() == "presentation-ready"
                    && json.RootElement.TryGetProperty("token", out var token) && token.ValueKind == System.Text.Json.JsonValueKind.String
                    && _loading && Uri.UnescapeDataString(source.AbsolutePath).TrimStart('/') == GetBrowserPath(_pages[_currentPageIndex].Path)
                    && _pageReadiness.ReceiveReady(token.GetString()))
                {
                    _diagnostics.Record("Browser", "PageReadySignal");
                    CompletePageIfReady();
                }
                if (json.RootElement.TryGetProperty("type", out var type)
                    && type.ValueKind == System.Text.Json.JsonValueKind.String
                    && type.GetString() is "resource-error" or "javascript-error" or "unhandled-rejection")
                    _diagnostics.Record("Browser", type.GetString()!, "Warning");
            }
            catch (System.Text.Json.JsonException) { }
        };
        Browser.CoreWebView2.DOMContentLoaded += async (_, args) =>
        {
            if (args.NavigationId != _activeNavigationId) return;
            _navigationTrace.Mark("DomReady");
            if (_navigationStartedAt != 0)
                _domContentLoadedAfter = Stopwatch.GetElapsedTime(_navigationStartedAt);
            var token = _pageReadiness.Token;
            if (!_loading || token.Length == 0) return;
            try
            {
                await Browser.CoreWebView2.ExecuteScriptAsync($"window.__fisBindReady?.({System.Text.Json.JsonSerializer.Serialize(token)});");
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException)
            { _diagnostics.Record("Browser", "ReadinessBridgeFailed", "Warning"); }
        };
        Browser.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            if (_navigationStartedAt == 0 || args.NavigationId != _activeNavigationId) return;
            _navigationTrace.Mark(args.IsSuccess ? "BrowserCompleted" : "BrowserFailed");
            _pageReadiness.NavigationFinished(args.IsSuccess);

            var elapsed = Stopwatch.GetElapsedTime(_navigationStartedAt);
            var domElapsed = _domContentLoadedAfter?.TotalSeconds;
            PageStatus.Text = args.IsSuccess
                ? $"{_pendingPageTitle}  ·  {_currentPageIndex + 1} of {_pages.Count}  ·  DOM {(domElapsed.HasValue ? $"{domElapsed.Value:F1}s" : "—")} / load {elapsed.TotalSeconds:F1}s"
                : $"Page load failed: {args.WebErrorStatus}";
            _diagnostics.Record("Navigation", args.IsSuccess ? "PageLoaded" : "PageFailed", args.IsSuccess ? "Information" : "Error", elapsed.TotalMilliseconds);
            if (args.IsSuccess)
            {
                if (!_pageReadiness.IsReady)
                {
                    PageStatus.Text = $"{_pendingPageTitle}: waiting for presentation readiness…";
                    DemoLoader.Message = $"{_pendingPageTitle}: preparing content…";
                }
                CompletePageIfReady();
            }
            else
            {
                ShowPlayerError($"Page load failed: {args.WebErrorStatus}. Retry or select another page.");
                _navigationCompletion?.TrySetException(new InvalidOperationException($"Entry navigation failed: {args.WebErrorStatus}."));
            }
            if (!args.IsSuccess) SetNavigationControlsEnabled(true);
        };
        Browser.CoreWebView2.ProcessFailed += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                _navigationTimeout.Stop();
                _pageReadiness.Fail();
                _loading = false;
                _diagnostics.Record("Browser", "ProcessFailed", "Error");
                _navigationTrace.Mark("ProcessFailed");
                _navigationCompletion?.TrySetException(new InvalidOperationException("The embedded browser process failed."));
                ShowPlayerError("The embedded browser stopped unexpectedly. Close the player and try opening the package again.");
            });
        };
    }

    private async Task ShowPageAsync(int index)
    {
        if (_config is null || _contentDirectory is null || _pages.Count == 0) return;
        if (_loading) return;
        _currentPageIndex = Math.Clamp(index, 0, _pages.Count - 1);
        var page = _pages[_currentPageIndex];
        _navigationTrace.Begin(page.Id);
        _navigationTrace.Mark("NativeNavigation");
        _nativeNavigationPending = true;
        _pendingPageTitle = page.Title;
        _pageReadiness.Begin(_config.Loading.ReadyMode == "message");
        _activeNavigationId = 0;
        PageStatus.Text = $"Loading {page.Title}…";
        Browser.Visibility = Visibility.Hidden;
        DemoLoader.Message = $"Loading {page.Title}…";
        DemoLoader.Visibility = Visibility.Visible;
        PlayerErrorPanel.Visibility = Visibility.Collapsed;
        var targetPath = GetBrowserPath(page.Path);
        _navigationCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Observe navigation failures outside startup so they cannot become unobserved task exceptions.
        _ = _navigationCompletion.Task.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        _loading = true;
        SetNavigationControlsEnabled(false);
        var urlPath = string.Join('/', targetPath.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
        Browser.CoreWebView2.Navigate($"https://{VirtualHostName}/{urlPath}");
        ProgressBar.Value = (_currentPageIndex + 1d) / _pages.Count * 100;

        foreach (var item in NavigationPanel.Children.OfType<Button>())
        {
            var selected = item.Tag is int itemIndex && itemIndex == _currentPageIndex;
            item.Background = selected
                ? new SolidColorBrush(Color.FromRgb(39, 52, 64))
                : Brushes.Transparent;
            item.Foreground = selected
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(_config.Branding.AccentColor))
                : new SolidColorBrush(Color.FromRgb(205, 215, 223));
        }
        await Task.CompletedTask;
    }

    private void SetNavigationControlsEnabled(bool enabled)
    {
        foreach (var button in NavigationPanel.Children.OfType<Button>()) button.IsEnabled = enabled;
        PreviousButton.IsEnabled = enabled && _currentPageIndex > 0;
        NextButton.IsEnabled = enabled && _currentPageIndex < _pages.Count - 1;
    }

    private async void NavigationPage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index }) await ShowPageAsync(index);
    }

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPageIndex > 0) await ShowPageAsync(_currentPageIndex - 1);
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPageIndex < _pages.Count - 1) await ShowPageAsync(_currentPageIndex + 1);
    }

    private void ClosePlayer_Click(object sender, RoutedEventArgs e) => Close();

    private static string GetBrowserPath(string path) => IsMediaFile(path)
        ? $"__fis_runtime/{Path.GetFileNameWithoutExtension(path)}-player.html"
        : path.Replace('\\', '/');

    private void ShowPlayerError(string message)
    {
        _navigationTimeout.Stop();
        _pageReadiness.Fail();
        _loading = false;
        _navigationStartedAt = 0;
        DemoLoader.Visibility = Visibility.Collapsed;
        Browser.Visibility = Visibility.Hidden;
        PlayerErrorPanel.Visibility = Visibility.Visible;
        PlayerErrorText.Text = message;
    }

    /// <summary>
    /// Exposes the start-dialog choices to demo pages as <c>window.FIS_DEMO_CONTEXT</c>: persona/data set (with the parsed
    /// package JSON), client name and reason. Opportunity IDs stay in the usage log and are not shown on screen.
    /// </summary>
    private string BuildContextScript()
    {
        var dataSet = _launchContext?.DataSet ?? _config?.DataSets.FirstOrDefault(item => item.Default) ?? _config?.DataSets.FirstOrDefault();
        System.Text.Json.Nodes.JsonObject? data = null;
        var files = new System.Text.Json.Nodes.JsonObject();
        string? dataError = null;
        long dataBytes = 0;
        if (dataSet is not null && !string.IsNullOrWhiteSpace(dataSet.File) && _contentDirectory is not null)
        {
            try
            {
                var resolved = DataSetResolver.Resolve(_contentDirectory, dataSet.File);
                data = resolved.Data;
                dataBytes = resolved.Bytes;
                foreach (var (name, path) in resolved.Files) files[name] = path;
                if (!resolved.Ok)
                {
                    dataError = string.Join(" ", resolved.Errors);
                    _diagnostics.Record("Content", "DataSetUnreadable", "Warning");
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                dataError = exception.Message;
                _diagnostics.Record("Content", "DataSetUnreadable", "Warning");
            }
        }
        var context = new System.Text.Json.Nodes.JsonObject
        {
            ["dataSet"] = dataSet?.Id,
            ["dataSetName"] = dataSet?.Name,
            ["persona"] = dataSet?.Persona,
            ["clientName"] = _launchContext?.ClientName ?? "",
            ["reason"] = _launchContext?.Reason ?? "",
            ["tracked"] = _usageSession is not null,
            ["data"] = data,
            ["files"] = files,
            ["dataBytes"] = dataBytes,
            ["dataError"] = dataError
        };
        return $"window.FIS_DEMO_CONTEXT = Object.freeze({context.ToJsonString()});";
    }

    private void CompletePageIfReady()
    {
        if (!_loading || !_pageReadiness.IsReady) return;
        _navigationTimeout.Stop();
        _loading = false;
        var elapsed = _navigationStartedAt == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_navigationStartedAt);
        _diagnostics.Record("Navigation", "PageReady", durationMilliseconds: elapsed.TotalMilliseconds);
        _navigationTrace.Mark("Available");
        PageStatus.Text += $" · request→ready {_navigationTrace.ElapsedMilliseconds / 1000:F2}s";
        _navigationStartedAt = 0;
        if (_ready)
        {
            DemoLoader.Visibility = Visibility.Collapsed;
            Browser.Visibility = Visibility.Visible;
            PlayerErrorPanel.Visibility = Visibility.Collapsed;
        }
        _navigationCompletion?.TrySetResult(true);
        SetNavigationControlsEnabled(true);
        if (_currentPageIndex < _pages.Count) _usageSession?.PageViewed(_pages[_currentPageIndex].Id, _pages[_currentPageIndex].Title);
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Diagnostics JSON (*.json)|*.json", FileName = "fis-diagnostics.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { await File.WriteAllTextAsync(dialog.FileName, _diagnostics.Export(_preflight.Results)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, exception.Message, "Could not export diagnostics"); }
    }

    private async void Home_Click(object sender, RoutedEventArgs e) { if (_ready) await ShowPageAsync(_entryIndex); }

    private async void RetryPage_Click(object sender, RoutedEventArgs e) { if (_ready) await ShowPageAsync(_currentPageIndex); }

    private static bool IsMediaFile(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".bmp" or ".avif"
        or ".mp4" or ".m4v" or ".mov" or ".webm" or ".ogv"
        or ".mp3" or ".wav" or ".ogg" or ".m4a";

    private void ToggleNavigation_Click(object sender, RoutedEventArgs e)
    {
        if (_config?.Navigation.Enabled != true) return;
        _navigationVisible = !_navigationVisible;
        NavigationPane.Visibility = _navigationVisible ? Visibility.Visible : Visibility.Collapsed;
        NavigationColumn.Width = _navigationVisible ? new GridLength(250) : new GridLength(0);
        ToggleNavigationButton.Content = _navigationVisible ? "Hide menu" : "Show menu";
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (_isFullscreen)
        {
            WindowStyle = _windowedStyle;
            ResizeMode = _windowedResizeMode;
            WindowState = _windowedState;
            _isFullscreen = false;
            FullscreenButton.Content = "Fullscreen";
            return;
        }

        _windowedState = WindowState;
        _windowedStyle = WindowStyle;
        _windowedResizeMode = ResizeMode;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        _isFullscreen = true;
        FullscreenButton.Content = "Exit fullscreen";
    }

    private async void PlayerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isFullscreen)
        {
            Fullscreen_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            Fullscreen_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Right && _currentPageIndex < _pages.Count - 1)
        {
            await ShowPageAsync(_currentPageIndex + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && _currentPageIndex > 0)
        {
            await ShowPageAsync(_currentPageIndex - 1);
            e.Handled = true;
        }
    }
}
