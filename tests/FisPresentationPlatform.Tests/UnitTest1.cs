using System.IO.Compression;
using FisPresentationPlatform.Models;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Tests;

public sealed class PresentationServiceTests
{
    private readonly PresentationService _service = new();

    [Fact]
    public void ValidateConfiguration_ReportsMissingConfiguredPage()
    {
        using var source = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(source.Path, "index.html"), "<h1>Home</h1>");
        var config = CreateConfig();

        var errors = _service.ValidateConfiguration(config, source.Path);

        Assert.Contains(errors, error => error.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAndExtractPackage_PreservesLocalPagesAndValidatesHashes()
    {
        using var source = new TemporaryDirectory();
        var contentDirectory = Path.Combine(source.Path, "content");
        Directory.CreateDirectory(Path.Combine(contentDirectory, "pages"));
        File.WriteAllText(Path.Combine(contentDirectory, "index.html"), "<h1>Welcome</h1>");
        File.WriteAllText(Path.Combine(contentDirectory, "pages", "details.html"), "<h1>Details</h1>");
        File.WriteAllText(Path.Combine(contentDirectory, "styles.css"), "body { color: navy; }");
        File.WriteAllText(Path.Combine(contentDirectory, "old-export.fisxpkg"), "not source content");
        var config = CreateConfig();
        File.WriteAllText(Path.Combine(contentDirectory, PresentationService.ConfigurationFileName), _service.Serialize(config));
        var packagePath = Path.Combine(source.Path, "demo.fisxpkg");

        var manifest = await _service.BuildPackageAsync(contentDirectory, packagePath, config);
        var extracted = await _service.ExtractAndValidateAsync(packagePath);

        Assert.Equal(4, manifest.Files.Count);
        Assert.Equal(config.Name, extracted.Config.Name);
        Assert.True(File.Exists(Path.Combine(extracted.Directory, "pages", "details.html")));
        Assert.True(File.Exists(Path.Combine(extracted.Directory, "styles.css")));
        Assert.DoesNotContain(manifest.Files, file => file.Path.EndsWith(".fisxpkg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPackage_IncludesImageAndPlayableMp4WithoutRecompressingMedia()
    {
        using var source = new TemporaryDirectory();
        var contentDirectory = Path.Combine(source.Path, "media-presentation");
        Directory.CreateDirectory(Path.Combine(contentDirectory, "media"));
        File.WriteAllText(Path.Combine(contentDirectory, "index.html"), "<video controls src='media/demo.mp4'></video>");
        File.WriteAllText(Path.Combine(contentDirectory, "media", "poster.svg"), "<svg xmlns='http://www.w3.org/2000/svg'></svg>");
        var videoBytes = Enumerable.Range(0, 2048).Select(value => (byte)(value % 251)).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(contentDirectory, "media", "demo.mp4"), videoBytes);
        var config = CreateConfig(singlePage: true);
        var packagePath = Path.Combine(source.Path, "media.fisxpkg");

        var manifest = await _service.BuildPackageAsync(contentDirectory, packagePath, config);
        using var archive = ZipFile.OpenRead(packagePath);
        var videoEntry = archive.GetEntry("media/demo.mp4")!;

        Assert.Contains(manifest.Files, file => file.Path == "media/poster.svg" && file.ContentType == "image/svg+xml");
        Assert.Contains(manifest.Files, file => file.Path == "media/demo.mp4" && file.ContentType == "video/mp4");
        Assert.Equal(videoEntry.Length, videoEntry.CompressedLength);
    }

    [Fact]
    public async Task BuildPackage_CreatesManifestedPlayerPageForVideoOnlyPresentation()
    {
        using var source = new TemporaryDirectory();
        var contentDirectory = Path.Combine(source.Path, "video-only");
        Directory.CreateDirectory(contentDirectory);
        await File.WriteAllBytesAsync(Path.Combine(contentDirectory, "launch.mp4"), [0, 1, 2, 3, 4]);
        var config = CreateConfig(singlePage: true);
        config.EntryPoint = "launch.mp4";
        config.Navigation.Sections[0].Pages[0].Path = "launch.mp4";
        var packagePath = Path.Combine(source.Path, "video-only.fisxpkg");

        var manifest = await _service.BuildPackageAsync(contentDirectory, packagePath, config);
        var extracted = await _service.ExtractAndValidateAsync(packagePath);

        Assert.Contains(manifest.Files, file => file.Path == "__fis_runtime/launch-player.html" && file.ContentType == "text/html");
        Assert.True(File.Exists(Path.Combine(extracted.Directory, "__fis_runtime", "launch-player.html")));
        Assert.True(File.Exists(Path.Combine(extracted.Directory, "launch.mp4")));
    }

    [Fact]
    public async Task LoadOrCreateConfig_GeneratesNavigationFromHtmlPages()
    {
        using var source = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(source.Path, "nested"));
        File.WriteAllText(Path.Combine(source.Path, "index.html"), "<h1>Home</h1>");
        File.WriteAllText(Path.Combine(source.Path, "nested", "details.html"), "<h1>Details</h1>");

        var result = await _service.LoadOrCreateConfigAsync(source.Path);

        Assert.True(result.Created);
        Assert.Equal(2, result.Config.Navigation.Sections.Single().Pages.Count);
        Assert.True(File.Exists(Path.Combine(source.Path, PresentationService.ConfigurationFileName)));
        Assert.Equal("index.html", result.Config.EntryPoint);
    }

    [Fact]
    public void SerializeAndDeserialize_PreservesSectionAndPageOrder()
    {
        var config = CreateConfig();
        config.Navigation.Sections[0].Pages.Reverse();
        config.Navigation.Sections.Insert(0, new NavigationSection
        {
            Id = "opening",
            Title = "Opening",
            Pages = [new NavigationPage { Id = "intro", Title = "Intro", Path = "index.html" }]
        });

        var roundTrip = _service.Deserialize(_service.Serialize(config));

        Assert.Equal(["opening", "main"], roundTrip.Navigation.Sections.Select(section => section.Id));
        Assert.Equal(["details", "home"], roundTrip.Navigation.Sections[1].Pages.Select(page => page.Id));
    }

    [Fact]
    public void ValidateConfiguration_ReportsTraversalAndDuplicatePageIdentifiers()
    {
        using var source = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(source.Path, "index.html"), "<h1>Home</h1>");
        var config = CreateConfig();
        config.Navigation.Sections[0].Pages.Add(new NavigationPage { Id = "home", Title = "Escape", Path = "../outside.html" });

        var errors = _service.ValidateConfiguration(config, source.Path);

        Assert.Contains(errors, error => error.Contains("duplicated", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("unsafe relative path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateConfiguration_FindsMissingImageAndCssFontButAllowsRemoteAssets()
    {
        using var source = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(source.Path, "index.html"),
            "<link rel='stylesheet' href='styles.css'><img src='images/missing.png'><script src='https://cdn.example.test/app.js'></script>");
        File.WriteAllText(Path.Combine(source.Path, "styles.css"), "@font-face { src: url('fonts/missing.woff2'); }");
        var config = CreateConfig(singlePage: true);

        var errors = _service.ValidateConfiguration(config, source.Path);
        var references = new AssetReferenceScanner().Scan(source.Path);

        Assert.Contains(errors, error => error.Contains("images/missing.png", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("fonts/missing.woff2", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, reference => reference.Reference.StartsWith("https://cdn.example.test", StringComparison.OrdinalIgnoreCase)
                                                 && reference.Status == "External");
    }

    [Fact]
    public void AssetInventory_ClassifiesMediaAndSkipsDevelopmentDirectories()
    {
        using var source = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(source.Path, "media"));
        Directory.CreateDirectory(Path.Combine(source.Path, "node_modules", "vendor"));
        File.WriteAllBytes(Path.Combine(source.Path, "media", "demo.mp4"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(source.Path, "media", "poster.svg"), "<svg />");
        File.WriteAllText(Path.Combine(source.Path, "node_modules", "vendor", "ignored.js"), "ignored");

        var assets = new AssetInventoryService().Scan(source.Path);

        Assert.Contains(assets, asset => asset.RelativePath == "media/demo.mp4" && asset.Kind == "Video");
        Assert.Contains(assets, asset => asset.RelativePath == "media/poster.svg" && asset.Kind == "Image");
        Assert.DoesNotContain(assets, asset => asset.RelativePath.Contains("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExtractAndValidate_RejectsTamperedPackageContent()
    {
        using var source = new TemporaryDirectory();
        var contentDirectory = Path.Combine(source.Path, "content");
        Directory.CreateDirectory(contentDirectory);
        File.WriteAllText(Path.Combine(contentDirectory, "index.html"), "<h1>Welcome</h1>");
        var config = CreateConfig(singlePage: true);
        var packagePath = Path.Combine(source.Path, "tampered.fisxpkg");
        await _service.BuildPackageAsync(contentDirectory, packagePath, config);

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            archive.GetEntry("index.html")!.Delete();
            var changed = archive.CreateEntry("index.html");
            await using var stream = changed.Open();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync("<h1>Changed after build</h1>");
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => _service.ExtractAndValidateAsync(packagePath));
        Assert.Contains("integrity check failed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PresentationConfig CreateConfig(bool singlePage = false) => new()
    {
        Id = $"test-presentation-{Guid.NewGuid():N}",
        Name = "Test presentation",
        EntryPoint = "index.html",
        Navigation = new NavigationConfig
        {
            Sections =
            [
                new NavigationSection
                {
                    Id = "main",
                    Title = "Main",
                    Pages = singlePage
                        ? [new NavigationPage { Id = "home", Title = "Home", Path = "index.html" }]
                        : [
                            new NavigationPage { Id = "home", Title = "Home", Path = "index.html" },
                            new NavigationPage { Id = "details", Title = "Details", Path = "pages/details.html" }
                        ]
                }
            ]
        }
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
