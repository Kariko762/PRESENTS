using System.IO;

namespace FisPresentationPlatform.Services;

public static class ContentFileSafety
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".vs", "bin", "obj", "node_modules", "packages" };

    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Length > 400) return false;
        var invalid = Path.GetInvalidFileNameChars();
        return path.Replace('\\', '/').Split('/').All(part =>
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(invalid) >= 0) return false;
            var stem = part.Split('.')[0].ToUpperInvariant();
            return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
                && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                    && stem[3] is >= '1' and <= '9');
        });
    }

    public static void EnsureNoReparseAncestors(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((Directory.Exists(current) || File.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Content/cache paths must not contain symbolic links or junctions.");
            current = Path.GetDirectoryName(current);
        }
    }

    public static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        EnsureNoReparseAncestors(root);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0 && ExcludedDirectories.Contains(Path.GetFileName(entry))) continue;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Source content must not contain symbolic links or junctions.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else
                {
                    if (!IsSafeRelativePath(Path.GetRelativePath(root, entry)))
                        throw new InvalidDataException("Source content contains an unsafe Windows filename.");
                    yield return entry;
                }
            }
        }
    }
}