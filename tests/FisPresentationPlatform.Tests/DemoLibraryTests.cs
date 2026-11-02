using FisPresentationPlatform.Models;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Tests;

public sealed class DemoLibraryTests
{
    [Fact]
    public async Task Import_PersistsVerifiedPackageAndDeduplicatesIdenticalBytes()
    {
        using var fixture = new Fixture();
        await fixture.BuildAsync();
        var service = new DemoLibraryService(fixture.Library);
        var record = await service.ImportAsync(fixture.Package);
        await service.ImportAsync(fixture.Package);
        Assert.True(File.Exists(record.PackagePath));
        Assert.Single(await service.LoadAsync());
        await service.UpdateAsync(record.Hash, favorite: true, launched: true);
        var saved = Assert.Single(await new DemoLibraryService(fixture.Library).LoadAsync());
        Assert.True(saved.Favorite);
        Assert.NotNull(saved.LastLaunchedUtc);
        Assert.Equal("Payments", saved.Product);
        File.Delete(saved.PackagePath);
        Assert.False(Assert.Single(await service.LoadAsync()).Available);
        await service.ImportAsync(fixture.Package);
        Assert.True(Assert.Single(await service.LoadAsync()).Available);
    }

    [Fact]
    public async Task InvalidPackage_DoesNotCreateLibraryEntry()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Package, "Not a zip package");
        var service = new DemoLibraryService(fixture.Library);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(fixture.Package));
        Assert.Empty(await service.LoadAsync());
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.Library, "staging"), "*.fisxpkg"));
    }

    [Fact]
    public void Filters_CombineSearchProductAndFavoritesWithoutInventingClassification()
    {
        var payments = new LibraryDemo { Favorite = true, Config = new PresentationConfig { Name = "Fraud demo", Catalog = new CatalogConfig { Product = "Payments", Functions = ["Fraud detection"], Tags = ["Treasury"] } } };
        var banking = new LibraryDemo { Config = new PresentationConfig { Name = "Banking demo" } };
        Assert.Equal("Unclassified", banking.Product);
        Assert.Single(DemoLibraryService.Filter([payments, banking], "fraud treasury", "Payments", favorites: true));
        Assert.Empty(DemoLibraryService.Filter([payments, banking], "missing", null));
        Assert.Empty(DemoLibraryService.Filter([payments, banking], null, null, recent: true));
    }

    [Fact]
    public async Task EditableCopy_ExtractsSourceKeepsExistingCopyAndRebuildReplacesInstalledVersion()
    {
        using var fixture = new Fixture();
        await fixture.BuildAsync();
        var service = new DemoLibraryService(fixture.Library);
        var original = await service.ImportAsync(fixture.Package);

        var workspace = await service.CreateEditableCopyAsync(original);
        Assert.Equal(service.WorkspacePath("library-test"), workspace);
        Assert.True(File.Exists(Path.Combine(workspace, "index.html")));
        Assert.True(File.Exists(Path.Combine(workspace, PresentationService.ConfigurationFileName)));
        Assert.False(File.Exists(Path.Combine(workspace, "manifest.json")));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(fixture.Library, "staging"), "edit-*"));

        File.WriteAllText(Path.Combine(workspace, "notes.txt"), "unbuilt change");
        Assert.Equal(workspace, await service.CreateEditableCopyAsync(original));
        Assert.True(File.Exists(Path.Combine(workspace, "notes.txt")));

        var presentation = new PresentationService();
        var loaded = await presentation.LoadOrCreateConfigAsync(workspace);
        loaded.Config.Name = "Library test (edited)";
        var rebuilt = Path.Combine(workspace, "..", "rebuilt.fisxpkg");
        await presentation.BuildPackageAsync(workspace, rebuilt, loaded.Config);
        var edited = await service.ImportAsync(rebuilt);
        await service.RetireOtherVersionsAsync(edited.Config.Id, edited.Hash);
        var only = Assert.Single(await service.LoadAsync());
        Assert.Equal("Library test (edited)", only.Name);
        Assert.False(File.Exists(original.PackagePath));

        File.WriteAllText(Path.Combine(workspace, "scratch.txt"), "unbuilt change");
        await service.CreateEditableCopyAsync(only, replaceExisting: true);
        Assert.False(File.Exists(Path.Combine(workspace, "scratch.txt")));
        Assert.True(File.Exists(Path.Combine(workspace, "notes.txt")));
    }

    [Fact]
    public async Task Uninstall_RemovesAllVersionsFilesAndWorkingCopy()
    {
        using var fixture = new Fixture();
        await fixture.BuildAsync();
        var service = new DemoLibraryService(fixture.Library);
        var record = await service.ImportAsync(fixture.Package);
        var workspace = await service.CreateEditableCopyAsync(record);

        Assert.Equal(1, await service.UninstallAsync("LIBRARY-TEST"));
        Assert.Empty(await service.LoadAsync());
        Assert.False(File.Exists(record.PackagePath));
        Assert.False(Directory.Exists(workspace));
        Assert.Equal(0, await service.UninstallAsync("library-test"));
        Assert.Throws<InvalidDataException>(() => service.WorkspacePath("..\\escape"));
    }

    [Fact]
    public async Task SaveAsNewDemo_CreatesSeparatePackageWorkspaceAndLibraryEntry()
    {
        using var fixture = new Fixture();
        await fixture.BuildAsync();
        var service = new DemoLibraryService(fixture.Library);
        var original = await service.ImportAsync(fixture.Package);
        var workspace = await service.CreateEditableCopyAsync(original);
        File.WriteAllText(Path.Combine(workspace, "notes.txt"), "kept in the copy");
        var presentation = new PresentationService();
        var config = (await presentation.LoadOrCreateConfigAsync(workspace)).Config;
        config.Id = "library-test-copy";
        config.Name = "Library test copy";
        config.Version = "1.0.0";
        var package = Path.Combine(fixture.Library, "..", "copy.fisxpkg");

        var (created, newWorkspace) = await service.SaveAsNewDemoAsync(workspace, config, package);

        Assert.Equal(service.WorkspacePath("library-test-copy"), newWorkspace);
        Assert.True(File.Exists(package));
        Assert.True(File.Exists(Path.Combine(newWorkspace, "notes.txt")));
        Assert.Equal("library-test-copy", (await presentation.LoadOrCreateConfigAsync(newWorkspace)).Config.Id);
        Assert.Equal("library-test", (await presentation.LoadOrCreateConfigAsync(workspace)).Config.Id);
        var demos = await service.LoadAsync();
        Assert.Equal(2, demos.Count);
        Assert.Contains(demos, demo => demo.Hash == created.Hash && demo.Name == "Library test copy");
        Assert.True(File.Exists(original.PackagePath));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsNewDemoAsync(workspace, config, package));
        Assert.Equal("fis-trade-demo-copy", DemoLibraryService.SuggestId("Trade demo  copy!", "fis-trade"));
        Assert.Equal("my-demo", DemoLibraryService.SuggestId("My Demo", "other"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "fis-library-" + Guid.NewGuid().ToString("N"));
        public string Library => Path.Combine(_root, "library");
        public string Package => Path.Combine(_root, "demo.fisxpkg");
        public Fixture() => Directory.CreateDirectory(_root);
        public async Task BuildAsync()
        {
            var source = Path.Combine(_root, "source"); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "index.html"), "<html>Local demo</html>");
            var config = new PresentationConfig { Id = "library-test", Name = "Library test", Catalog = new CatalogConfig { Product = "Payments", Tags = ["Local"] }, Navigation = new NavigationConfig { Enabled = false } };
            await new PresentationService().BuildPackageAsync(source, Package, config);
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}