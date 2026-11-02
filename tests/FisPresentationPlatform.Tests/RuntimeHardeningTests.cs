using System.IO.Compression;
using System.Text.Json;
using FisPresentationPlatform.Models;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Tests;

public sealed class RuntimeHardeningTests
{
    [Theory]
    [InlineData("../outside.html")]
    [InlineData("CON.txt")]
    [InlineData("pages/file.html.")]
    [InlineData("pages/file.html ")]
    [InlineData("pages/file.html:stream")]
    [InlineData("pages/./home.html")]
    [InlineData("C:\\outside.html")]
    public void UnsafeWindowsPath_IsRejected(string path) => Assert.False(ContentFileSafety.IsSafeRelativePath(path));

    [Fact]
    public void SafePackagePath_AllowsNestedMediaAndSpaces() => Assert.True(ContentFileSafety.IsSafeRelativePath("media/my clip.mp4"));

    [Fact]
    public void Schema_AllowsAbsentCatalogForOldV1Packages()
    {
        var config = new PresentationService().Deserialize("""
            {"schemaVersion":"1.0","id":"old-demo","name":"Old demo","version":"1.0.0","entryPoint":"index.html"}
            """);
        Assert.NotNull(config.Catalog);
        Assert.Empty(config.Catalog.Tags);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":\"1.0\"}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"id\":\"demo\",\"name\":null,\"version\":\"1.0.0\",\"entryPoint\":\"index.html\"}")]
    public void InvalidDocument_IsRejectedBeforeModelDefaults(string json) => Assert.Throws<JsonException>(() => new PresentationService().Deserialize(json));

    [Fact]
    public void SchemaAndSemanticValidation_HandleTypedNullsAndTagLimits()
    {
        using var fixture = new Fixture();
        var service = fixture.Service;
        fixture.Config.Branding = null!;
        Assert.NotEmpty(service.ValidateConfiguration(fixture.Config, fixture.Source));
        fixture.Config.Branding = new BrandingConfig();
        fixture.Config.Catalog.Tags = Enumerable.Range(0, 31).Select(index => $"tag-{index}").ToList();
        Assert.Contains(service.ValidateConfiguration(fixture.Config, fixture.Source), error => error.Contains("catalog/tags"));
        fixture.Config.Catalog.Tags = [new string('a', 80)];
        Assert.Empty(service.ValidateConfiguration(fixture.Config, fixture.Source));
    }

    [Fact]
    public void TileTheme_AcceptsBrandThemesAndRejectsOthers()
    {
        using var fixture = new Fixture();
        foreach (var theme in new[] { "", "green", "raspberry", "eggplant", "navy", "charcoal" })
        {
            fixture.Config.Branding.TileTheme = theme;
            Assert.Empty(fixture.Service.ValidateConfiguration(fixture.Config, fixture.Source));
        }
        fixture.Config.Branding.TileTheme = "purple";
        Assert.NotEmpty(fixture.Service.ValidateConfiguration(fixture.Config, fixture.Source));
        Assert.Equal("green", TileThemes.Get("purple").Id);
        Assert.Equal(TileThemes.StableSeed("fis-data-lab"), TileThemes.StableSeed("fis-data-lab"));
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x43, 0xCE, 0xBD), TileThemes.LineColor("green", "#43CEBD"));
        Assert.NotEqual(TileThemes.LineColor("green", "#43CEBD"), TileThemes.LineColor("raspberry", "#43CEBD"));
    }

    [Fact]
    public void Navigation_StartsConfiguredEntryRatherThanFirstPage()
    {
        using var fixture = new Fixture();
        fixture.Config.Navigation.Sections[0].Pages.Insert(0, new NavigationPage { Id = "other", Title = "Other", Path = "other.html" });
        var plan = RuntimeNavigationPlan.Create(fixture.Config);
        Assert.Equal(1, plan.EntryIndex);
        Assert.Equal("index.html", plan.Pages[plan.EntryIndex].Path);
    }

    [Fact]
    public void Navigation_DisabledMenuStillStartsConfiguredEntry()
    {
        using var fixture = new Fixture();
        fixture.Config.Navigation.Enabled = false;
        fixture.Config.Navigation.Sections[0].Pages[0].Path = "other.html";
        var plan = RuntimeNavigationPlan.Create(fixture.Config);
        Assert.Single(plan.Pages);
        Assert.Equal("index.html", plan.Pages[plan.EntryIndex].Path);
    }

    [Fact]
    public void Navigation_EntryOutsideMenuGetsRuntimeHomeWithoutChangingConfig()
    {
        using var fixture = new Fixture();
        fixture.Config.Navigation.Sections[0].Pages[0].Path = "other.html";
        var plan = RuntimeNavigationPlan.Create(fixture.Config);
        Assert.Equal("index.html", plan.Pages[plan.EntryIndex].Path);
        Assert.Equal("other.html", fixture.Config.Navigation.Sections[0].Pages[0].Path);
    }

    [Fact]
    public async Task ArchiveEntryCountAndExpandedSize_AreEnforced()
    {
        using var fixture = new Fixture();
        await fixture.Service.BuildPackageAsync(fixture.Source, fixture.Package, fixture.Config);
        var smallCount = new PresentationService(new PackageLimits { MaxEntries = 1 }, fixture.Cache);
        await Assert.ThrowsAsync<InvalidDataException>(() => smallCount.ExtractAndValidateAsync(fixture.Package));
        var smallSize = new PresentationService(new PackageLimits { MaxExpandedBytes = 1 }, fixture.Cache);
        await Assert.ThrowsAsync<InvalidDataException>(() => smallSize.ExtractAndValidateAsync(fixture.Package));
        Assert.False(Directory.Exists(fixture.Cache));
    }

    [Fact]
    public async Task DuplicateManifestEntries_AreRejectedCleanly()
    {
        using var fixture = new Fixture();
        await fixture.Service.BuildPackageAsync(fixture.Source, fixture.Package, fixture.Config);
        using (var archive = ZipFile.Open(fixture.Package, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("manifest.json")!;
            PackageManifest manifest;
            using (var stream = entry.Open()) manifest = JsonSerializer.Deserialize<PackageManifest>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            manifest.Files.Add(manifest.Files[0]);
            entry.Delete();
            using var output = archive.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(output, manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.ExtractAndValidateAsync(fixture.Package));
    }

    [Fact]
    public async Task CancelDuringAssembly_PreservesOutputAndRemovesStagingArchive()
    {
        using var fixture = new Fixture();
        await fixture.Service.BuildPackageAsync(fixture.Source, fixture.Package, fixture.Config);
        var previousBytes = await File.ReadAllBytesAsync(fixture.Package);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(status => { if (status.Percent >= 45) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.BuildPackageAsync(
            fixture.Source, fixture.Package, fixture.Config, progress, cancellation.Token));
        Assert.Equal(previousBytes, await File.ReadAllBytesAsync(fixture.Package));
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, "*.building"));
    }

    [Fact]
    public async Task NestedConfigurationAssets_AreNotMistakenForRootMetadata()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Source, "assets"));
        File.WriteAllText(Path.Combine(fixture.Source, "assets", "presentation.json"), "{\"data\":true}");
        File.WriteAllText(Path.Combine(fixture.Source, "assets", "manifest.json"), "{\"asset\":true}");
        var manifest = await fixture.Service.BuildPackageAsync(fixture.Source, fixture.Package, fixture.Config);
        Assert.Contains(manifest.Files, file => file.Path == "assets/presentation.json");
        Assert.Contains(manifest.Files, file => file.Path == "assets/manifest.json");
        var result = await fixture.Service.ExtractAndValidateAsync(fixture.Package);
        Assert.True(File.Exists(Path.Combine(result.Directory, "assets", "presentation.json")));
    }

    [Fact]
    public async Task ArchiveSymbolicLinkEntry_IsRejected()
    {
        using var fixture = new Fixture();
        await fixture.Service.BuildPackageAsync(fixture.Source, fixture.Package, fixture.Config);
        using (var archive = ZipFile.Open(fixture.Package, ZipArchiveMode.Update))
        {
            var link = archive.CreateEntry("link.txt");
            link.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var writer = new StreamWriter(link.Open());
            writer.Write("../outside.txt");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.ExtractAndValidateAsync(fixture.Package));
    }

    [Fact]
    public async Task ConcurrentOpensAndSameVersionDifferentPackages_DoNotOverwriteRunningContent()
    {
        using var fixture = new Fixture();
        await fixture.Service.BuildPackageAsync(fixture.Source, fixture.Package, fixture.Config);
        var results = await Task.WhenAll(fixture.Service.ExtractAndValidateAsync(fixture.Package), fixture.Service.ExtractAndValidateAsync(fixture.Package));
        Assert.NotEqual(results[0].Directory, results[1].Directory);
        File.WriteAllText(Path.Combine(fixture.Source, "index.html"), "Changed content, same version");
        var otherPackage = Path.Combine(fixture.Root, "other.fisxpkg");
        await fixture.Service.BuildPackageAsync(fixture.Source, otherPackage, fixture.Config);
        var other = await fixture.Service.ExtractAndValidateAsync(otherPackage);
        Assert.NotEqual(Path.GetDirectoryName(results[0].Directory), Path.GetDirectoryName(other.Directory));
        Assert.Equal("Original content", File.ReadAllText(Path.Combine(results[0].Directory, "index.html")));
    }

    [Fact]
    public async Task Preflight_CriticalFailureBlocksDependentChecks()
    {
        var runner = new PreflightRunner();
        var dependentCalled = false;
        Assert.False(runner.CanLaunch);
        Assert.False(await runner.RunAsync("integrity", "Integrity", () => throw new InvalidDataException("Corrupt package")));
        Assert.False(await runner.RunAsync("browser", "Browser", () => { dependentCalled = true; return Task.CompletedTask; }));
        Assert.False(dependentCalled);
        Assert.False(runner.CanLaunch);
        Assert.Equal(["FAILED", "BLOCKED"], runner.Results.Select(result => result.Status));
    }

    [Fact]
    public async Task Preflight_PassingChecksAllowLaunchAndExportHasNoRawFailureText()
    {
        using var fixture = new Fixture();
        var runner = new PreflightRunner();
        Assert.True(await runner.RunAsync("package", "Package", () => Task.CompletedTask));
        Assert.True(runner.CanLaunch);
        var diagnostics = new RuntimeDiagnostics(Path.Combine(fixture.Root, "logs"));
        diagnostics.Record("Preflight", "Ready");
        Assert.True(File.Exists(diagnostics.LogPath));
        using var json = JsonDocument.Parse(diagnostics.Export(runner.Results));
        Assert.Single(json.RootElement.GetProperty("Preflight").EnumerateArray());
        Assert.Single(json.RootElement.GetProperty("Events").EnumerateArray());
    }

    private sealed class InlineProgress(Action<PackageBuildProgress> action) : IProgress<PackageBuildProgress>
    {
        public void Report(PackageBuildProgress value) => action(value);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fis-hardening-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "source");
        public string Cache => Path.Combine(Root, "cache");
        public string Package => Path.Combine(Root, "demo.fisxpkg");
        public PresentationService Service => new(cacheRoot: Cache);
        public PresentationConfig Config { get; } = new()
        {
            Id = "hardening-demo", Name = "Hardening demo",
            Navigation = new NavigationConfig
            {
                Sections = [new NavigationSection { Id = "main", Title = "Main", Pages = [new NavigationPage { Id = "home", Title = "Home", Path = "index.html" }] }]
            }
        };
        public Fixture() { Directory.CreateDirectory(Source); File.WriteAllText(Path.Combine(Source, "index.html"), "Original content"); }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}