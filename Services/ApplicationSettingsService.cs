using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Win32;

namespace FisPresentationPlatform.Services;

public sealed class ApplicationSettings
{
    public string LibraryLocation { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FISPresents", "data");
    public string CacheLocation { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FIS", "PresentationRuntime", "Cache");
    public string BackupLocation { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FISPresents", "Backups");
    public bool BackupBeforeImport { get; set; } = true;
    public int BackupsToKeep { get; set; } = 5;
    public string RepositoryUrl { get; set; } = "";
    /// <summary>Folder (local or UNC share) holding repository.json, assignments.json and published packages.</summary>
    public string DemoRepositoryPath { get; set; } = @"C:\FISDemoRepository";
    public bool CheckOnLaunch { get; set; } = true;
    public bool PeriodicChecks { get; set; } = true;
    public int CheckIntervalMinutes { get; set; } = 60;
    public bool StartWithWindows { get; set; }
    /// <summary>Studio display size: 0 = Auto (fit to screen), otherwise a fixed scale between 0.7 and 1.4.</summary>
    public double UiScale { get; set; }
    /// <summary>Default for the "Track this session" switch on the demo start dialog.</summary>
    public bool TrackingEnabledByDefault { get; set; } = true;
    /// <summary>Upload pending usage events to the demo repository in the background after a successful repository check.</summary>
    public bool UploadUsage { get; set; } = true;
}

public sealed class ApplicationSettingsService
{
    public const string DeploymentFileName = "deployment.json";
    public string Root { get; }
    public string DeploymentDefaultsPath { get; }
    public static ApplicationSettings Current { get; private set; } = new();
    public ApplicationSettingsService(string? root = null, string? deploymentDefaultsPath = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FISPresents", "Settings");
        DeploymentDefaultsPath = deploymentDefaultsPath ?? Path.Combine(AppContext.BaseDirectory, DeploymentFileName);
    }

    public async Task<ApplicationSettings> LoadAsync()
    {
        var path = Path.Combine(Root, "settings.json");
        var settings = File.Exists(path) ? JsonSerializer.Deserialize<ApplicationSettings>(await File.ReadAllTextAsync(path))
            ?? throw new InvalidDataException("Settings are invalid.") : await LoadDeploymentDefaultsAsync();
        Validate(settings);
        Current = settings;
        return settings;
    }

    /// <summary>First-run defaults: deployment.json beside the exe (any subset of settings) seeds users who have no saved settings yet.</summary>
    private async Task<ApplicationSettings> LoadDeploymentDefaultsAsync()
    {
        if (!File.Exists(DeploymentDefaultsPath)) return new ApplicationSettings();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var defaults = JsonSerializer.Deserialize<ApplicationSettings>(await File.ReadAllTextAsync(DeploymentDefaultsPath), options)
            ?? throw new InvalidDataException($"{DeploymentFileName} is invalid.");
        foreach (var property in typeof(ApplicationSettings).GetProperties().Where(p => p.PropertyType == typeof(string)))
            if (property.GetValue(defaults) is string value) property.SetValue(defaults, Environment.ExpandEnvironmentVariables(value));
        return defaults;
    }

    public async Task SaveAsync(ApplicationSettings settings)
    {
        Validate(settings);
        foreach (var folder in new[] { settings.LibraryLocation, settings.CacheLocation, settings.BackupLocation, Root })
        {
            ContentFileSafety.EnsureNoReparseAncestors(folder);
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, ".fis-write-test-" + Guid.NewGuid().ToString("N"));
            try { await File.WriteAllTextAsync(probe, ""); } finally { if (File.Exists(probe)) File.Delete(probe); }
        }
        var path = Path.Combine(Root, "settings.json");
        var temporary = path + ".tmp";
        try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Current = settings;
    }

    public static void Validate(ApplicationSettings settings)
    {
        var folders = new[] { settings.LibraryLocation, settings.CacheLocation, settings.BackupLocation };
        if (folders.Any(folder => string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)))
            throw new InvalidDataException("Storage locations must be full folder paths.");
        var normalized = folders.Select(folder => Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).ToArray();
        for (var first = 0; first < normalized.Length; first++)
            for (var second = first + 1; second < normalized.Length; second++)
                if (normalized[first].StartsWith(normalized[second], StringComparison.OrdinalIgnoreCase) || normalized[second].StartsWith(normalized[first], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Library, cache and backup folders must be separate, not nested.");
        if (settings.BackupsToKeep is < 1 or > 50 || settings.CheckIntervalMinutes is < 5 or > 10080)
            throw new InvalidDataException("Keep 1–50 backups; update interval must be 5–10080 minutes.");
        if (!string.IsNullOrWhiteSpace(settings.DemoRepositoryPath))
        {
            if (!Path.IsPathFullyQualified(settings.DemoRepositoryPath))
                throw new InvalidDataException("Demo repository must be a full local or network folder path.");
            var repository = Path.GetFullPath(settings.DemoRepositoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (normalized.Any(folder => folder.StartsWith(repository, StringComparison.OrdinalIgnoreCase) || repository.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The demo repository must be separate from the local library, cache and backup folders.");
        }
        if (settings.UiScale != 0 && settings.UiScale is < 0.7 or > 1.4 || double.IsNaN(settings.UiScale))
            throw new InvalidDataException("Display size must be Auto or between 70% and 140%.");
        if (!string.IsNullOrWhiteSpace(settings.RepositoryUrl)
            && (!Uri.TryCreate(settings.RepositoryUrl, UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo.Length > 0))
            throw new InvalidDataException("Remote repository must be an HTTPS feed URL without embedded credentials.");
    }

    public static string CreateBackup(ApplicationSettings settings)
    {
        Validate(settings);
        ContentFileSafety.EnsureNoReparseAncestors(settings.LibraryLocation);
        ContentFileSafety.EnsureNoReparseAncestors(settings.BackupLocation);
        Directory.CreateDirectory(settings.BackupLocation);
        var destination = Path.Combine(settings.BackupLocation, "fis-library-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".zip");
        var temp = destination + ".building";
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                if (Directory.Exists(settings.LibraryLocation))
                    foreach (var file in ContentFileSafety.EnumerateSourceFiles(settings.LibraryLocation)
                        .Where(file => !Path.GetRelativePath(settings.LibraryLocation, file).StartsWith("staging" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                        archive.CreateEntryFromFile(file, Path.GetRelativePath(settings.LibraryLocation, file).Replace('\\', '/'), CompressionLevel.NoCompression);
            }
            File.Move(temp, destination);
            foreach (var old in new DirectoryInfo(settings.BackupLocation).GetFiles("fis-library-*.zip").OrderByDescending(file => file.LastWriteTimeUtc).Skip(settings.BackupsToKeep)) old.Delete();
            return destination;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static void SetWindowsStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (!enabled) { key.DeleteValue("FISPresents", false); return; }
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Application executable path is unavailable.");
        var command = "\"" + executable + "\"";
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            command += " \"" + typeof(ApplicationSettingsService).Assembly.Location + "\"";
        key.SetValue("FISPresents", command);
    }
}