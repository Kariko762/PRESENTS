using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FisPresentationPlatform.Models;

namespace FisPresentationPlatform.Services;

public sealed class RepositoryIndex
{
    public string SchemaVersion { get; set; } = "1.0";
    public DateTimeOffset UpdatedUtc { get; set; }
    public List<RepositoryDemo> Demos { get; set; } = [];
}

public sealed class RepositoryDemo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string AccentColor { get; set; } = "#4BCD3E";
    public string TileTheme { get; set; } = "";
    public OrganizationConfig Organization { get; set; } = new();
    public List<RepositoryVersion> Versions { get; set; } = [];
    public RepositoryVersion? Latest => Versions.OrderByDescending(version => version.Version, SemanticVersion.Comparer).FirstOrDefault();
}

public sealed class RepositoryVersion
{
    public string Version { get; set; } = "";
    public string Package { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Bytes { get; set; }
    public DateTimeOffset PublishedUtc { get; set; }
    public string ReleaseNotes { get; set; } = "";
}

public sealed class AssignmentFile
{
    public string SchemaVersion { get; set; } = "1.0";
    public List<UserAssignment> Assignments { get; set; } = [];
}

public sealed class UserAssignment
{
    /// <summary>Windows account as DOMAIN\user, or a bare user name that matches on any domain.</summary>
    public string User { get; set; } = "";
    /// <summary>L8 solution names this user is assigned to.</summary>
    public List<string> Solutions { get; set; } = [];
}

public enum DemoAvailabilityState { NotInstalled, UpdateAvailable, UpToDate, LocalIsNewer }

public sealed record DemoAvailability(RepositoryDemo Demo, RepositoryVersion Latest, LibraryDemo? Installed)
{
    public DemoAvailabilityState State => Installed is null ? DemoAvailabilityState.NotInstalled
        : SemanticVersion.Compare(Latest.Version, Installed.Config.Version) switch
        {
            > 0 => DemoAvailabilityState.UpdateAvailable,
            0 => DemoAvailabilityState.UpToDate,
            _ => DemoAvailabilityState.LocalIsNewer
        };
}

public sealed record RepositorySnapshot(RepositoryIndex Index, bool FromCache, string Message);

public sealed class DemoRepositoryService
{
    public const string IndexFileName = "repository.json";
    public const string AssignmentsFileName = "assignments.json";
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Root { get; }
    public string CacheRoot { get; }

    /// <param name="root">Repository folder (the shared source of truth).</param>
    /// <param name="cacheRoot">Local folder for offline copies of the index and assignments.</param>
    public DemoRepositoryService(string root, string cacheRoot)
    {
        Root = root;
        CacheRoot = cacheRoot;
    }

    public static string CurrentUser => Environment.UserDomainName + "\\" + Environment.UserName;

    /// <summary>Reads the repository index, refreshing the local cached copy. Falls back to the cache when the repository is unreachable.</summary>
    public async Task<RepositorySnapshot> LoadIndexAsync()
    {
        var cached = Path.Combine(CacheRoot, "repository.cache.json");
        try
        {
            var path = Path.Combine(Root, IndexFileName);
            if (!File.Exists(path)) throw new FileNotFoundException($"No {IndexFileName} found in the demo repository '{Root}'.");
            var json = await File.ReadAllTextAsync(path);
            var index = ParseIndex(json);
            await WriteCacheAsync(cached, json);
            return new RepositorySnapshot(index, false, $"{index.Demos.Count} demos published · repository online");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && File.Exists(cached))
        {
            var index = ParseIndex(await File.ReadAllTextAsync(cached));
            return new RepositorySnapshot(index, true, $"Repository unavailable ({exception.Message}) · using cached index of {index.Demos.Count} demos");
        }
    }

