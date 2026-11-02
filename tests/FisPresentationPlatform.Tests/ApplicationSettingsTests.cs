using System.IO.Compression;
using System.Net;
using System.Net.Http;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Tests;

public sealed class ApplicationSettingsTests
{
    [Fact]
    public async Task SettingsPersistPathsAndPollingAndBackupsKeepBoundedSnapshots()
    {
        var root = Path.Combine(Path.GetTempPath(), "fis-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new ApplicationSettings { LibraryLocation = Path.Combine(root, "library"), CacheLocation = Path.Combine(root, "cache"), BackupLocation = Path.Combine(root, "backups"), BackupsToKeep = 2, CheckOnLaunch = false, CheckIntervalMinutes = 30 };
            var service = new ApplicationSettingsService(Path.Combine(root, "settings"));
            await service.SaveAsync(settings);
            var loaded = await service.LoadAsync();
            Assert.Equal(settings.CacheLocation, loaded.CacheLocation);
            Assert.False(loaded.CheckOnLaunch);
            File.WriteAllText(Path.Combine(settings.LibraryLocation, "library.json"), "[]");
            for (var index = 0; index < 3; index++) ApplicationSettingsService.CreateBackup(settings);
            var snapshots = Directory.GetFiles(settings.BackupLocation, "*.zip");
            Assert.Equal(2, snapshots.Length);
            using var archive = ZipFile.OpenRead(snapshots[0]);
            Assert.NotNull(archive.GetEntry("library.json"));
        }
        finally
        {
            await new ApplicationSettingsService().LoadAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FirstRunUsesDeploymentDefaultsUntilUserSavesSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "fis-deploy-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var deployment = Path.Combine(root, "deployment.json");
            File.WriteAllText(deployment, "{ // set by the admin\n \"demoRepositoryPath\": \"\\\\\\\\fileserver\\\\FISDemos\", \"trackingEnabledByDefault\": false, }");
            var service = new ApplicationSettingsService(Path.Combine(root, "settings"), deployment);

            var firstRun = await service.LoadAsync();
            Assert.Equal(@"\\fileserver\FISDemos", firstRun.DemoRepositoryPath);
            Assert.False(firstRun.TrackingEnabledByDefault);
            Assert.Equal(new ApplicationSettings().LibraryLocation, firstRun.LibraryLocation);

            firstRun.DemoRepositoryPath = Path.Combine(root, "my-repo");
            firstRun.LibraryLocation = Path.Combine(root, "library"); firstRun.CacheLocation = Path.Combine(root, "cache"); firstRun.BackupLocation = Path.Combine(root, "backups");
            await service.SaveAsync(firstRun);
            Assert.Equal(Path.Combine(root, "my-repo"), (await service.LoadAsync()).DemoRepositoryPath);
        }
        finally
        {
            await new ApplicationSettingsService().LoadAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InvalidDeploymentDefaultsAreRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "fis-deploy-bad-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var deployment = Path.Combine(root, "deployment.json");
            File.WriteAllText(deployment, "{ \"demoRepositoryPath\": \"relative\\\\path\" }");
            await Assert.ThrowsAsync<InvalidDataException>(() => new ApplicationSettingsService(Path.Combine(root, "settings"), deployment).LoadAsync());
        }
        finally
        {
            await new ApplicationSettingsService().LoadAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SettingsRejectNestedFoldersAndUnsafeRemoteAddresses()
    {
        var settings = new ApplicationSettings { BackupLocation = Path.Combine(new ApplicationSettings().LibraryLocation, "backups") };
        Assert.Throws<InvalidDataException>(() => ApplicationSettingsService.Validate(settings));
        settings = new ApplicationSettings { RepositoryUrl = "http://example.test/feed" };
        Assert.Throws<InvalidDataException>(() => ApplicationSettingsService.Validate(settings));
    }

    [Fact]
    public async Task DisabledLaunchCheckUsesCacheWithoutHttpRequest()
    {
        var root = Path.Combine(Path.GetTempPath(), "fis-cache-only-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new CountingHandler();
            using var service = new WelcomeFeedService(root, handler);
            await service.RefreshAsync("https://example.test/feed");
            var cached = await service.RefreshAsync("https://example.test/feed", checkRemote: false);
            Assert.Equal(1, handler.Calls);
            Assert.Equal("Cached", cached.Source);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"schemaVersion\":\"1.0\",\"items\":[]}") });
        }
    }
}