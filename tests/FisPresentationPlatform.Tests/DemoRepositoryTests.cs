using FisPresentationPlatform.Models;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Tests;

public sealed class DemoRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fis-repo-" + Guid.NewGuid().ToString("N"));
    private string Repository => Path.Combine(_root, "repository");
    private string Library => Path.Combine(_root, "library");
    private string Cache => Path.Combine(Library, "repository-cache");

    public DemoRepositoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task BundledDemos_PublishWithOrganizationTagsAndUpdateReplacesOldVersion()
    {
        foreach (var folder in DemoRepositoryPublisher.BundledDemoFolders)
            await DemoRepositoryPublisher.PublishAsync(Repository, Path.Combine(DemoRepositoryPublisher.BundledDemosRoot, folder), "1.0.0");

        var service = new DemoRepositoryService(Repository, Cache);
        var snapshot = await service.LoadIndexAsync();
        Assert.False(snapshot.FromCache);
        Assert.Equal(DemoRepositoryPublisher.BundledDemoFolders.Length, snapshot.Index.Demos.Count);
        Assert.Equal(["Banking", "Capital Markets", "Payments"], snapshot.Index.Demos.Select(demo => demo.Organization.L3).Distinct().Order());
        Assert.All(snapshot.Index.Demos, demo =>
        {
            Assert.False(string.IsNullOrWhiteSpace(demo.Organization.L8));
            Assert.Contains(demo.Organization.L3, OrganizationConfig.BusinessLines);
            Assert.True(File.Exists(Path.Combine(Repository, demo.Latest!.Package)));
        });

        var library = new DemoLibraryService(Library);
        var advantage = snapshot.Index.Demos.Single(demo => demo.Id == "fis-digital-advantage");
        Assert.All(DemoRepositoryService.Compare(snapshot.Index, await library.LoadAsync()), item => Assert.Equal(DemoAvailabilityState.NotInstalled, item.State));

        var progress = new List<int>();
        var installed = await service.DownloadAsync(advantage, advantage.Latest!, library, new SyncProgress(progress.Add));
        Assert.Equal(100, progress[^1]);
        Assert.Equal("Banking", installed.Business);
        Assert.Equal("Digital Account Origination", installed.Solution);
        await library.UpdateAsync(installed.Hash, favorite: true);
        Assert.Equal(DemoAvailabilityState.UpToDate, Availability(snapshot.Index, await library.LoadAsync(), advantage.Id));

        await DemoRepositoryPublisher.PublishAsync(Repository, Path.Combine(DemoRepositoryPublisher.BundledDemosRoot, "DigitalAdvantage"), "1.1.0", "Update");
        snapshot = await service.LoadIndexAsync();
        advantage = snapshot.Index.Demos.Single(demo => demo.Id == "fis-digital-advantage");
        Assert.Equal("1.1.0", advantage.Latest!.Version);
        Assert.Equal(2, advantage.Versions.Count);
        Assert.Equal(DemoAvailabilityState.UpdateAvailable, Availability(snapshot.Index, await library.LoadAsync(), advantage.Id));

        await service.DownloadAsync(advantage, advantage.Latest!, library);
        var record = Assert.Single(await library.LoadAsync());
        Assert.Equal("1.1.0", record.Config.Version);
        Assert.True(record.Favorite);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(Library, "packages")));
        Assert.Equal(DemoAvailabilityState.UpToDate, Availability(snapshot.Index, await library.LoadAsync(), advantage.Id));
    }

    [Fact]
    public async Task Release_RefusesExistingVersionUnlessForced()
    {
        var folder = Path.Combine(DemoRepositoryPublisher.BundledDemosRoot, "DesignedForDigital");
        var first = await DemoRepositoryPublisher.PublishAsync(Repository, folder, "1.0.0", "First");
        var packagePath = Path.Combine(Repository, first.Package);
        var originalBytes = await File.ReadAllBytesAsync(packagePath);

        var refused = await Assert.ThrowsAsync<DemoRepositoryPublisher.VersionAlreadyPublishedException>(() => DemoRepositoryPublisher.PublishAsync(Repository, folder, "1.0.0", "Again"));
        Assert.Equal("1.0.0", refused.Version);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(packagePath));
        var entry = Assert.Single((await new DemoRepositoryService(Repository, Cache).LoadIndexAsync()).Index.Demos);
        Assert.Equal("First", Assert.Single(entry.Versions).ReleaseNotes);

        var forced = await DemoRepositoryPublisher.PublishAsync(Repository, folder, "1.0.0", "Forced", force: true);
        entry = Assert.Single((await new DemoRepositoryService(Repository, Cache).LoadIndexAsync()).Index.Demos);
        Assert.Equal("Forced", Assert.Single(entry.Versions).ReleaseNotes);
        Assert.Equal(forced.Sha256, entry.Latest!.Sha256);
    }

    [Fact]
    public async Task Retire_RemovesOneVersionThenTheWholeDemo()
    {
        var folder = Path.Combine(DemoRepositoryPublisher.BundledDemosRoot, "DigitalAdvantage");
        var v1 = await DemoRepositoryPublisher.PublishAsync(Repository, folder, "1.0.0");
        var v2 = await DemoRepositoryPublisher.PublishAsync(Repository, folder, "1.1.0");
        var service = new DemoRepositoryService(Repository, Cache);

        Assert.Equal(["1.1.0"], await DemoRepositoryPublisher.RetireAsync(Repository, "fis-digital-advantage", "1.1.0"));
        var demo = Assert.Single((await service.LoadIndexAsync()).Index.Demos);
        Assert.Equal("1.0.0", demo.Latest!.Version);
        Assert.False(File.Exists(Path.Combine(Repository, v2.Package)));
        Assert.True(File.Exists(Path.Combine(Repository, v1.Package)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => DemoRepositoryPublisher.RetireAsync(Repository, "fis-digital-advantage", "9.9.9"));
        Assert.Equal(["1.0.0"], await DemoRepositoryPublisher.RetireAsync(Repository, "fis-digital-advantage"));
        Assert.Empty((await service.LoadIndexAsync()).Index.Demos);
        Assert.False(Directory.Exists(Path.Combine(Repository, "packages", "fis-digital-advantage")));

        // A retired version can be released again.
        await DemoRepositoryPublisher.PublishAsync(Repository, folder, "1.0.0");
        Assert.Single((await service.LoadIndexAsync()).Index.Demos);
    }

    [Fact]
    public async Task TamperedPackage_IsRejectedAndNothingIsInstalled()
    {
        await DemoRepositoryPublisher.PublishAsync(Repository, Path.Combine(DemoRepositoryPublisher.BundledDemosRoot, "ConfidentDelivery"), "1.0.0");
        var service = new DemoRepositoryService(Repository, Cache);
        var demo = Assert.Single((await service.LoadIndexAsync()).Index.Demos);
        await File.AppendAllTextAsync(Path.Combine(Repository, demo.Latest!.Package), "tampered");
        var library = new DemoLibraryService(Library);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(demo, demo.Latest!, library));
        Assert.Empty(await library.LoadAsync());
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(Cache, "downloads")));
    }

    [Fact]
    public async Task UnsafePackagePath_IsRejected()
    {
        var service = new DemoRepositoryService(Repository, Cache);
        var demo = new RepositoryDemo { Id = "x", Name = "X" };
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(demo, new RepositoryVersion { Version = "1.0.0", Package = "../outside.fisxpkg" }, new DemoLibraryService(Library)));
    }

    [Fact]
    public async Task UnreachableRepository_FallsBackToCachedIndexAndAssignments()
    {
        Directory.CreateDirectory(Repository);
        await DemoRepositoryPublisher.PublishAsync(Repository, Path.Combine(DemoRepositoryPublisher.BundledDemosRoot, "DesignedForDigital"), "1.0.0");
        await DemoRepositoryPublisher.WriteAssignmentsAsync(Repository, new AssignmentFile { Assignments = [new UserAssignment { User = "CORP\\alex", Solutions = ["Instant Payments Hub"] }] });
        var service = new DemoRepositoryService(Repository, Cache);
        Assert.False((await service.LoadIndexAsync()).FromCache);
        Assert.False((await service.LoadAssignmentsAsync("CORP\\alex")).FromCache);

        Directory.Delete(Repository, true);
        var snapshot = await service.LoadIndexAsync();
        Assert.True(snapshot.FromCache);
        Assert.Single(snapshot.Index.Demos);
        var (solutions, fromCache) = await service.LoadAssignmentsAsync("CORP\\alex");
        Assert.True(fromCache);
        Assert.Equal(["Instant Payments Hub"], solutions);

        await Assert.ThrowsAnyAsync<IOException>(() => new DemoRepositoryService(Repository, Path.Combine(_root, "empty-cache")).LoadIndexAsync());
    }

    [Fact]
    public void Assignments_MatchDomainQualifiedOrBareUserNames()
    {
        var file = new AssignmentFile
        {
            Assignments =
            [
                new UserAssignment { User = "CORP\\alex", Solutions = ["Instant Payments Hub"] },
                new UserAssignment { User = "alex", Solutions = ["Digital Account Origination", "instant payments hub"] },
                new UserAssignment { User = "OTHER\\alex", Solutions = ["Trade Lifecycle Manager"] }
            ]
        };
        Assert.Equal(["Instant Payments Hub", "Digital Account Origination"], DemoRepositoryService.SolutionsFor(file, "corp\\ALEX"));
        Assert.Equal(["Digital Account Origination", "instant payments hub"], DemoRepositoryService.SolutionsFor(file, "alex"));
        Assert.Empty(DemoRepositoryService.SolutionsFor(file, "CORP\\sam"));
    }

    [Theory]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("2.0.0", "10.0.0", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.0-beta", 1)]
    public void SemanticVersions_CompareNumerically(string left, string right, int expected) =>
        Assert.Equal(expected, Math.Sign(SemanticVersion.Compare(left, right)));

    [Fact]
    public void Filter_ByBusinessAndAssignedSolutions()
    {
        static LibraryDemo Demo(string l3, string l8) => new() { Config = new PresentationConfig { Name = l8, Catalog = new CatalogConfig { Organization = new OrganizationConfig { L3 = l3, L8 = l8 } } } };
        var demos = new[] { Demo("Payments", "Instant Payments Hub"), Demo("Banking", "Digital Account Origination"), Demo("Capital Markets", "Trade Lifecycle Manager") };
        Assert.Single(DemoLibraryService.Filter(demos, null, null, business: "Banking"));
        Assert.Equal(3, DemoLibraryService.Filter(demos, null, null, business: DemoLibraryService.AllBusinesses).Count);
        Assert.Equal(2, DemoLibraryService.Filter(demos, null, null, solutions: ["instant payments hub", "Digital Account Origination"]).Count);
        Assert.Empty(DemoLibraryService.Filter(demos, null, null, business: "Payments", solutions: ["Trade Lifecycle Manager"]));
        Assert.Single(DemoLibraryService.Filter(demos, "trade", null));
    }

    [Fact]
    public void Settings_RejectRelativeOrNestedRepositoryPath()
    {
        ApplicationSettings Settings(string repository) => new() { LibraryLocation = Path.Combine(_root, "lib"), CacheLocation = Path.Combine(_root, "cache"), BackupLocation = Path.Combine(_root, "backups"), DemoRepositoryPath = repository };
        ApplicationSettingsService.Validate(Settings(Path.Combine(_root, "repo")));
        ApplicationSettingsService.Validate(Settings(""));
        Assert.Throws<InvalidDataException>(() => ApplicationSettingsService.Validate(Settings("repo")));
        Assert.Throws<InvalidDataException>(() => ApplicationSettingsService.Validate(Settings(Path.Combine(_root, "lib", "repo"))));
        Assert.Throws<InvalidDataException>(() => ApplicationSettingsService.Validate(Settings(_root)));
    }

    private static DemoAvailabilityState Availability(RepositoryIndex index, IEnumerable<LibraryDemo> library, string id) =>
        DemoRepositoryService.Compare(index, library).Single(item => item.Demo.Id == id).State;

    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