    /// <summary>Returns the L8 solutions assigned to the user, or an empty list when the user has no assignment.</summary>
    public async Task<(IReadOnlyList<string> Solutions, bool FromCache)> LoadAssignmentsAsync(string? user = null)
    {
        var cached = Path.Combine(CacheRoot, "assignments.cache.json");
        string json;
        var fromCache = false;
        try
        {
            var path = Path.Combine(Root, AssignmentsFileName);
            if (!File.Exists(path)) throw new FileNotFoundException($"No {AssignmentsFileName} found in the demo repository.");
            json = await File.ReadAllTextAsync(path);
            ParseAssignments(json);
            await WriteCacheAsync(cached, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && File.Exists(cached))
        {
            json = await File.ReadAllTextAsync(cached);
            fromCache = true;
        }
        return (SolutionsFor(ParseAssignments(json), user ?? CurrentUser), fromCache);
    }

    public static IReadOnlyList<string> SolutionsFor(AssignmentFile file, string user)
    {
        var bareUser = user.Contains('\\') ? user[(user.LastIndexOf('\\') + 1)..] : user;
        return file.Assignments
            .Where(assignment => assignment.User.Equals(user, StringComparison.OrdinalIgnoreCase)
                                 || (!assignment.User.Contains('\\') && assignment.User.Equals(bareUser, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(assignment => assignment.Solutions)
            .Where(solution => !string.IsNullOrWhiteSpace(solution))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<DemoAvailability> Compare(RepositoryIndex index, IEnumerable<LibraryDemo> library)
    {
        var installed = library.Where(demo => demo.Available).ToList();
        return index.Demos
            .Where(demo => demo.Latest is not null)
            .Select(demo => new DemoAvailability(demo, demo.Latest!, installed
                .Where(local => local.Config.Id.Equals(demo.Id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(local => local.Config.Version, SemanticVersion.Comparer)
                .FirstOrDefault()))
            .ToList();
    }

    /// <summary>Copies a published package into the local cache, verifies its SHA-256 against the index, imports it and retires older local versions.</summary>
    public async Task<LibraryDemo> DownloadAsync(RepositoryDemo demo, RepositoryVersion version, DemoLibraryService library, IProgress<int>? progress = null)
    {
        if (!ContentFileSafety.IsSafeRelativePath(version.Package))
            throw new InvalidDataException($"Repository entry for '{demo.Id}' has an unsafe package path.");
        var source = Path.GetFullPath(Path.Combine(Root, version.Package.Replace('/', Path.DirectorySeparatorChar)));
        if (!source.StartsWith(Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Repository package path resolves outside the repository.");
        if (!File.Exists(source)) throw new FileNotFoundException($"Published package for {demo.Name} v{version.Version} is missing from the repository.");

        var downloads = Path.Combine(CacheRoot, "downloads");
        Directory.CreateDirectory(downloads);
        var temporary = Path.Combine(downloads, Guid.NewGuid().ToString("N") + ".fisxpkg");
        try
        {
            await using (var input = File.OpenRead(source))
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[1 << 20];
                long copied = 0;
                int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    copied += read;
                    progress?.Report(input.Length == 0 ? 100 : (int)(copied * 80 / input.Length));
                }
            }
            string hash;
            await using (var input = File.OpenRead(temporary)) hash = Convert.ToHexString(await SHA256.HashDataAsync(input)).ToLowerInvariant();
            if (!hash.Equals(version.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Downloaded package for {demo.Name} failed its SHA-256 check; the repository copy may be corrupt or changed.");
            progress?.Report(85);
            var imported = await library.ImportAsync(temporary);
            if (!imported.Config.Id.Equals(demo.Id, StringComparison.OrdinalIgnoreCase) || imported.Config.Version != version.Version)
            {
                await library.RemoveAsync(record => record.Hash == imported.Hash);
                throw new InvalidDataException($"Package identity {imported.Config.Id} v{imported.Config.Version} does not match the repository entry {demo.Id} v{version.Version}.");
            }
            await library.RetireOtherVersionsAsync(demo.Id, imported.Hash);
            progress?.Report(100);
            return imported;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static RepositoryIndex ParseIndex(string json)
    {
        var index = JsonSerializer.Deserialize<RepositoryIndex>(json, JsonOptions) ?? throw new InvalidDataException("Repository index is empty.");
        if (index.SchemaVersion != "1.0") throw new InvalidDataException($"Unsupported repository index version {index.SchemaVersion}.");
        foreach (var demo in index.Demos)
        {
            if (string.IsNullOrWhiteSpace(demo.Id) || string.IsNullOrWhiteSpace(demo.Name)) throw new InvalidDataException("Repository demo entries need an id and name.");
            foreach (var version in demo.Versions)
                if (!SemanticVersion.IsValid(version.Version) || version.Sha256.Length != 64 || !version.Sha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException($"Repository entry {demo.Id} has an invalid version or hash.");
        }
        return index;
    }

    public static AssignmentFile ParseAssignments(string json)
    {
        var file = JsonSerializer.Deserialize<AssignmentFile>(json, JsonOptions) ?? throw new InvalidDataException("Assignments file is empty.");
        if (file.SchemaVersion != "1.0") throw new InvalidDataException($"Unsupported assignments version {file.SchemaVersion}.");
        return file;
    }

    private static async Task WriteCacheAsync(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, json); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public static class SemanticVersion
{
    public static readonly IComparer<string> Comparer = Comparer<string>.Create(Compare);

    public static bool IsValid(string? value) => TryParse(value, out _, out _);

    public static int Compare(string? left, string? right)
    {
        var leftValid = TryParse(left, out var leftCore, out var leftPre);
        var rightValid = TryParse(right, out var rightCore, out var rightPre);
        if (!leftValid || !rightValid) return leftValid.CompareTo(rightValid);
        var core = leftCore.CompareTo(rightCore);
        if (core != 0) return core;
        if (leftPre.Length == 0 || rightPre.Length == 0) return rightPre.Length.CompareTo(leftPre.Length);
        return string.CompareOrdinal(leftPre, rightPre);
    }

    private static bool TryParse(string? value, out Version core, out string prerelease)
    {
        core = new Version(0, 0, 0);
        prerelease = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        var withoutBuild = value.Split('+')[0];
        var dash = withoutBuild.IndexOf('-');
        if (dash >= 0) prerelease = withoutBuild[(dash + 1)..];
        var parts = (dash >= 0 ? withoutBuild[..dash] : withoutBuild).Split('.');
        if (parts.Length != 3 || !parts.All(part => int.TryParse(part, out var number) && number >= 0)) return false;
        core = new Version(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        return true;
    }
}
