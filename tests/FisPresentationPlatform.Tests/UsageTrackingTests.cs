using System.Text.Json;
using FisPresentationPlatform.Models;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Tests;

public sealed class UsageTrackingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fis-usage-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _originalLogFolder = AppLog.Folder;

    public UsageTrackingTests()
    {
        Directory.CreateDirectory(_root);
        AppLog.Folder = Path.Combine(_root, "logs");
    }

    public void Dispose()
    {
        AppLog.Folder = _originalLogFolder;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static PresentationConfig Demo() => new()
    {
        Id = "usage-demo", Name = "Usage demo", Version = "1.2.0",
        Catalog = new CatalogConfig { Organization = new OrganizationConfig { L3 = "Banking", L8 = "Digital One" } }
    };

    [Fact]
    public void UntrackedSession_WritesNothing()
    {
        var usage = new UsageTrackingService(Path.Combine(_root, "library"));
        Assert.Null(usage.StartSession(Demo(), new DemoLaunchContext { Track = false, ClientName = "Contoso" }));
        Assert.Equal(0, usage.GetStats().TrackedSessions);
    }

    [Fact]
    public async Task TrackedSession_RecordsDealDataPagesAndDuration_ThenUploadsToRepository()
    {
        var usage = new UsageTrackingService(Path.Combine(_root, "library"));
        var dataSet = new DemoDataSet { Id = "cio", Name = "Regional bank", Persona = "Chief Information Officer" };
        var session = usage.StartSession(Demo(), new DemoLaunchContext
        {
            Track = true, Reason = "Client demo", ClientName = "Contoso Bank", OpportunityId = "OID-12345", DataSet = dataSet
        })!;
        session.PageViewed("intro", "Introduction");
        session.PageViewed("value", "Value");

        var repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repository);
        Assert.Equal(0, await usage.UploadAsync(repository));

        session.End();
        var summary = Assert.Single(usage.LoadSessions());
        Assert.Equal("Contoso Bank", summary.ClientName);
        Assert.Equal("OID-12345", summary.OpportunityId);
        Assert.Equal("Chief Information Officer", summary.Persona);
        Assert.Equal(2, summary.Pages);
        Assert.NotNull(summary.DurationSeconds);
        Assert.False(summary.Uploaded);

        Assert.Equal(1, await usage.UploadAsync(repository));
        var uploaded = Assert.Single(Directory.GetFiles(Path.Combine(repository, "usage"), "*.jsonl", SearchOption.AllDirectories));
        var events = UsageTrackingService.ReadEvents(uploaded);
        Assert.Equal(["demo_session_start", "page_view", "page_view", "demo_session_end"], events.Select(e => e.Type));
        Assert.Equal("Banking", events[0].Organization!.L3);
        Assert.True(Assert.Single(usage.LoadSessions()).Uploaded);
        Assert.Equal(0, usage.GetStats().PendingFiles);
    }

    [Fact]
    public void AppLog_WritesAndReadsBackEntries()
    {
        AppLog.Info("Test", "first message");
        AppLog.Warn("Test", "line\nbreak");
        var entries = AppLog.Read(AppLog.FileFor(DateTimeOffset.Now));
        Assert.Contains(entries, entry => entry.Level == "INFO" && entry.Message == "first message");
        Assert.Contains(entries, entry => entry.Level == "WARN" && entry.Message == "line break");
    }
}

public sealed class DemoDataSetValidationTests : IDisposable
{
    private readonly string _source = Path.Combine(Path.GetTempPath(), "fis-datasets-" + Guid.NewGuid().ToString("N"));
    private readonly PresentationService _service = new();
    private readonly PresentationConfig _config;

    public DemoDataSetValidationTests()
    {
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "samples", "BasicPresentation"), _source);
        _config = _service.Deserialize(File.ReadAllText(Path.Combine(_source, "presentation.json")));
        Directory.CreateDirectory(Path.Combine(_source, "data"));
        File.WriteAllText(Path.Combine(_source, "data", "cio.json"), JsonSerializer.Serialize(new { bank = "Contoso" }));
    }

    public void Dispose() { try { Directory.Delete(_source, recursive: true); } catch (IOException) { } }

    [Fact]
    public void ValidDataSets_PassAndRoundTrip()
    {
        _config.DataSets = [new DemoDataSet { Id = "cio", Name = "Regional bank", File = "data/cio.json", Default = true }, new DemoDataSet { Id = "plain", Name = "No data" }];
        Assert.Empty(_service.ValidateConfiguration(_config, _source));
        var roundTrip = _service.Deserialize(_service.Serialize(_config));
        Assert.Equal(2, roundTrip.DataSets.Count);
        Assert.Equal("data/cio.json", roundTrip.DataSets[0].File);
    }

    [Theory]
    [InlineData("cio", "cio", "data/cio.json", "data/cio.json")]
    [InlineData("cio", "other", "../outside.json", "")]
    [InlineData("cio", "other", "data/missing.json", "")]
    [InlineData("cio", "other", "data/cio.txt", "")]
    public void InvalidDataSets_AreRejected(string firstId, string secondId, string firstFile, string secondFile)
    {
        _config.DataSets = [new DemoDataSet { Id = firstId, Name = "One", File = firstFile }, new DemoDataSet { Id = secondId, Name = "Two", File = secondFile }];
        Assert.NotEmpty(_service.ValidateConfiguration(_config, _source));
    }

    [Fact]
    public void MoreThanOneDefault_IsRejected()
    {
        _config.DataSets = [new DemoDataSet { Id = "a", Name = "A", Default = true }, new DemoDataSet { Id = "b", Name = "B", Default = true }];
        Assert.NotEmpty(_service.ValidateConfiguration(_config, _source));
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
