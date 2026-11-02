using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FisPresentationPlatform.Models;

namespace FisPresentationPlatform.Services;

public sealed class PresentationService
{
    private readonly PackageLimits _limits;
    private readonly string _cacheRoot;

    public PresentationService(PackageLimits? limits = null, string? cacheRoot = null)
    {
        _limits = limits ?? new PackageLimits();
        _cacheRoot = cacheRoot ?? ApplicationSettingsService.Current.CacheLocation;
    }
    public const string ConfigurationFileName = "presentation.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", "packages"
    };

    public string Serialize(PresentationConfig config) => JsonSerializer.Serialize(config, JsonOptions);

    public PresentationConfig Deserialize(string json)
    {
        var errors = ConfigurationSchemaValidator.Validate(json);
        if (errors.Count > 0) throw new JsonException(string.Join(Environment.NewLine, errors));
        return JsonSerializer.Deserialize<PresentationConfig>(json, JsonOptions)
            ?? throw new JsonException("$: The configuration file is empty or invalid.");
    }

    public async Task<(PresentationConfig Config, string Json, bool Created)> LoadOrCreateConfigAsync(string sourceDirectory)
    {
        var configPath = Path.Combine(sourceDirectory, ConfigurationFileName);
        ContentFileSafety.EnsureNoReparseAncestors(configPath);
        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath);
            return (Deserialize(json), json, false);
        }

        var supportedFiles = ContentFileSafety.EnumerateSourceFiles(sourceDirectory)
            .Where(file => !IsInExcludedDirectory(sourceDirectory, file))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var htmlFiles = supportedFiles.Where(file => Path.GetExtension(file).Equals(".html", StringComparison.OrdinalIgnoreCase)
                                                      || Path.GetExtension(file).Equals(".htm", StringComparison.OrdinalIgnoreCase)).ToList();
        var mediaFiles = supportedFiles.Where(file => IsDirectMediaEntry(Path.GetExtension(file))).ToList();
        if (htmlFiles.Count == 0 && mediaFiles.Count == 0)
        {
            throw new InvalidDataException("This folder has no supported HTML pages or image/audio/video files. Add content or choose a presentation folder.");
        }

        var entry = htmlFiles.FirstOrDefault(file => Path.GetFileName(file).Equals("index.html", StringComparison.OrdinalIgnoreCase))
                    ?? htmlFiles.FirstOrDefault()
                    ?? mediaFiles[0];
        var navigationFiles = htmlFiles.Count > 0 ? htmlFiles : [entry];
        var name = new DirectoryInfo(sourceDirectory).Name;
        var config = new PresentationConfig
        {
            Id = ToIdentifier(name),
            Name = name,
            EntryPoint = ToPackagePath(sourceDirectory, entry),
            Navigation = new NavigationConfig
            {
                Sections =
                [
                    new NavigationSection
                    {
                        Id = "main",
                        Title = "Presentation",
                        Pages = navigationFiles.Select((file, index) => new NavigationPage
                        {
                            Id = ToIdentifier(Path.GetFileNameWithoutExtension(file)) + (index == 0 ? "" : $"-{index + 1}"),
                            Title = Path.GetFileNameWithoutExtension(file).Replace('-', ' ').Replace('_', ' '),
                            Path = ToPackagePath(sourceDirectory, file)
                        }).ToList()
                    }
                ]
            }
        };
        var generatedJson = Serialize(config);
        await File.WriteAllTextAsync(configPath, generatedJson);
        return (config, generatedJson, true);
    }

    public List<string> ValidateConfiguration(PresentationConfig config, string sourceDirectory)
    {
        var errors = ConfigurationSchemaValidator.Validate(Serialize(config));
        // Stop before accessing nested groups when schema/type/null checks failed.
        if (errors.Count > 0) return errors;
        if (config.SchemaVersion != "1.0") errors.Add("schemaVersion must be 1.0.");
        if (string.IsNullOrWhiteSpace(config.Id) || !Regex.IsMatch(config.Id, "^[a-zA-Z0-9][a-zA-Z0-9._-]{1,79}$", RegexOptions.CultureInvariant))
            errors.Add("id is required and must be 2–80 letters, numbers, dots, underscores, or hyphens.");
        if (!ContentFileSafety.IsSafeRelativePath(config.Id)) errors.Add("id must be a safe Windows storage identifier.");
        if (string.IsNullOrWhiteSpace(config.Name) || config.Name.Length > 120) errors.Add("name is required and must be no more than 120 characters.");
        if (config.Description.Length > 1000) errors.Add("description must be no more than 1000 characters.");
        if (!Regex.IsMatch(config.Version, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?(\\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant))
            errors.Add("version must use semantic version format, for example 1.0.0.");
        if (!IsSafeRelativePath(config.EntryPoint)) errors.Add("entryPoint must be a safe relative path.");
        else if (!File.Exists(ResolvePath(sourceDirectory, config.EntryPoint))) errors.Add($"Entry point not found: {config.EntryPoint}");
        else if (!Path.GetExtension(config.EntryPoint).Equals(".html", StringComparison.OrdinalIgnoreCase)
                 && !Path.GetExtension(config.EntryPoint).Equals(".htm", StringComparison.OrdinalIgnoreCase)
                 && !IsDirectMediaEntry(Path.GetExtension(config.EntryPoint)))
            errors.Add("entryPoint must be an HTML page or a supported image, audio, or video file.");
        if (!IsValidColor(config.Branding.AccentColor)) errors.Add("branding.accentColor must be a hex color such as #4BCD3E.");
        if (!TileThemes.IsKnown(config.Branding.TileTheme))
            errors.Add($"branding.tileTheme must be one of: {string.Join(", ", TileThemes.All.Select(theme => theme.Id))}.");
        if (!string.IsNullOrWhiteSpace(config.Branding.Logo) &&
            (!IsSafeRelativePath(config.Branding.Logo) || !File.Exists(ResolvePath(sourceDirectory, config.Branding.Logo))))
            errors.Add("branding.logo must point to an existing local image using a safe relative path.");
        if (!string.IsNullOrWhiteSpace(config.Catalog.CoverImage))
        {
            var coverExtension = Path.GetExtension(config.Catalog.CoverImage);
            if (!IsSafeRelativePath(config.Catalog.CoverImage)
                || !File.Exists(ResolvePath(sourceDirectory, config.Catalog.CoverImage))
                || !IsSupportedCoverImage(coverExtension))
                errors.Add("catalog.coverImage must point to an existing PNG, JPEG, or BMP inside the source folder.");
        }
        ValidateLabels(config.Catalog.Functions, "catalog.functions", errors);
        ValidateLabels(config.Catalog.Tags, "catalog.tags", errors);
        if (config.Navigation.Enabled && config.Navigation.Sections.Count == 0)
            errors.Add("navigation.sections must contain at least one section when navigation is enabled.");
        if (config.Navigation.Enabled && !config.Navigation.Sections.Any(section => section.Pages.Count > 0))
            errors.Add("navigation.sections: an enabled menu requires at least one page.");

        var sectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in config.Navigation.Sections)
        {
            if (!IsValidConfigId(section.Id) || !sectionIds.Add(section.Id))
                errors.Add($"Section id is invalid or duplicated: {section.Id}");
            if (string.IsNullOrWhiteSpace(section.Title)) errors.Add($"Section {section.Id} needs a title.");
            foreach (var page in section.Pages)
            {
                if (!IsValidConfigId(page.Id) || !pageIds.Add(page.Id))
                    errors.Add($"Page id is invalid or duplicated: {page.Id}");
                if (string.IsNullOrWhiteSpace(page.Title)) errors.Add($"Page {page.Id} needs a title.");
                if (!IsSafeRelativePath(page.Path)) errors.Add($"Page {page.Id} has an unsafe relative path: {page.Path}");
                else if (!File.Exists(ResolvePath(sourceDirectory, page.Path))) errors.Add($"Page file not found: {page.Path}");
                else if (!Path.GetExtension(page.Path).Equals(".html", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetExtension(page.Path).Equals(".htm", StringComparison.OrdinalIgnoreCase)
                         && !(IsDirectMediaEntry(Path.GetExtension(page.Path)) && page.Path.Equals(config.EntryPoint, StringComparison.OrdinalIgnoreCase)))
                    errors.Add($"Page {page.Id} must reference an HTML page or the supported media entry point: {page.Path}");
            }
        }

        var dataSetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataSet in config.DataSets)
        {
            if (!IsValidConfigId(dataSet.Id) || !dataSetIds.Add(dataSet.Id)) errors.Add($"Data set id is invalid or duplicated: {dataSet.Id}");
            if (string.IsNullOrWhiteSpace(dataSet.Name)) errors.Add($"Data set {dataSet.Id} needs a name.");
            if (string.IsNullOrWhiteSpace(dataSet.File)) continue;
            if (!IsSafeRelativePath(dataSet.File) || !Path.GetExtension(dataSet.File).Equals(".json", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Data set {dataSet.Id} must reference a JSON file using a safe relative path: {dataSet.File}");
            else if (!File.Exists(ResolvePath(sourceDirectory, dataSet.File))) errors.Add($"Data set file not found: {dataSet.File}");
            else errors.AddRange(DataSetResolver.Resolve(sourceDirectory, dataSet.File).Errors.Select(error => $"Data set {dataSet.Id}: {error}"));
        }
        if (config.DataSets.Count(dataSet => dataSet.Default) > 1) errors.Add("Only one data set can be marked as the default.");

        foreach (var reference in new AssetReferenceScanner().Scan(sourceDirectory)
                     .Where(reference => reference.Status is "Missing" or "Unsafe path" or "Invalid path"))
        {
            errors.Add($"{reference.Status} resource '{reference.Reference}' referenced by {reference.SourceFile}.");
        }

        foreach (var page in config.Navigation.Sections.SelectMany(section => section.Pages)
                     .Where(page => IsDirectMediaEntry(Path.GetExtension(page.Path))))
        {
            if (!page.Path.Equals(config.EntryPoint, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Only the entry point can directly open a media file: {page.Path}. Add an HTML page with media controls for other media.");
        }

        return errors;
    }

    public async Task<PackageManifest> BuildPackageAsync(
        string sourceDirectory,
        string outputPath,
        PresentationConfig config,
        IProgress<PackageBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new PackageBuildProgress("Validating configuration…", 0));
        var errors = ValidateConfiguration(config, sourceDirectory);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));

        var files = ContentFileSafety.EnumerateSourceFiles(sourceDirectory)
            .Where(file => !IsInExcludedDirectory(sourceDirectory, file))
            .Where(file => !ToPackagePath(sourceDirectory, file).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .Where(file => !Path.GetExtension(file).Equals(".fisxpkg", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.EndsWith(".building", StringComparison.OrdinalIgnoreCase))
            .Where(file => !Path.GetFullPath(file).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var tempPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".building";
        ContentFileSafety.EnsureNoReparseAncestors(outputPath);
        if (files.Count + 3 > _limits.MaxEntries || files.Sum(file => new FileInfo(file).Length) > _limits.MaxExpandedBytes)
            throw new InvalidDataException("Source content exceeds package file-count or expanded-size limits.");
        if (files.Any(file => new FileInfo(file).Length > _limits.MaxFileBytes))
            throw new InvalidDataException("A source file exceeds the package per-file size limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        progress?.Report(new PackageBuildProgress("Scanning source files…", 5));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configBytes = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);
            var manifest = new PackageManifest
            {
                PackageId = config.Id,
                PresentationId = config.Id,
                PresentationVersion = config.Version,
                EntryPoint = NormalizePackagePath(IsDirectMediaEntry(Path.GetExtension(config.EntryPoint))
                    ? $"__fis_runtime/{Path.GetFileNameWithoutExtension(config.EntryPoint)}-player.html"
                    : config.EntryPoint),
                CreatedUtc = DateTimeOffset.UtcNow
            };
            manifest.Files.Add(new ManifestFile
            {
                Path = ConfigurationFileName,
                Size = configBytes.LongLength,
                ContentType = "application/json",
                Sha256 = Convert.ToHexString(SHA256.HashData(configBytes)).ToLowerInvariant()
            });

            var contentFiles = files.Where(file => !ToPackagePath(sourceDirectory, file).Equals(ConfigurationFileName, StringComparison.OrdinalIgnoreCase)).ToList();
            for (var index = 0; index < contentFiles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = contentFiles[index];
                var relativePath = ToPackagePath(sourceDirectory, file);
                var info = new FileInfo(file);
                await using var stream = File.OpenRead(file);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
                manifest.Files.Add(new ManifestFile
                {
                    Path = relativePath,
                    Size = info.Length,
                    ContentType = GetContentType(Path.GetExtension(file)),
                    Sha256 = hash
                });
                progress?.Report(new PackageBuildProgress($"Hashing {Path.GetFileName(file)}", 10 + (int)((index + 1d) / Math.Max(1, contentFiles.Count) * 35)));
            }

            byte[]? generatedMediaPlayer = null;
            if (IsDirectMediaEntry(Path.GetExtension(config.EntryPoint)))
            {
                var mediaPath = Path.GetFullPath(ResolvePath(sourceDirectory, config.EntryPoint));
                var relativeMediaPath = ToPackagePath(sourceDirectory, mediaPath);
                generatedMediaPlayer = CreateMediaPlayerPage(config.Name, relativeMediaPath, GetContentType(Path.GetExtension(mediaPath)), config.Branding.AccentColor);
                manifest.Files.Add(new ManifestFile
                {
                    Path = manifest.EntryPoint,
                    Size = generatedMediaPlayer.LongLength,
                    ContentType = "text/html",
                    Sha256 = Convert.ToHexString(SHA256.HashData(generatedMediaPlayer)).ToLowerInvariant()
                });
            }

            await using (var output = File.Create(tempPath))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                for (var index = 0; index < contentFiles.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var file = contentFiles[index];
                    var relativePath = ToPackagePath(sourceDirectory, file);
                    var entry = archive.CreateEntry(relativePath, GetCompressionLevel(Path.GetExtension(file)));
                    await using var source = File.OpenRead(file);
                    await using var destination = entry.Open();
                    await source.CopyToAsync(destination, cancellationToken);
                    progress?.Report(new PackageBuildProgress($"Adding {Path.GetFileName(file)}", 45 + (int)((index + 1d) / Math.Max(1, contentFiles.Count) * 40)));
                }

                cancellationToken.ThrowIfCancellationRequested();
                await WriteEntryAsync(archive, ConfigurationFileName, configBytes);
                if (generatedMediaPlayer is not null)
                    await WriteEntryAsync(archive, manifest.EntryPoint, generatedMediaPlayer);
                await WriteEntryAsync(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
            }

            progress?.Report(new PackageBuildProgress("Verifying package hashes…", 90));
            await VerifyBuiltArchiveAsync(tempPath, manifest, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, outputPath, true);
            progress?.Report(new PackageBuildProgress("Package complete", 100));
            return manifest;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public async Task<(string Directory, PresentationConfig Config)> ExtractAndValidateAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        ContentFileSafety.EnsureNoReparseAncestors(_cacheRoot);
        await using var packageStream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var packageHash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream, cancellationToken)).ToLowerInvariant();
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        ValidateArchiveBudgets(archive);
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Package manifest.json is missing.");
        PackageManifest manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<PackageManifest>(manifestStream, JsonOptions, cancellationToken)
                       ?? throw new InvalidDataException("Package manifest is invalid.");
        }

        if (manifest.Files is null || manifest.Files.Count == 0 || manifest.Files.Count >= _limits.MaxEntries)
            throw new InvalidDataException("Package manifest file list is empty or exceeds limits.");
        if (manifest.Files.Any(file => file is null || !IsSafeRelativePath(file.Path) || file.Size < 0
            || file.Sha256 is null || !Regex.IsMatch(file.Sha256, "^[0-9a-fA-F]{64}$")
            || file.Path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Package manifest contains an unsafe file path.");
        if (manifest.Files.Select(file => NormalizePackagePath(file.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count)
            throw new InvalidDataException("Package manifest contains duplicate file paths.");
        var expected = manifest.Files.ToDictionary(item => NormalizePackagePath(item.Path), StringComparer.OrdinalIgnoreCase);
        if (!expected.ContainsKey(ConfigurationFileName) || !IsSafeRelativePath(manifest.EntryPoint))
            throw new InvalidDataException("Package manifest is missing required file information.");
        foreach (var file in manifest.Files)
        {
            var path = NormalizePackagePath(file.Path);
            var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"Package file is missing: {path}");
            await using var stream = entry.Open();
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (entry.Length != file.Size || !hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Package integrity check failed for {path}.");
        }

        var packageEntries = archive.Entries
            .Where(entry => !entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in packageEntries)
        {
            var path = NormalizePackagePath(entry.FullName);
            if (!IsSafeRelativePath(entry.FullName) || !expected.ContainsKey(path) || !seenPaths.Add(path))
                throw new InvalidDataException($"Unexpected, duplicate, or unsafe package file: {path}");
        }
        if (seenPaths.Count != expected.Count)
            throw new InvalidDataException("Package contents do not match the manifest.");

        // Separate per-open extraction under a package hash prevents running players being overwritten.
        var configEntry = archive.GetEntry(ConfigurationFileName)!;
        PresentationConfig archiveConfig;
        using (var reader = new StreamReader(configEntry.Open()))
            archiveConfig = Deserialize(await reader.ReadToEndAsync(cancellationToken));
        if (manifest.PackageId != archiveConfig.Id || manifest.PresentationId != archiveConfig.Id || manifest.PresentationVersion != archiveConfig.Version)
            throw new InvalidDataException("Manifest presentation ID, package ID and version must match presentation.json.");
        if (!ContentFileSafety.IsSafeRelativePath(archiveConfig.Id))
            throw new InvalidDataException("Presentation identity is not a safe Windows storage identifier.");
        var destination = Path.Combine(_cacheRoot, archiveConfig.Id, packageHash, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var volume = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!);
        var expandedBytes = archive.Entries.Sum(entry => entry.Length);
        if (volume.AvailableFreeSpace < expandedBytes + 32L * 1024 * 1024)
            throw new IOException("Not enough free space to safely extract the presentation package.");
        var staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var entry in packageEntries)
            {
                var relativePath = NormalizePackagePath(entry.FullName);
                var target = Path.GetFullPath(Path.Combine(staging, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package contains an unsafe path.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var source = entry.Open();
                await using var output = File.Create(target);
                await source.CopyToAsync(output, cancellationToken);
            }

            var configPath = Path.Combine(staging, ConfigurationFileName);
            var config = Deserialize(await File.ReadAllTextAsync(configPath));
            if (!string.Equals(manifest.PresentationId, config.Id, StringComparison.Ordinal)
                || !string.Equals(manifest.PackageId, config.Id, StringComparison.Ordinal)
                || !string.Equals(manifest.PresentationVersion, config.Version, StringComparison.Ordinal))
                throw new InvalidDataException("Manifest presentation ID, package ID and version must match presentation.json.");
            var configuredManifestEntry = IsDirectMediaEntry(Path.GetExtension(config.EntryPoint))
                ? $"__fis_runtime/{Path.GetFileNameWithoutExtension(config.EntryPoint)}-player.html"
                : config.EntryPoint;
            if (!NormalizePackagePath(configuredManifestEntry).Equals(NormalizePackagePath(manifest.EntryPoint), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The configured entry point does not match the package manifest.");
            var errors = ValidateConfiguration(config, staging);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);

            return (destination, config);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            throw;
        }
    }

    public static bool IsValidColor(string color) =>
        color.Length == 7 && color[0] == '#' && color[1..].All(Uri.IsHexDigit);

    private static bool IsValidConfigId(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && Regex.IsMatch(value, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$", RegexOptions.CultureInvariant);

    private static void ValidateLabels(IEnumerable<string> labels, string property, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels)
        {
            if (string.IsNullOrWhiteSpace(label) || label.Length > 80 || !seen.Add(label.Trim()))
                errors.Add($"{property} entries must be unique, non-empty labels of at most 80 characters.");
        }
    }

    private static bool IsSupportedCoverImage(string extension) => extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp";

    private static CompressionLevel GetCompressionLevel(string extension) => extension.ToLowerInvariant() switch
    {
        ".mp4" or ".m4v" or ".mov" or ".webm" or ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp"
            or ".avif" or ".woff" or ".woff2" or ".zip" or ".gz" or ".pdf" => CompressionLevel.NoCompression,
        _ => CompressionLevel.Optimal
    };

    private static string GetContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html",
        ".css" => "text/css",
        ".js" or ".mjs" => "text/javascript",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".ttf" => "font/ttf",
        _ => "application/octet-stream"
    };

    private static bool IsDirectMediaEntry(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".bmp" or ".avif"
            or ".mp4" or ".m4v" or ".mov" or ".webm" or ".ogv"
            or ".mp3" or ".wav" or ".ogg" or ".m4a" => true,
        _ => false
    };

    private static byte[] CreateMediaPlayerPage(string title, string mediaPath, string contentType, string accentColor)
    {
        var encodedTitle = System.Net.WebUtility.HtmlEncode(title);
        var encodedPath = System.Net.WebUtility.HtmlEncode($"../{mediaPath}");
        var safeAccent = IsValidColor(accentColor) ? accentColor : "#4BCD3E";
        var mediaMarkup = contentType.StartsWith("video/", StringComparison.Ordinal)
            ? $"<video controls autoplay playsinline preload=\"metadata\"><source src=\"{encodedPath}\" type=\"{contentType}\"></video>"
            : contentType.StartsWith("audio/", StringComparison.Ordinal)
                ? $"<audio controls autoplay src=\"{encodedPath}\"></audio>"
                : $"<img src=\"{encodedPath}\" alt=\"{encodedTitle}\">";
        var html = $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>{encodedTitle}</title><style>*{{box-sizing:border-box}}body{{margin:0;min-height:100vh;background:#1D1F48;color:white;font:16px 'Segoe UI',sans-serif;display:grid;place-items:center;padding:32px}}main{{width:min(1100px,100%)}}h1{{font-size:clamp(28px,5vw,56px);margin:0 0 24px}}img,video{{display:block;max-width:100%;max-height:76vh;margin:auto;border-radius:16px;background:#10122A}}img{{object-fit:contain}}video{{width:100%}}p{{color:{safeAccent};letter-spacing:.08em;text-transform:uppercase;font-size:12px}}</style></head><body><main><p>FIS PRESENTATION STUDIO · LOCAL MEDIA</p><h1>{encodedTitle}</h1>{mediaMarkup}<p>Packaged locally · no internet required</p></main></body></html>";
        return System.Text.Encoding.UTF8.GetBytes(html);
    }

    private async Task VerifyBuiltArchiveAsync(string packagePath, PackageManifest manifest, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        ValidateArchiveBudgets(archive);
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.GetEntry(file.Path) ?? throw new InvalidDataException($"Built package is missing {file.Path}.");
            await using var stream = entry.Open();
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (entry.Length != file.Size || !hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Built package verification failed for {file.Path}.");
        }
        if (archive.GetEntry(manifest.EntryPoint) is null) throw new InvalidDataException("Built package entry point is missing.");
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
    }

    private static bool IsInExcludedDirectory(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => ExcludedDirectories.Contains(part));
    }

    private static string ToPackagePath(string root, string file) =>
        NormalizePackagePath(Path.GetRelativePath(root, file));

    private static string NormalizePackagePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static bool IsSafeRelativePath(string path) => ContentFileSafety.IsSafeRelativePath(path);

    private void ValidateArchiveBudgets(ZipArchive archive)
    {
        if (archive.Entries.Count > _limits.MaxEntries) throw new InvalidDataException("Package exceeds archive entry-count limit.");
        long total = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (!IsSafeRelativePath(entry.FullName) || !paths.Add(NormalizePackagePath(entry.FullName)))
                throw new InvalidDataException("Archive contains an unsafe or duplicate path.");
            // Unix symbolic-link entries must not be interpreted as presentation content.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Archive symbolic links are not permitted.");
            if (entry.Length > _limits.MaxFileBytes || entry.Length > _limits.MaxExpandedBytes - total)
                throw new InvalidDataException("Package exceeds expanded-file size limits.");
            total += entry.Length;
            if (entry.FullName == "manifest.json" && entry.Length > _limits.MaxManifestBytes)
                throw new InvalidDataException("Package manifest exceeds size limit.");
            if (entry.FullName == ConfigurationFileName && entry.Length > _limits.MaxConfigurationBytes)
                throw new InvalidDataException("Package configuration exceeds size limit.");
            if (entry.Length > 1024 * 1024 && entry.Length / (double)Math.Max(1, entry.CompressedLength) > _limits.MaxCompressionRatio)
                throw new InvalidDataException("Archive compression ratio exceeds safe limits.");
        }
    }

    private static string ResolvePath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));

    private static string ToIdentifier(string value)
    {
        var result = new string(value.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "presentation" : result;
    }
}
