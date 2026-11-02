using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FisPresentationPlatform.Models;

namespace FisPresentationPlatform.Services;

public sealed class LibraryDemo
{
    public string Hash { get; set; } = "";
    public PresentationConfig Config { get; set; } = new();
    public DateTimeOffset ImportedUtc { get; set; }
    public DateTimeOffset? LastLaunchedUtc { get; set; }
    public bool Favorite { get; set; }
    public long PackageBytes { get; set; }
    [JsonIgnore] public string PackagePath { get; set; } = "";
    [JsonIgnore] public string? CoverPath { get; set; }
    [JsonIgnore] public bool Available => File.Exists(PackagePath);
    [JsonIgnore] public string Name => Config.Name;
    [JsonIgnore] public string Description => Config.Description;
    [JsonIgnore] public string Product => string.IsNullOrWhiteSpace(Config.Catalog.Product) ? "Unclassified" : Config.Catalog.Product;
    [JsonIgnore] public string Business => string.IsNullOrWhiteSpace(Config.Catalog.Organization.L3) ? "Unassigned" : Config.Catalog.Organization.L3;
    [JsonIgnore] public string Solution => Config.Catalog.Organization.L8;
    [JsonIgnore] public string OrganizationPath => Config.Catalog.Organization.IsEmpty ? "No organizational tags" : Config.Catalog.Organization.Path;
    [JsonIgnore] public string VersionLabel => $"v{Config.Version}";
    [JsonIgnore] public string Classification => string.Join(" · ", Config.Catalog.Functions.Concat(Config.Catalog.Tags));
    [JsonIgnore] public string StatusLabel => Available ? "Verified on import" : "Package missing";
    [JsonIgnore] public string FavoriteLabel => Favorite ? "★" : "☆";
}

