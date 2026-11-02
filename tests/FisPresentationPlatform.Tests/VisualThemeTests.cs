using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FisPresentationPlatform.Tests;

public sealed class VisualThemeTests
{
    [Fact]
    public void ShellAndSelectedTabs_UseExplicitCharcoalAndGreenTemplates()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application();
                app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                    new Uri("/FisPresentationPlatform;component/Themes/FisTheme.xaml", UriKind.Relative)));
                var window = new MainWindow();
                var loader = new FisPresentationPlatform.Controls.DemoLoadingView { Message = "Preparing test content" };
                loader.Measure(new Size(600, 400)); loader.Arrange(new Rect(0, 0, 600, 400));
                Assert.Equal("Preparing test content", loader.Message);
                Assert.IsType<RotateTransform>(((System.Windows.Shapes.Path)loader.FindName("Spinner")).RenderTransform);
                var service = new FisPresentationPlatform.Services.PresentationService();
                var config = service.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "BasicPresentation", "presentation.json")));
                typeof(MainWindow).GetMethod("RenderConfig", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, [config, null, null]);
                var root = (Grid)window.Content;
                root.Measure(new Size(1440, 860));
                root.Arrange(new Rect(0, 0, 1440, 860));
                root.UpdateLayout();

                var banner = (Grid)window.FindName("HeroBanner");
                Assert.Equal(0, Grid.GetRow(banner));
                Assert.Null(window.FindName("LocationTag"));
                Assert.Null(window.FindName("ConfigurationNavButton"));
                Assert.Equal(banner, ((FrameworkElement)window.FindName("HeroTextPanel")).Parent);
                var showWorkspace = typeof(MainWindow).GetMethod("ShowWorkspace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var workspace = (Grid)window.FindName("WorkspaceSurface");
                var workspaceBrush = Assert.IsType<SolidColorBrush>(workspace.Background);
                Assert.Equal(Color.FromRgb(16, 25, 37), workspaceBrush.Color);
                var divider = (Border)window.FindName("BannerContentDivider");
                Assert.Equal(1, divider.Height);
                Assert.Equal(((SolidColorBrush)((Border)window.FindName("SidebarBackground")).BorderBrush).Color,
                    ((SolidColorBrush)divider.Background).Color);
                var glassBrush = (LinearGradientBrush)app.Resources["PanelGradient"];
                Assert.All(glassBrush.GradientStops, stop => Assert.True(stop.Color.A < 255));
                var artwork = (Border)window.FindName("HeroArtwork");
                Assert.Equal(560, artwork.Width);
                Assert.Equal(210, artwork.Height);
                Assert.Equal(Color.FromRgb(16, 25, 37), ((SolidColorBrush)banner.Background).Color);
                var bannerBrush = Assert.IsType<ImageBrush>(artwork.Background);
                Assert.Equal(Stretch.Uniform, bannerBrush.Stretch);
                Assert.True(((BitmapSource)bannerBrush.ImageSource).PixelWidth > 0);
                var menuArtwork = (Image)window.FindName("SidebarArtwork");
                Assert.Equal(520, menuArtwork.Width);
                Assert.Equal(292, menuArtwork.Height);
                Assert.Equal(0.5, menuArtwork.Opacity);
                Assert.Equal(HorizontalAlignment.Left, menuArtwork.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Bottom, menuArtwork.VerticalAlignment);
                Assert.True(((BitmapSource)menuArtwork.Source).PixelWidth > 0);

                var tabs = (TabControl)window.FindName("MainViews");
                tabs.ApplyTemplate();
                var headerScroll = (ScrollViewer)tabs.Template.FindName("TabHeaderScroll", tabs);
                Assert.Equal(new Thickness(0), headerScroll.Margin);
                var contentSurface = (Border)tabs.Template.FindName("ContentSurface", tabs);
                Assert.Equal(Color.FromRgb(16, 25, 37), ((SolidColorBrush)contentSurface.Background).Color);
                Assert.Equal(2, tabs.SelectedIndex);
                Assert.Equal(["Welcome", "Demo Library"], tabs.Items.Cast<TabItem>().Where(tab => tab.Visibility == Visibility.Visible).Select(tab => tab.Header.ToString()));
                showWorkspace.Invoke(window, ["config"]);
                root.UpdateLayout();
                Assert.Equal(1, tabs.SelectedIndex);
                var configTab = (TabItem)tabs.Items[1];
                configTab.ApplyTemplate();
                var chrome = (Border)configTab.Template.FindName("Chrome", configTab);
                Assert.IsType<LinearGradientBrush>(chrome.Background);
                Assert.Equal(Colors.White, ((SolidColorBrush)configTab.Foreground).Color);
                var field = (TextBox)window.FindName("NameText");
                Assert.Equal(Color.FromRgb(16, 25, 37), ((SolidColorBrush)field.Background).Color);
                Assert.Equal(config.Name, field.Text);
                Assert.NotNull(app.Resources[typeof(ScrollBar)]);
                var builder = (Button)window.FindName("BuilderNavButton");
                builder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, tabs.SelectedIndex);
                Assert.Single(tabs.Items.Cast<TabItem>(), tab => tab.Visibility == Visibility.Visible);
                Assert.Equal("Active", builder.Tag);
                showWorkspace.Invoke(window, ["config"]);
                Assert.Equal(1, tabs.SelectedIndex);

                root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1440, 860, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(AppContext.BaseDirectory, "studio-theme-preview.png"));
                encoder.Save(output);
                var libraryButton = (Button)window.FindName("LibraryNavButton");
                libraryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(3, tabs.SelectedIndex);
                Assert.Single(tabs.Items.Cast<TabItem>(), tab => tab.Visibility == Visibility.Visible);
                Assert.Equal("Active", libraryButton.Tag);
                var demo = new FisPresentationPlatform.Services.LibraryDemo
                {
                    Hash = new string('a', 64), Config = config, ImportedUtc = DateTimeOffset.UtcNow,
                    PackagePath = Path.Combine(AppContext.BaseDirectory, "sample-test.fisxpkg")
                };
                typeof(MainWindow).GetField("_libraryDemos", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(window, new List<FisPresentationPlatform.Services.LibraryDemo> { demo });
                typeof(MainWindow).GetField("_highlightedDemoId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(window, config.Id);
                typeof(MainWindow).GetMethod("UpdateLibraryView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
                var cards = (ItemsControl)window.FindName("LibraryCards");
                Assert.Single(cards.Items.Cast<object>());
                var renderedCard = cards.Items[0];
                Assert.Equal(true, renderedCard.GetType().GetProperty("Highlighted")!.GetValue(renderedCard));
                root.UpdateLayout();
                var libraryBitmap = new RenderTargetBitmap(1440, 860, 96, 96, PixelFormats.Pbgra32);
                libraryBitmap.Render(root);
                var libraryEncoder = new PngBitmapEncoder(); libraryEncoder.Frames.Add(BitmapFrame.Create(libraryBitmap));
                using (var libraryOutput = File.Create(Path.Combine(AppContext.BaseDirectory, "library-theme-preview.png"))) libraryEncoder.Save(libraryOutput);
                ((TextBox)window.FindName("LibrarySearchText")).Text = "does-not-match";
                Assert.Empty(cards.Items.Cast<object>());
                Assert.Equal(Visibility.Visible, ((StackPanel)window.FindName("LibraryEmptyState")).Visibility);
                ((Button)window.FindName("WelcomeNavButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(2, tabs.SelectedIndex);
                Assert.Equal(["Welcome", "Demo Library"], tabs.Items.Cast<TabItem>().Where(tab => tab.Visibility == Visibility.Visible).Select(tab => tab.Header.ToString()));
                using (var feed = new FisPresentationPlatform.Services.WelcomeFeedService(Path.Combine(Path.GetTempPath(), "fis-visual-news")))
                    ((ItemsControl)window.FindName("WelcomeItems")).ItemsSource = Task.Run(() => feed.LoadSampleAsync()).GetAwaiter().GetResult().Items;
                ((StackPanel)window.FindName("WelcomeEmptyState")).Visibility = Visibility.Collapsed;
                root.UpdateLayout();
                var welcomeBitmap = new RenderTargetBitmap(1440, 860, 96, 96, PixelFormats.Pbgra32);
                welcomeBitmap.Render(root);
                var welcomeEncoder = new PngBitmapEncoder(); welcomeEncoder.Frames.Add(BitmapFrame.Create(welcomeBitmap));
                using (var welcomeOutput = File.Create(Path.Combine(AppContext.BaseDirectory, "welcome-theme-preview.png"))) welcomeEncoder.Save(welcomeOutput);
                ((Button)window.FindName("SettingsNavButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(4, tabs.SelectedIndex);
                Assert.Single(tabs.Items.Cast<TabItem>(), tab => tab.Visibility == Visibility.Visible);
                Assert.Equal("Settings", ((TabItem)tabs.SelectedItem).Header);
                root.UpdateLayout();
                var settingsTabs = Descendants((DependencyObject)((TabItem)tabs.SelectedItem).Content).OfType<TabControl>().First();
                settingsTabs.ApplyTemplate();
                Assert.IsType<StackPanel>(settingsTabs.Template.FindName("HeaderStrip", settingsTabs));
                foreach (TabItem tab in settingsTabs.Items)
                {
                    tab.ApplyTemplate();
                    tab.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var header = (ContentPresenter)tab.Template.FindName("HeaderPresenter", tab);
                    Assert.True(tab.DesiredSize.Width >= header.DesiredSize.Width,
                        "Tab header must not be squeezed or clipped.");
                    Assert.True(tab.ActualWidth >= header.ActualWidth + tab.Padding.Left + tab.Padding.Right,
                        "Rendered tab must leave space for its header and padding.");
                }
                var primary = new Button { Content = "Save", Style = (Style)app.Resources["PrimaryButtonStyle"] };
                primary.ApplyTemplate();
                Assert.Equal(new Thickness(0), primary.BorderThickness);
                Assert.Equal(new Thickness(0), ((Border)primary.Template.FindName("Chrome", primary)).BorderThickness);
                var startupCheck = new FisPresentationPlatform.Services.StartupCheck("Local Demo Library", true);
                startupCheck.RunAsync(() => Task.FromResult("3 multi-page examples available")).GetAwaiter().GetResult();
                var startupItems = (ItemsControl)window.FindName("StartupCheckList");
                startupItems.ItemsSource = Enumerable.Range(0, 8).Select(_ => startupCheck).ToArray();
                var startupPanel = (Border)window.FindName("StartupOverlay");
                startupPanel.Visibility = Visibility.Visible;
                root.UpdateLayout();
                Assert.True(startupPanel.ActualWidth > 0);
                var startupGrid = Descendants(startupItems).OfType<WrapPanel>().Single();
                Assert.Equal(420, startupGrid.ItemWidth);
                Assert.Equal(8, startupGrid.Children.Count);
                var startupBitmap = new RenderTargetBitmap(1440, 860, 96, 96, PixelFormats.Pbgra32);
                startupBitmap.Render(root);
                var startupEncoder = new PngBitmapEncoder(); startupEncoder.Frames.Add(BitmapFrame.Create(startupBitmap));
                using (var startupOutput = File.Create(Path.Combine(AppContext.BaseDirectory, "startup-theme-preview.png"))) startupEncoder.Save(startupOutput);
                VerifyPlayerLoadsAndNavigates();
                VerifyBareVisibleBrowser();
                window.Close();
                app.Shutdown();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF visual test exceeded its time limit.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifyPlayerLoadsAndNavigates()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "samples", "NavigationBaseline");
        var package = Path.Combine(Path.GetTempPath(), "fis-player-smoke-" + Guid.NewGuid().ToString("N") + ".fisxpkg");
        PlayerWindow? player = null;
        try
        {
            Task.Run(async () =>
            {
                var service = new FisPresentationPlatform.Services.PresentationService();
                var config = service.Deserialize(await File.ReadAllTextAsync(Path.Combine(source, "presentation.json")));
                await service.BuildPackageAsync(source, package, config);
            }).GetAwaiter().GetResult();
            player = new PlayerWindow(package);
            Assert.IsType<Microsoft.Web.WebView2.Wpf.WebView2>(player.FindName("Browser"));
            player.Show();
            PumpUntilReady(player);
            var browser = (Microsoft.Web.WebView2.Wpf.WebView2)player.FindName("Browser");
            Assert.EndsWith("/index.html", browser.CoreWebView2.Source);
            ((Button)player.FindName("NextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntilReady(player);
            Assert.EndsWith("/second.html", browser.CoreWebView2.Source);
            Assert.Equal("fis.example", new Uri(browser.CoreWebView2.Source).Host);
            _ = browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('a').click();");
            PumpUntilReady(player, "/index.html");
            Assert.EndsWith("/index.html", browser.CoreWebView2.Source);
            var diagnostics = (FisPresentationPlatform.Services.RuntimeDiagnostics)typeof(PlayerWindow)
                .GetField("_diagnostics", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(player)!;
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "player-origin-trace.json"), diagnostics.Export([]));
        }
        finally
        {
            player?.Close();
            if (File.Exists(package)) File.Delete(package);
        }
    }

    private static void VerifyBareVisibleBrowser()
    {
        var root = Path.Combine(Path.GetTempPath(), "fis-visible-probe-" + Guid.NewGuid().ToString("N"));
        var browser = new Microsoft.Web.WebView2.Wpf.WebView2();
        var host = new Window { Width = 800, Height = 500, Content = browser, ShowInTaskbar = false };
        var timings = new List<double>();
        Exception? failure = null;
        var completed = false;
        host.Loaded += async (_, _) =>
        {
            try
            {
                var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(userDataFolder: root);
                await browser.EnsureCoreWebView2Async(environment);
                browser.CoreWebView2.SetVirtualHostNameToFolderMapping("fis.local", Path.Combine(AppContext.BaseDirectory, "samples", "NavigationBaseline"),
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.DenyCors);
                foreach (var page in new[] { "index.html", "second.html" })
                {
                    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs> handler = (_, args) => completion.TrySetResult(args.IsSuccess);
                    browser.CoreWebView2.NavigationCompleted += handler;
                    var started = System.Diagnostics.Stopwatch.StartNew();
                    browser.CoreWebView2.Navigate("https://fis.local/" + page);
                    Assert.True(await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)));
                    timings.Add(started.Elapsed.TotalMilliseconds);
                    browser.CoreWebView2.NavigationCompleted -= handler;
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { completed = true; }
        };
        host.Show();
        var frame = new System.Windows.Threading.DispatcherFrame();
        var budget = System.Diagnostics.Stopwatch.StartNew();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => { if (completed || budget.Elapsed.TotalSeconds > 12) frame.Continue = false; };
        timer.Start();
        try
        {
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            Assert.True(completed, "Bare WebView2 probe timed out.");
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "bare-visible-timings.json"), System.Text.Json.JsonSerializer.Serialize(timings));
        }
        finally { timer.Stop(); browser.Dispose(); host.Close(); }
    }

    private static void PumpUntilReady(PlayerWindow player, string? expectedPath = null)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var started = System.Diagnostics.Stopwatch.StartNew();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        var readyField = typeof(PlayerWindow).GetField("_ready", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var loadingField = typeof(PlayerWindow).GetField("_loading", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        timer.Tick += (_, _) =>
        {
            var browser = (Microsoft.Web.WebView2.Wpf.WebView2)player.FindName("Browser");
            if ((bool)readyField.GetValue(player)! && !(bool)loadingField.GetValue(player)!
                && (expectedPath is null || browser.CoreWebView2.Source.EndsWith(expectedPath, StringComparison.Ordinal))
                || started.Elapsed > TimeSpan.FromSeconds(12)) frame.Continue = false;
        };
        timer.Start();
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.True((bool)readyField.GetValue(player)! && !(bool)loadingField.GetValue(player)!,
            "Live WebView2 baseline did not reach readiness within the test budget.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}