using System.Text.Json.Nodes;
using FisPresentationPlatform.Models;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Tests;

public sealed class DataSetResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fis-resolver-" + Guid.NewGuid().ToString("N"));

    public DataSetResolverTests() => Directory.CreateDirectory(Path.Combine(_root, "data"));

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch (IOException) { } }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void Extends_DeepMergesObjects_ReplacesArrays_AndNullRemovesKeys()
    {
        Write("data/base.json", """{ "client": { "name": "Base", "region": "NA" }, "kpis": { "a": 1, "b": 2 }, "trades": [1, 2, 3], "legacy": true }""");
        Write("data/child.json", """{ "$extends": "data/base.json", "client": { "name": "Child" }, "kpis": { "b": 20 }, "trades": [9], "legacy": null }""");

        var resolved = DataSetResolver.Resolve(_root, "data/child.json");

        Assert.True(resolved.Ok, string.Join(" ", resolved.Errors));
        Assert.Equal(["data/child.json", "data/base.json"], resolved.Chain);
        var data = resolved.Data!;
        Assert.Equal("Child", (string?)data["client"]!["name"]);
        Assert.Equal("NA", (string?)data["client"]!["region"]);
        Assert.Equal(1, (int)data["kpis"]!["a"]!);
        Assert.Equal(20, (int)data["kpis"]!["b"]!);
        Assert.Single(data["trades"]!.AsArray());
        Assert.False(data.ContainsKey("legacy"));
        Assert.False(data.ContainsKey(DataSetLimits.ExtendsKey));
        Assert.True(resolved.Bytes > 0);
    }

    [Fact]
    public void Extends_Loop_IsReported()
    {
        Write("data/a.json", """{ "$extends": "data/b.json" }""");
        Write("data/b.json", """{ "$extends": "data/a.json" }""");
        Assert.Contains(DataSetResolver.Resolve(_root, "data/a.json").Errors, error => error.Contains("loop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Extends_TooDeep_IsReported()
    {
        Write("data/l0.json", """{ "v": 0 }""");
        for (var level = 1; level <= DataSetLimits.MaxExtendsDepth; level++)
            Write($"data/l{level}.json", $$"""{ "$extends": "data/l{{level - 1}}.json", "v": {{level}} }""");
        // MaxExtendsDepth counts files in the chain: l3 → l2 → l1 → l0 is allowed, l4 adds a fifth file.
        Assert.False(DataSetResolver.Resolve(_root, $"data/l{DataSetLimits.MaxExtendsDepth}.json").Ok);
        Assert.True(DataSetResolver.Resolve(_root, $"data/l{DataSetLimits.MaxExtendsDepth - 1}.json").Ok);
    }

    [Theory]
    [InlineData("""{ "$files": { "history": "data/history.csv" } }""", true)]
    [InlineData("""{ "$files": { "history": "data/missing.csv" } }""", false)]
    [InlineData("""{ "$files": { "history": "../outside.csv" } }""", false)]
    [InlineData("""{ "$files": { "history": "data/history.exe" } }""", false)]
    [InlineData("""{ "$files": ["data/history.csv"] }""", false)]
    public void Files_AreValidated(string json, bool ok)
    {
        Write("data/history.csv", "id,value\n1,2\n");
        Write("data/history.exe", "x");
        Write("data/set.json", json);
        var resolved = DataSetResolver.Resolve(_root, "data/set.json");
        Assert.Equal(ok, resolved.Ok);
        if (ok) Assert.Equal("data/history.csv", resolved.Files["history"]);
    }

    [Fact]
    public void Files_AreInheritedThroughExtends()
    {
        Write("data/history.csv", "id\n1\n");
        Write("data/base.json", """{ "$files": { "history": "data/history.csv" } }""");
        Write("data/child.json", """{ "$extends": "data/base.json", "name": "c" }""");
        Assert.Equal("data/history.csv", DataSetResolver.Resolve(_root, "data/child.json").Files["history"]);
    }

    [Fact]
    public void InlineData_OverOneMegabyte_IsRejected()
    {
        Write("data/big.json", $$"""{ "blob": "{{new string('x', (int)DataSetLimits.MaxInlineBytes)}}" }""");
        Assert.False(DataSetResolver.Resolve(_root, "data/big.json").Ok);
    }

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("{ not json")]
    public void NonObjectOrInvalidJson_IsRejected(string json)
    {
        Write("data/bad.json", json);
        Assert.False(DataSetResolver.Resolve(_root, "data/bad.json").Ok);
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreAllowed()
    {
        Write("data/c.json", "{\n // note\n \"a\": 1,\n}");
        Assert.True(DataSetResolver.Resolve(_root, "data/c.json").Ok);
    }
}

public sealed class DataSetServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fis-datasets-svc-" + Guid.NewGuid().ToString("N"));
    private readonly PresentationConfig _config = new() { Id = "demo", Name = "Demo", EntryPoint = "index.html" };

    public DataSetServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data", "shared"));
        File.WriteAllText(Path.Combine(_root, "data", "shared", "history.csv"), "id,value\n1,2\n");
        File.WriteAllText(Path.Combine(_root, "data", "base.json"),
            """{ "client": { "name": "Base bank" }, "trades": [ { "id": "T1", "notional": 10 } ], "$files": { "history": "data/shared/history.csv" } }""");
        _config.DataSets.Add(new DemoDataSet { Id = "base", Name = "Base", Persona = "COO", File = "data/base.json", Default = true });
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch (IOException) { } }

    [Fact]
    public void Create_Override_WritesSmallExtendsFile_AndInheritsData()
    {
        var created = DataSetService.Create(_root, _config, "base", "emea", "EMEA bank", "Head of Ops", DataSetCreateMode.Override);

        Assert.Equal("data/emea.json", created.File);
        Assert.False(created.Default);
        Assert.Equal(2, _config.DataSets.Count);
        var raw = DataSetService.Parse(DataSetService.ReadText(_root, created.File));
        Assert.Equal("data/base.json", (string?)raw[DataSetLimits.ExtendsKey]);
        var resolved = DataSetResolver.Resolve(_root, created.File);
        Assert.True(resolved.Ok);
        Assert.Equal("Base bank", (string?)resolved.Data!["client"]!["name"]);
        Assert.Equal("Head of Ops", (string?)resolved.Data["persona"]);
        Assert.Equal(["EMEA bank"], DataSetService.DependantsOf(_root, _config, _config.DataSets[0]));
    }

    [Fact]
    public void Create_Duplicate_CopiesJsonAndPrivateFiles()
    {
        var created = DataSetService.Create(_root, _config, "base", "copy", "Copy", "COO", DataSetCreateMode.Duplicate);

        var resolved = DataSetResolver.Resolve(_root, created.File);
        Assert.True(resolved.Ok, string.Join(" ", resolved.Errors));
        Assert.Single(resolved.Chain);
        Assert.StartsWith("data/copy/", resolved.Files["history"]);
        Assert.True(File.Exists(Path.Combine(_root, resolved.Files["history"])));
        Assert.Equal("T1", (string?)resolved.Data!["trades"]![0]!["id"]);
    }

    [Fact]
    public void Create_Blank_AndFirstSetBecomesDefault()
    {
        var config = new PresentationConfig { Id = "x", Name = "X", EntryPoint = "index.html" };
        var created = DataSetService.Create(_root, config, null, "blank", "Blank", "CFO", DataSetCreateMode.Blank);
        Assert.True(created.Default);
        Assert.Equal("CFO", (string?)DataSetService.Parse(DataSetService.ReadText(_root, created.File))["persona"]);
    }

    [Fact]
    public void Create_DuplicateId_Throws()
        => Assert.ThrowsAny<Exception>(() => DataSetService.Create(_root, _config, "base", "BASE", "Again", "", DataSetCreateMode.Blank));

    [Fact]
    public void Remove_IsBlockedByDependants_ThenReassignsDefaultAndDeletesFile()
    {
        var child = DataSetService.Create(_root, _config, "base", "child", "Child", "", DataSetCreateMode.Override);
        Assert.Null(DataSetService.Parse(DataSetService.ReadText(_root, child.File))["persona"]);
        Assert.Throws<InvalidOperationException>(() => DataSetService.Remove(_root, _config, "base"));

        DataSetService.Remove(_root, _config, "child");
        Assert.False(File.Exists(Path.Combine(_root, child.File)));

        var other = DataSetService.Create(_root, _config, null, "other", "Other", "", DataSetCreateMode.Blank);
        var deleted = DataSetService.Remove(_root, _config, "base");
        Assert.Equal(["data/base.json"], deleted);
        Assert.True(other.Default);
    }

    [Theory]
    [InlineData("""{ "$extends": "data/base.json" }""")]
    [InlineData("""{ "$files": {} }""")]
    [InlineData("[1]")]
    public void Import_RejectsReservedKeysAndNonObjects(string json)
    {
        var external = Path.Combine(_root, "external.json");
        File.WriteAllText(external, json);
        Assert.Throws<InvalidDataException>(() => DataSetService.Import(_root, _config, external, "imp", "Imported", ""));
    }

    [Fact]
    public void Import_CopiesIntoDataFolder()
    {
        var external = Path.Combine(_root, "external.json");
        File.WriteAllText(external, """{ "client": { "name": "Imported" } }""");
        var created = DataSetService.Import(_root, _config, external, "imp", "Imported", "");
        Assert.StartsWith("data/", created.File);
        Assert.True(DataSetResolver.Resolve(_root, created.File).Ok);
    }

    [Fact]
    public void SaveText_KeepsFormatting_AndRejectsInvalidJson()
    {
        const string text = "{\n  \"rows\": [\n    {\"id\":1},{\"id\":2}\n  ]\n}";
        DataSetService.SaveText(_root, "data/base.json", text);
        Assert.Equal(text + "\n", File.ReadAllText(Path.Combine(_root, "data", "base.json")));
        Assert.Throws<InvalidDataException>(() => DataSetService.SaveText(_root, "data/base.json", "{ broken"));
        Assert.Throws<InvalidDataException>(() => DataSetService.SaveText(_root, "../escape.json", "{}"));
    }

    [Fact]
    public void Compare_ReportsMissingExtraAndTypeChanges()
    {
        var baseline = JsonNode.Parse("""{ "a": 1, "b": { "c": "x" }, "rows": [ { "id": 1 } ], "empty": [ { "k": 1 } ] }""");
        var candidate = JsonNode.Parse("""{ "a": "one", "rows": [ { "id": 2, "extra": true } ], "empty": [] }""");
        var comparison = DataSetService.Compare(baseline, candidate);
        Assert.Contains("b.c", comparison.Missing);
        Assert.DoesNotContain("empty[].k", comparison.Missing);
        Assert.Contains("rows[].extra", comparison.Extra);
        Assert.Contains(comparison.TypeChanged, entry => entry.StartsWith("a "));
        Assert.False(comparison.Matches);
    }

    [Fact]
    public void Inspect_CountsTablesRowsAndFiles()
    {
        var health = DataSetService.Inspect(_root, _config.DataSets[0]);
        Assert.True(health.Ok);
        Assert.Equal(1, health.Rows);
        Assert.True(health.Tables >= 1);
        Assert.Contains("history", health.Files.Keys);
    }

    [Fact]
    public void SuggestId_IsUniqueAndValid()
    {
        Assert.Equal("base-2", DataSetService.SuggestId(_config, "Base"));
        Assert.True(DataSetService.IsValidId(DataSetService.SuggestId(_config, "  Ünïcode & Co!  ")));
        Assert.False(DataSetService.IsValidId("../x"));
    }

    [Fact]
    public void ValidateConfiguration_SurfacesResolverErrors()
    {
        File.WriteAllText(Path.Combine(_root, "data", "loop.json"), """{ "$extends": "data/loop.json" }""");
        _config.DataSets.Add(new DemoDataSet { Id = "loop", Name = "Loop", File = "data/loop.json" });
        Assert.Contains(new PresentationService().ValidateConfiguration(_config, _root), error => error.Contains("Data set loop", StringComparison.Ordinal));
    }
}

