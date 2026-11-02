using System.IO;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace FisPresentationPlatform.Services;

public sealed record AssetReference(string SourceFile, string Reference, string Status, string? ResolvedPath);

public sealed partial class AssetReferenceScanner
{
    [GeneratedRegex("url\\(\\s*(['\\\"]?)(.*?)\\1\\s*\\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CssUrlPattern();

    public IReadOnlyList<AssetReference> Scan(string sourceDirectory)
    {
        var root = Path.GetFullPath(sourceDirectory);
        var references = new List<AssetReference>();
        var sourceFiles = ContentFileSafety.EnumerateSourceFiles(root)
            .Where(file => !IsExcluded(root, file))
            .Where(file => Path.GetExtension(file).Equals(".html", StringComparison.OrdinalIgnoreCase)
                           || Path.GetExtension(file).Equals(".htm", StringComparison.OrdinalIgnoreCase)
                           || Path.GetExtension(file).Equals(".css", StringComparison.OrdinalIgnoreCase)
                           || Path.GetExtension(file).Equals(".svg", StringComparison.OrdinalIgnoreCase));

        foreach (var sourceFile in sourceFiles)
        {
            var relativeSource = Path.GetRelativePath(root, sourceFile).Replace('\\', '/');
            var text = File.ReadAllText(sourceFile);
            if (Path.GetExtension(sourceFile).Equals(".css", StringComparison.OrdinalIgnoreCase))
            {
                AddCssReferences(root, sourceFile, relativeSource, text, references);
                continue;
            }

            var document = new HtmlParser().ParseDocument(text);
            foreach (var element in document.QuerySelectorAll("[src], [href], [poster], [srcset]"))
            {
                foreach (var attributeName in new[] { "src", "href", "poster" })
                {
                    var value = element.GetAttribute(attributeName);
                    if (!string.IsNullOrWhiteSpace(value)) AddReference(root, sourceFile, relativeSource, value, references);
                }

                var srcset = element.GetAttribute("srcset");
                if (!string.IsNullOrWhiteSpace(srcset))
                {
                    foreach (var candidate in srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var source = candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                        if (source is not null) AddReference(root, sourceFile, relativeSource, source, references);
                    }
                }

                var inlineStyle = element.GetAttribute("style");
                if (!string.IsNullOrWhiteSpace(inlineStyle)) AddCssReferences(root, sourceFile, relativeSource, inlineStyle, references);
            }

            foreach (var styleElement in document.QuerySelectorAll("style"))
            {
                if (styleElement.TextContent.Length > 0)
                    AddCssReferences(root, sourceFile, relativeSource, styleElement.TextContent, references);
            }
        }

        return references;
    }

    private static void AddCssReferences(string root, string sourceFile, string relativeSource, string css, List<AssetReference> references)
    {
        foreach (Match match in CssUrlPattern().Matches(css))
        {
            var value = match.Groups[2].Value.Trim();
            if (value.Length > 0) AddReference(root, sourceFile, relativeSource, value, references);
        }
    }

    private static void AddReference(string root, string sourceFile, string relativeSource, string value, List<AssetReference> references)
    {
        var reference = value.Trim().Trim('"', '\'');
        if (reference.Length == 0 || reference.StartsWith('#') || IsNonFileScheme(reference)) return;

        if (reference.StartsWith("//", StringComparison.Ordinal)
            || (Uri.TryCreate(reference, UriKind.Absolute, out var absolute) && !absolute.IsFile))
        {
            references.Add(new AssetReference(relativeSource, reference, "External", null));
            return;
        }

        var localPart = reference.Split('?', '#')[0];
        if (localPart.Length == 0) return;
        try
        {
            var packageRootRelative = localPart.StartsWith("/", StringComparison.Ordinal);
            localPart = Uri.UnescapeDataString(localPart).TrimStart('/');
            var referenceRoot = packageRootRelative ? root : Path.GetDirectoryName(sourceFile)!;
            var candidate = Path.GetFullPath(Path.Combine(referenceRoot, localPart.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                references.Add(new AssetReference(relativeSource, reference, "Unsafe path", null));
                return;
            }

            references.Add(new AssetReference(relativeSource, reference, File.Exists(candidate) ? "Local" : "Missing",
                Path.GetRelativePath(root, candidate).Replace('\\', '/')));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UriFormatException or NotSupportedException)
        {
            references.Add(new AssetReference(relativeSource, reference, "Invalid path", null));
        }
    }

    private static bool IsNonFileScheme(string reference) =>
        reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("blob:", StringComparison.OrdinalIgnoreCase);

    private static bool IsExcluded(string root, string file) =>
        Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase)
                         || part.Equals(".vs", StringComparison.OrdinalIgnoreCase)
                         || part.Equals("bin", StringComparison.OrdinalIgnoreCase)
                         || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
                         || part.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                         || part.Equals("packages", StringComparison.OrdinalIgnoreCase));
}
