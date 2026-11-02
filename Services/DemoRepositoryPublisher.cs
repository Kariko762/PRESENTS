using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FisPresentationPlatform.Models;

namespace FisPresentationPlatform.Services;

/// <summary>Publishes demo source folders into a demo repository (repository.json + versioned .fisxpkg files).</summary>
public static class DemoRepositoryPublisher
{
    public static string BundledDemosRoot => Path.Combine(AppContext.BaseDirectory, "samples", "Demos");

    /// <summary>The bundled demo source folders, in story order.</summary>
    public static readonly string[] BundledDemoFolders = ["DesignedForDigital", "DigitalAdvantage", "ConfidentDelivery", "DataLab"];

    /// <summary>Thrown when a release would replace a version that is already published.</summary>
    public sealed class VersionAlreadyPublishedException(string id, string version)
        : InvalidOperationException($"{id} v{version} is already published. Release a higher version, or use --force to replace it (laptops that already installed it will not be told it changed).")
    {
        public string Id { get; } = id;
        public string Version { get; } = version;
    }

    /// <summary>Returns the published version numbers of a demo, or an empty list.</summary>
    public static async Task<IReadOnlyList<string>> PublishedVersionsAsync(string repositoryRoot, string id)
    {
        var indexPath = Path.Combine(repositoryRoot, DemoRepositoryService.IndexFileName);
        if (!File.Exists(indexPath)) return [];
        var index = DemoRepositoryService.ParseIndex(await File.ReadAllTextAsync(indexPath));
        return index.Demos.FirstOrDefault(demo => demo.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Versions.Select(entry => entry.Version).ToList() ?? [];
    }

    public static async Task<RepositoryVersion> PublishAsync(string repositoryRoot, string demoFolder, string? version = null, string releaseNotes = "", string? sharedFolder = null, bool force = false)
    {
        sharedFolder ??= Path.Combine(Path.GetDirectoryName(Path.GetFullPath(demoFolder).TrimEnd(Path.DirectorySeparatorChar))!, "shared");
        var service = new PresentationService();
        var config = service.Deserialize(await File.ReadAllTextAsync(Path.Combine(demoFolder, PresentationService.ConfigurationFileName)));
        if (version is not null)
        {
            if (!SemanticVersion.IsValid(version)) throw new ArgumentException($"'{version}' is not a semantic version such as 1.2.0.", nameof(version));
            config.Version = version;
        }
        if (string.IsNullOrWhiteSpace(releaseNotes)) releaseNotes = $"Version {config.Version} published.";
        // Checked before building so a refused release never touches the published package file.
        if (!force && (await PublishedVersionsAsync(repositoryRoot, config.Id)).Contains(config.Version, StringComparer.OrdinalIgnoreCase))
            throw new VersionAlreadyPublishedException(config.Id, config.Version);

        var staging = Path.Combine(Path.GetTempPath(), "fis-publish-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(demoFolder, staging);
            if (Directory.Exists(sharedFolder)) CopyDirectory(sharedFolder, Path.Combine(staging, "shared"));
            await File.WriteAllTextAsync(Path.Combine(staging, "shared", "version.js"),
                "window.FIS_DEMO = " + JsonSerializer.Serialize(new { id = config.Id, name = config.Name, version = config.Version, releaseNotes,
                    solution = config.Catalog.Organization.L8, business = config.Catalog.Organization.L3 }) + ";\n");
            config.Catalog.CoverImage = "cover.png";
            CreateCover(Path.Combine(staging, "cover.png"), config);

            var relative = $"packages/{config.Id}/{config.Version}/{config.Id}-{config.Version}.fisxpkg";
            var destination = Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await service.BuildPackageAsync(staging, destination, config);

            string hash;
            await using (var stream = File.OpenRead(destination)) hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            var published = new RepositoryVersion
            {
                Version = config.Version, Package = relative, Sha256 = hash, Bytes = new FileInfo(destination).Length,
                PublishedUtc = DateTimeOffset.UtcNow, ReleaseNotes = releaseNotes
            };

            var indexPath = Path.Combine(repositoryRoot, DemoRepositoryService.IndexFileName);
            var index = File.Exists(indexPath) ? DemoRepositoryService.ParseIndex(await File.ReadAllTextAsync(indexPath)) : new RepositoryIndex();
            var entry = index.Demos.FirstOrDefault(demo => demo.Id.Equals(config.Id, StringComparison.OrdinalIgnoreCase));
            if (entry is null) index.Demos.Add(entry = new RepositoryDemo { Id = config.Id });
            entry.Name = config.Name;
            entry.Description = config.Description;
            entry.AccentColor = config.Branding.AccentColor;
            entry.TileTheme = config.Branding.TileTheme;
            entry.Organization = config.Catalog.Organization;
            entry.Versions.RemoveAll(existing => existing.Version == config.Version);
            entry.Versions.Add(published);
            index.UpdatedUtc = DateTimeOffset.UtcNow;
            await WriteJsonAtomicAsync(indexPath, index);
            return published;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    /// <summary>
    /// Withdraws one version (or, when <paramref name="version"/> is null, every version) of a demo from the repository.
    /// The index is updated atomically first, then the package folders are deleted. Laptops that installed it keep their copy.
    /// </summary>
    /// <returns>The versions that were removed.</returns>
    public static async Task<IReadOnlyList<string>> RetireAsync(string repositoryRoot, string id, string? version = null)
    {
        var indexPath = Path.Combine(repositoryRoot, DemoRepositoryService.IndexFileName);
        if (!File.Exists(indexPath)) throw new FileNotFoundException("The repository has no index.", indexPath);
        var index = DemoRepositoryService.ParseIndex(await File.ReadAllTextAsync(indexPath));
        var entry = index.Demos.FirstOrDefault(demo => demo.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Demo '{id}' is not published.");
        var removed = entry.Versions.Where(item => version is null || item.Version.Equals(version, StringComparison.OrdinalIgnoreCase)).ToList();
        if (removed.Count == 0) throw new InvalidOperationException($"Version {version} of '{id}' is not published.");
        entry.Versions.RemoveAll(removed.Contains);
        if (entry.Versions.Count == 0) index.Demos.Remove(entry);
        index.UpdatedUtc = DateTimeOffset.UtcNow;
        await WriteJsonAtomicAsync(indexPath, index);

        var packagesRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "packages"));
        foreach (var item in removed)
        {
            var folder = Path.GetFullPath(Path.GetDirectoryName(Path.Combine(repositoryRoot, item.Package.Replace('/', Path.DirectorySeparatorChar)))!);
            if (folder.StartsWith(packagesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
        var demoFolder = Path.Combine(packagesRoot, entry.Id);
        if (Directory.Exists(demoFolder) && !Directory.EnumerateFileSystemEntries(demoFolder).Any()) Directory.Delete(demoFolder);
        return removed.Select(item => item.Version).ToList();
    }

    public static Task WriteAssignmentsAsync(string repositoryRoot, AssignmentFile assignments) =>
        WriteJsonAtomicAsync(Path.Combine(repositoryRoot, DemoRepositoryService.AssignmentsFileName), assignments);

    private static async Task WriteJsonAtomicAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, DemoRepositoryService.JsonOptions)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in ContentFileSafety.EnumerateSourceFiles(source))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    /// <summary>Renders a branded 640×292 library cover: FIS palette, swoosh, logo, business line and title.</summary>
    private static void CreateCover(string path, PresentationConfig config)
    {
        var logoPath = string.IsNullOrEmpty(config.Branding.Logo) ? null : Path.Combine(Path.GetDirectoryName(path)!, config.Branding.Logo.Replace('/', Path.DirectorySeparatorChar));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                BitmapImage? logo = null;
                if (logoPath is not null && File.Exists(logoPath))
                {
                    logo = new BitmapImage();
                    logo.BeginInit(); logo.CacheOption = BitmapCacheOption.OnLoad; logo.UriSource = new Uri(logoPath); logo.EndInit();
                }
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                    TileThemes.Draw(drawing, config.Branding.TileTheme, config.Branding.AccentColor, config.Id, logo, config.Catalog.Organization.L3, config.Name);
                var bitmap = new RenderTargetBitmap(640, 292, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(path);
                encoder.Save(stream);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
