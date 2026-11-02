using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FisPresentationPlatform.Models;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Tests;

// These tests assert the blueprint contract, not just current implementation behavior.
// BlueprintGap tests retain their identities as regression checks for the reviewed defects.
public sealed class BlueprintAcceptanceTests
{
    private readonly PresentationService _service = new();

    [Fact, Trait("Category", "BlueprintGap")]
    public void RequiredConfigurationProperties_CannotBeReplacedByModelDefaults()
    {
        Assert.ThrowsAny<Exception>(() => _service.Deserialize("{}"));
    }

    [Fact, Trait("Category", "BlueprintGap")]
    public void NullConfigurationGroup_ProducesUsefulValidationRatherThanNullReference()
    {
        using var fixture = new Fixture();
        var document = JsonNode.Parse(_service.Serialize(fixture.Config))!;
        document["branding"] = null;
        List<string>? errors = null;
        var exception = Record.Exception(() =>
        {
            var config = _service.Deserialize(document.ToJsonString());
            errors = _service.ValidateConfiguration(config, fixture.Source);
        });
        Assert.False(exception is NullReferenceException, "Explicit null branding crashes validation instead of reporting a configuration error.");
        Assert.True(exception is JsonException or InvalidDataException || errors is { Count: > 0 },
            "Null branding must be rejected with a useful validation error.");
    }

    [Fact, Trait("Category", "BlueprintGap")]
    public void EnabledMenu_RequiresAtLeastOnePage()
    {
        using var fixture = new Fixture();
        fixture.Config.Navigation.Sections[0].Pages.Clear();
        Assert.NotEmpty(_service.ValidateConfiguration(fixture.Config, fixture.Source));
    }

    [Fact, Trait("Category", "BlueprintGap")]
    public void MenuPage_CannotPointToAnArbitraryNonPresentationFile()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Source, "notes.txt"), "Not a page");
        fixture.Config.Navigation.Sections[0].Pages[0].Path = "notes.txt";
        Assert.NotEmpty(_service.ValidateConfiguration(fixture.Config, fixture.Source));
    }

    [Fact, Trait("Category", "BlueprintGap")]
    public async Task PackageManifestIdentity_MustAgreeWithConfiguration()
    {
        using var fixture = new Fixture();
        await _service.BuildPackageAsync(fixture.Source, fixture.Package, fixture.Config);
        using (var archive = ZipFile.Open(fixture.Package, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("manifest.json")!;
            PackageManifest manifest;
            using (var stream = entry.Open())
                manifest = JsonSerializer.Deserialize<PackageManifest>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            manifest.PresentationId = fixture.Config.Id + "-mismatch";
            fixture.OtherCacheId = manifest.PresentationId;
            entry.Delete();
            using var output = archive.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(output, manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.ExtractAndValidateAsync(fixture.Package));
    }

    [Fact, Trait("Category", "Blueprint")]
    public void UnknownConfigurationProperties_AreRejected()
    {
        Assert.Throws<JsonException>(() => _service.Deserialize("{\"unexpected\":true}"));
    }

    [Fact, Trait("Category", "Blueprint")]
    public void UnsupportedSchemaVersion_BlocksValidation()
    {
        using var fixture = new Fixture();
        fixture.Config.SchemaVersion = "99.0";
        Assert.Contains(_service.ValidateConfiguration(fixture.Config, fixture.Source), error => error.Contains("schemaVersion"));
    }

    [Fact, Trait("Category", "Blueprint")]
    public async Task ExtraArchiveFiles_AreRejected()
    {
        using var fixture = new Fixture();
        await _service.BuildPackageAsync(fixture.Source, fixture.Package, fixture.Config);
        using (var archive = ZipFile.Open(fixture.Package, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(archive.CreateEntry("unexpected.txt").Open());
            writer.Write("Not in manifest");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.ExtractAndValidateAsync(fixture.Package));
    }

    [Fact, Trait("Category", "Blueprint")]
    public async Task ArchiveTraversalEntry_IsRejectedBeforeExtraction()
    {
        using var fixture = new Fixture();
        await _service.BuildPackageAsync(fixture.Source, fixture.Package, fixture.Config);
        using (var archive = ZipFile.Open(fixture.Package, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open());
            writer.Write("Must not be extracted");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.ExtractAndValidateAsync(fixture.Package));
    }

    [Fact, Trait("Category", "Blueprint")]
    public async Task CancelledBuild_PreservesPreviousOutput()
    {
        using var fixture = new Fixture();
        await _service.BuildPackageAsync(fixture.Source, fixture.Package, fixture.Config);
        var previous = await File.ReadAllBytesAsync(fixture.Package);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.BuildPackageAsync(
            fixture.Source, fixture.Package, fixture.Config, cancellationToken: cancellation.Token));
        Assert.Equal(previous, await File.ReadAllBytesAsync(fixture.Package));
    }

    [Fact, Trait("Category", "Blueprint")]
    public async Task Package_CanBeOpenedAfterOriginalSourceIsRemoved()
    {
        using var fixture = new Fixture();
        await _service.BuildPackageAsync(fixture.Source, fixture.Package, fixture.Config);
        Directory.Delete(fixture.Source, true);
        var result = await _service.ExtractAndValidateAsync(fixture.Package);
        Assert.True(File.Exists(Path.Combine(result.Directory, result.Config.EntryPoint)));
        fixture.Extracted = result.Directory;
    }

    [Fact, Trait("Category", "Blueprint")]
    public async Task RealSampleImageAndVideo_SurvivePackageAndExtractionByteForByte()
    {
        using var fixture = new Fixture();
        var sample = Path.Combine(AppContext.BaseDirectory, "samples", "BasicPresentation");
        var config = _service.Deserialize(await File.ReadAllTextAsync(Path.Combine(sample, "presentation.json")));
        config.Id = fixture.Config.Id;
        await _service.BuildPackageAsync(sample, fixture.Package, config);
        var result = await _service.ExtractAndValidateAsync(fixture.Package);
        fixture.Extracted = result.Directory;
        foreach (var relative in new[] { "media/money-lifecycle.svg", "media/money-lifecycle.mp4" })
        {
            var original = await File.ReadAllBytesAsync(Path.Combine(sample, relative));
            Assert.NotEmpty(original);
            Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(result.Directory, relative)));
        }
        // Byte preservation proves packaging, not video codec/rendering/interaction correctness.
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "fis-blueprint-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(_root, "content");
        public string Package => Path.Combine(_root, "demo.fisxpkg");
        public string? Extracted { get; set; }
        public string? OtherCacheId { get; set; }
        public PresentationConfig Config { get; } = new()
        {
            Id = "blueprint-" + Guid.NewGuid().ToString("N"),
            Name = "Blueprint acceptance demo",
            Navigation = new NavigationConfig
            {
                Sections = [new NavigationSection
                {
                    Id = "main", Title = "Main",
                    Pages = [new NavigationPage { Id = "home", Title = "Home", Path = "index.html" }]
                }]
            }
        };

        public Fixture()
        {
            Directory.CreateDirectory(Source);
            File.WriteAllText(Path.Combine(Source, "index.html"), "<!doctype html><html><body>Blueprint demo</body></html>");
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FIS", "PresentationRuntime", "Cache", Config.Id);
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
            if (Extracted is not null && Directory.Exists(Extracted)) Directory.Delete(Extracted, true);
            if (OtherCacheId is not null)
            {
                var otherCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FIS", "PresentationRuntime", "Cache", OtherCacheId);
                if (Directory.Exists(otherCache)) Directory.Delete(otherCache, true);
            }
        }
    }
}