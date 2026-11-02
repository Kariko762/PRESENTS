using System.IO;

namespace FisPresentationPlatform.Services;

public sealed record AssetInventoryItem(string RelativePath, string Kind, long SizeBytes, string Extension)
{
    public string SizeLabel => SizeBytes >= 1024 * 1024
        ? $"{SizeBytes / 1024d / 1024d:F1} MB"
        : SizeBytes >= 1024 ? $"{SizeBytes / 1024d:F0} KB" : $"{SizeBytes} B";
}

public sealed class AssetInventoryService
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", "packages"
    };

    public IReadOnlyList<AssetInventoryItem> Scan(string sourceDirectory)
    {
        return ContentFileSafety.EnumerateSourceFiles(sourceDirectory)
            .Where(file => !IsExcluded(sourceDirectory, file))
            .Select(file =>
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                return new AssetInventoryItem(
                    Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/'),
                    Classify(extension),
                    new FileInfo(file).Length,
                    extension);
            })
            .OrderBy(item => item.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Classify(string extension) => extension switch
    {
        ".html" or ".htm" => "HTML page",
        ".css" => "Stylesheet",
        ".js" or ".mjs" => "JavaScript",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".bmp" or ".avif" => "Image",
        ".mp4" or ".webm" or ".mov" or ".m4v" or ".ogv" => "Video",
        ".mp3" or ".wav" or ".ogg" or ".m4a" => "Audio",
        ".woff" or ".woff2" or ".ttf" or ".otf" => "Font",
        ".json" => "Configuration",
        _ => "Other"
    };

    private static bool IsExcluded(string root, string file) =>
        Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => ExcludedDirectories.Contains(part));
}