public sealed class DataLabSampleTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "samples", "Demos", "DataLab");

    [Fact]
    public void AllDataLabDataSets_ResolveWithinLimits()
    {
        var config = new PresentationService().Deserialize(File.ReadAllText(Path.Combine(Root, "presentation.json")));
        Assert.Equal(5, config.DataSets.Count);
        Assert.Single(config.DataSets, set => set.Default);
        foreach (var set in config.DataSets)
        {
            var resolved = DataSetResolver.Resolve(Root, set.File);
            Assert.True(resolved.Ok, $"{set.Id}: {string.Join(" ", resolved.Errors)}");
            Assert.InRange(resolved.Bytes, 1, DataSetLimits.MaxInlineBytes);
        }

        var assetManager = DataSetResolver.Resolve(Root, "data/asset-manager.json");
        Assert.Equal(2, assetManager.Chain.Count);
        Assert.True(assetManager.Data!["trades"]!.AsArray().Count > 100);
        Assert.Equal("GBP", (string?)assetManager.Data["currency"]);
        Assert.Contains("history", assetManager.Files.Keys);
    }
}

public sealed class BrandKitAuthoringTests
{
    private static readonly string DemosRoot = Path.Combine(AppContext.BaseDirectory, "samples", "Demos");

    // Authors copy a demo folder plus the shared brand kit and run Build Package; that must validate without the publisher.
    [Theory]
    [InlineData("Template")]
    [InlineData("DesignedForDigital")]
    [InlineData("DataLab")]
    public void BrandKitDemo_WithSharedFolderCopiedIn_PassesBuildValidation(string folder)
    {
        var root = Path.Combine(Path.GetTempPath(), "fis-authoring-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(Path.Combine(DemosRoot, folder), root);
            CopyDirectory(Path.Combine(DemosRoot, "shared"), Path.Combine(root, "shared"));
            var service = new PresentationService();
            var config = service.Deserialize(File.ReadAllText(Path.Combine(root, "presentation.json")));
            Assert.Empty(service.ValidateConfiguration(config, root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void TemplateDataSets_OverrideMergesOverBase()
    {
        var root = Path.Combine(DemosRoot, "Template");
        var resolved = DataSetResolver.Resolve(root, "data/example-client.json");
        Assert.True(resolved.Ok, string.Join(" ", resolved.Errors));
        Assert.Equal("USD", (string?)resolved.Data!["currency"]);
        Assert.Equal("Tier 2 bank", (string?)resolved.Data["client"]!["segment"]);
        Assert.Equal(4, resolved.Data["accounts"]!.AsArray().Count);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