public sealed class DemoLibraryService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public const string AllBusinesses = "All businesses";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Root { get; }
    private string IndexPath => Path.Combine(Root, "library.json");
    public DemoLibraryService(string? root = null) => Root = root ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FISPresents", "data");

    public async Task<List<LibraryDemo>> LoadAsync()
    {
        await _gate.WaitAsync();
        try { return await LoadCoreAsync(); }
        finally { _gate.Release(); }
    }

    public async Task<LibraryDemo> ImportAsync(string packagePath)
    {
        await _gate.WaitAsync();
        string? staging = null;
        string? extracted = null;
        try
        {
            ContentFileSafety.EnsureNoReparseAncestors(Root);
            ContentFileSafety.EnsureNoReparseAncestors(packagePath);
            if (new FileInfo(packagePath).Length > 8L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Package exceeds the local library's 8 GiB import limit.");
            Directory.CreateDirectory(Path.Combine(Root, "staging"));
            staging = Path.Combine(Root, "staging", Guid.NewGuid().ToString("N") + ".fisxpkg");
            await using (var input = File.OpenRead(packagePath))
            await using (var output = File.Create(staging)) await input.CopyToAsync(output);
            string hash;
            await using (var input = File.OpenRead(staging)) hash = Convert.ToHexString(await SHA256.HashDataAsync(input)).ToLowerInvariant();
            var records = await LoadCoreAsync();
            var existing = records.FirstOrDefault(demo => demo.Hash == hash);
            if (existing is { Available: true }) return existing;
            BackupBeforeChange();
            var verified = await new PresentationService(cacheRoot: Path.Combine(Root, "staging", "validation")).ExtractAndValidateAsync(staging);
            extracted = verified.Directory;
            var destination = Path.Combine(Root, "packages", hash + ".fisxpkg");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(staging, destination, true);
            staging = null;
            var record = existing ?? new LibraryDemo { Hash = hash, ImportedUtc = DateTimeOffset.UtcNow };
            record.Config = verified.Config;
            record.PackageBytes = new FileInfo(destination).Length;
            var cover = record.Config.Catalog.CoverImage;
            if (!string.IsNullOrEmpty(cover))
            {
                var covers = Path.Combine(Root, "covers");
                Directory.CreateDirectory(covers);
                File.Copy(Path.Combine(extracted, cover.Replace('/', Path.DirectorySeparatorChar)), Path.Combine(covers, hash + Path.GetExtension(cover)), true);
            }
            if (existing is null) records.Add(record);
            await SaveCoreAsync(records);
            SetPaths(record);
            return record;
        }
        finally
        {
            try
            {
                if (staging is not null && File.Exists(staging)) File.Delete(staging);
                if (extracted is not null && Directory.Exists(extracted)) Directory.Delete(extracted, true);
            }
            finally { _gate.Release(); }
        }
    }

    public async Task UpdateAsync(string hash, bool? favorite = null, bool launched = false)
    {
        await _gate.WaitAsync();
        try
        {
            var records = await LoadCoreAsync();
            var record = records.FirstOrDefault(demo => demo.Hash == hash) ?? throw new InvalidDataException("Demo no longer exists in the library.");
            if (favorite.HasValue) BackupBeforeChange();
            if (favorite.HasValue) record.Favorite = favorite.Value;
            if (launched) record.LastLaunchedUtc = DateTimeOffset.UtcNow;
            await SaveCoreAsync(records);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Removes library records (and their cached package/cover files) that match the predicate.</summary>
    public async Task<int> RemoveAsync(Func<LibraryDemo, bool> predicate)
    {
        await _gate.WaitAsync();
        try
        {
            var records = await LoadCoreAsync();
            var removed = records.Where(predicate).ToList();
            if (removed.Count == 0) return 0;
            BackupBeforeChange();
            await SaveCoreAsync(records.Except(removed).ToList());
            foreach (var record in removed.Where(record => !records.Except(removed).Any(kept => kept.Hash == record.Hash)))
                await DeleteFilesAsync(record);
            return removed.Count;
        }
        finally { _gate.Release(); }
    }

    /// <summary>After an update, keeps only <paramref name="keepHash"/> for the presentation ID and carries favorites/launch history forward.</summary>
    public async Task<int> RetireOtherVersionsAsync(string presentationId, string keepHash)
    {
        await _gate.WaitAsync();
        try
        {
            var records = await LoadCoreAsync();
            var kept = records.FirstOrDefault(demo => demo.Hash == keepHash) ?? throw new InvalidDataException("Updated package is not in the library.");
            var older = records.Where(demo => demo.Hash != keepHash && demo.Config.Id.Equals(presentationId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (older.Count == 0) return 0;
            kept.Favorite |= older.Any(demo => demo.Favorite);
            kept.LastLaunchedUtc ??= older.Max(demo => demo.LastLaunchedUtc);
            await SaveCoreAsync(records.Except(older).ToList());
            foreach (var record in older) await DeleteFilesAsync(record);
            return older.Count;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Lower-case, hyphenated id; keeps the "fis-" prefix convention of the source demo.</summary>
    public static string SuggestId(string text, string currentId)
    {
        var slug = System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (currentId.StartsWith("fis-", StringComparison.OrdinalIgnoreCase) && !slug.StartsWith("fis-")) slug = "fis-" + slug;
        if (slug.Length > 80) slug = slug[..80].TrimEnd('-');
        return slug.Length < 2 ? "new-demo" : slug;
    }

    public static bool IsSafeId(string presentationId) =>
        ContentFileSafety.IsSafeRelativePath(presentationId) && !presentationId.Contains('/') && !presentationId.Contains('\\');

    /// <summary>Folder that holds the editable working copy of a library demo.</summary>
    public string WorkspacePath(string presentationId)
    {
        if (!IsSafeId(presentationId))
            throw new InvalidDataException("Presentation identity is not a safe Windows storage identifier.");
        return Path.Combine(Root, "workspaces", presentationId);
    }

    /// <summary>
    /// Extracts the verified package into an editable working folder for the Configuration Studio.
    /// An existing working copy is kept unless <paramref name="replaceExisting"/> is set.
    /// </summary>
    public async Task<string> CreateEditableCopyAsync(LibraryDemo demo, bool replaceExisting = false)
    {
        var workspace = WorkspacePath(demo.Config.Id);
        ContentFileSafety.EnsureNoReparseAncestors(Root);
        if (Directory.Exists(workspace))
        {
            if (!replaceExisting) return workspace;
            Directory.Delete(workspace, true);
        }
        if (!demo.Available) throw new FileNotFoundException("The demo package is missing. Reinstall it from Downloads first.");
        var cache = Path.Combine(Root, "staging", "edit-" + Guid.NewGuid().ToString("N"));
        var staging = workspace + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            var (extracted, _) = await new PresentationService(cacheRoot: cache).ExtractAndValidateAsync(demo.PackagePath);
            foreach (var file in Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(extracted, file);
                // Generated runtime files are recreated by every package build.
                if (relative.Split(Path.DirectorySeparatorChar)[0].Equals("__fis_runtime", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            Directory.Move(staging, workspace);
            return workspace;
        }
        finally
        {
            try
            {
                if (Directory.Exists(cache)) Directory.Delete(cache, true);
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// "Save as new demo": copies <paramref name="sourceFolder"/> into a working copy for <paramref name="config"/>'s new id,
    /// writes the config there, builds a new verified package at <paramref name="packagePath"/> and adds it to the library.
    /// The original demo and its working copy are left untouched.
    /// </summary>
    public async Task<(LibraryDemo Demo, string Workspace)> SaveAsNewDemoAsync(string sourceFolder, PresentationConfig config, string packagePath,
        IProgress<PackageBuildProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if ((await LoadAsync()).Any(demo => demo.Config.Id.Equals(config.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A demo with the id '{config.Id}' is already installed. Choose another id.");
        var workspace = WorkspacePath(config.Id);
        if (Directory.Exists(workspace))
            throw new InvalidOperationException($"A working copy for '{config.Id}' already exists. Choose another id.");
        ContentFileSafety.EnsureNoReparseAncestors(Root);
        var source = Path.GetFullPath(sourceFolder);
        var staging = workspace + ".staging-" + Guid.NewGuid().ToString("N");
        var built = false;
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                if (relative.Split(Path.DirectorySeparatorChar)[0].Equals("__fis_runtime", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var presentations = new PresentationService();
            await File.WriteAllTextAsync(Path.Combine(staging, PresentationService.ConfigurationFileName), presentations.Serialize(config), cancellationToken);
            Directory.Move(staging, workspace);
            await presentations.BuildPackageAsync(workspace, packagePath, config, progress, cancellationToken);
            var imported = await ImportAsync(packagePath);
            built = true;
            return (imported, workspace);
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                if (!built && Directory.Exists(workspace)) Directory.Delete(workspace, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Removes every installed version of a demo, its cached files and (optionally) its editable working copy.</summary>
    public async Task<int> UninstallAsync(string presentationId, bool removeWorkingCopy = true)
    {
        var removed = await RemoveAsync(demo => demo.Config.Id.Equals(presentationId, StringComparison.OrdinalIgnoreCase));
        if (removeWorkingCopy)
        {
            var workspace = WorkspacePath(presentationId);
            if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        }
        return removed;
    }

    private static async Task DeleteFilesAsync(LibraryDemo record)
    {
        foreach (var path in new[] { record.PackagePath, record.CoverPath })
        {
            if (path is null) continue;
            // Antivirus/indexers briefly lock freshly written files; retry before leaving an orphan behind.
            for (var attempt = 0; attempt < 10 && File.Exists(path); attempt++)
            {
                try { File.Delete(path); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { await Task.Delay(150); }
            }
        }
    }

    public static IReadOnlyList<LibraryDemo> Filter(IEnumerable<LibraryDemo> demos, string? search, string? product,
        bool favorites = false, bool recent = false, string sort = "Name", string? business = null, IReadOnlyCollection<string>? solutions = null)
    {
        var query = demos.Where(demo => !favorites || demo.Favorite).Where(demo => !recent || demo.LastLaunchedUtc.HasValue);
        if (!string.IsNullOrWhiteSpace(product) && product != "All products") query = query.Where(demo => demo.Product.Equals(product, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(business) && business != AllBusinesses) query = query.Where(demo => demo.Business.Equals(business, StringComparison.OrdinalIgnoreCase));
        if (solutions is not null) query = query.Where(demo => solutions.Contains(demo.Solution, StringComparer.OrdinalIgnoreCase));
        var words = (search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        query = query.Where(demo => words.All(word => string.Join(" ", demo.Name, demo.Description, demo.Product, demo.Classification, demo.OrganizationPath).Contains(word, StringComparison.OrdinalIgnoreCase)));
        return (recent || sort == "Recently launched" ? query.OrderByDescending(demo => demo.LastLaunchedUtc)
            : sort == "Newest imported" ? query.OrderByDescending(demo => demo.ImportedUtc)
            : query.OrderBy(demo => demo.Name, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private async Task<List<LibraryDemo>> LoadCoreAsync()
    {
        ContentFileSafety.EnsureNoReparseAncestors(Root);
        if (!File.Exists(IndexPath)) return [];
        var records = JsonSerializer.Deserialize<List<LibraryDemo>>(await File.ReadAllTextAsync(IndexPath), JsonOptions)
            ?? throw new InvalidDataException("The library index is empty or invalid.");
        foreach (var record in records)
        {
            if (record is null || record.Hash is null || record.Hash.Length != 64 || !record.Hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid library package identity.");
            if (ConfigurationSchemaValidator.Validate(JsonSerializer.Serialize(record.Config, JsonOptions)).Count > 0)
                throw new InvalidDataException("Library metadata does not match the configuration schema.");
            if (!string.IsNullOrEmpty(record.Config.Catalog.CoverImage) && !ContentFileSafety.IsSafeRelativePath(record.Config.Catalog.CoverImage))
                throw new InvalidDataException("Library metadata contains an unsafe cover path.");
            SetPaths(record);
        }
        return records;
    }

    private async Task SaveCoreAsync(List<LibraryDemo> records)
    {
        Directory.CreateDirectory(Root);
        var temp = IndexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(records, JsonOptions)); File.Move(temp, IndexPath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private void BackupBeforeChange()
    {
        var settings = ApplicationSettingsService.Current;
        if (settings.BackupBeforeImport && File.Exists(IndexPath)
            && Path.GetFullPath(Root).Equals(Path.GetFullPath(settings.LibraryLocation), StringComparison.OrdinalIgnoreCase))
            ApplicationSettingsService.CreateBackup(settings);
    }

    private void SetPaths(LibraryDemo record)
    {
        record.PackagePath = Path.Combine(Root, "packages", record.Hash + ".fisxpkg");
        var cover = Path.Combine(Root, "covers", record.Hash + Path.GetExtension(record.Config.Catalog.CoverImage));
        record.CoverPath = File.Exists(cover) ? cover : null;
    }
}