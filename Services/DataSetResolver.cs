using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FisPresentationPlatform.Services;

/// <summary>Size budgets for data sets. Inline data is injected into every page, so it is deliberately bounded.</summary>
public static class DataSetLimits
{
    /// <summary>Hard limit for the merged inline data injected as <c>FIS_DEMO_CONTEXT.data</c> (and for each data file).</summary>
    public const long MaxInlineBytes = 1024 * 1024;
    /// <summary>Above this the Studio warns: move large tables to <c>$files</c> so they load on demand.</summary>
    public const long RecommendedInlineBytes = 256 * 1024;
    /// <summary>Maximum length of an <c>$extends</c> chain (data set → base → base …).</summary>
    public const int MaxExtendsDepth = 4;
    public const string ExtendsKey = "$extends";
    public const string FilesKey = "$files";
}

/// <summary>A data set after <c>$extends</c> merging. <see cref="Data"/> is what pages receive.</summary>
public sealed record ResolvedDataSet(
    JsonObject? Data,
    long Bytes,
    IReadOnlyList<string> Chain,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyList<string> Errors)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Loads a data set file, follows its <c>$extends</c> chain, deep-merges it and checks <c>$files</c> references.
/// Shared by validation, the Player (what is injected) and the Configuration Studio (what is shown).
/// </summary>
public static class DataSetResolver
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static ResolvedDataSet Resolve(string contentRoot, string? file)
    {
        var errors = new List<string>();
        var chain = new List<string>();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(file)) return new ResolvedDataSet(null, 0, chain, files, errors);

        var layers = new List<JsonObject>();
        var current = file;
        while (current is not null)
        {
            if (chain.Contains(current, StringComparer.OrdinalIgnoreCase)) { errors.Add($"$extends loops back to {current}."); break; }
            if (chain.Count >= DataSetLimits.MaxExtendsDepth) { errors.Add($"$extends chain is deeper than {DataSetLimits.MaxExtendsDepth} files."); break; }
            chain.Add(current);
            var layer = ReadObject(contentRoot, current, errors);
            if (layer is null) break;
            layers.Add(layer);
            current = null;
            if (layer[DataSetLimits.ExtendsKey] is JsonNode extendsNode)
            {
                if (extendsNode is JsonValue value && value.TryGetValue<string>(out var basePath) && !string.IsNullOrWhiteSpace(basePath)) current = basePath;
                else errors.Add($"{chain[^1]}: $extends must be a package-relative .json path.");
            }
        }
        if (errors.Count > 0) return new ResolvedDataSet(null, 0, chain, files, errors);

        // Base first, then each override on top.
        var merged = new JsonObject();
        for (var index = layers.Count - 1; index >= 0; index--) Merge(merged, layers[index]);
        merged.Remove(DataSetLimits.ExtendsKey);

        if (merged[DataSetLimits.FilesKey] is JsonNode filesNode)
        {
            if (filesNode is not JsonObject map) errors.Add("$files must be an object of name → package-relative file path.");
            else
                foreach (var (name, node) in map)
                {
                    if (node is not JsonValue pathValue || !pathValue.TryGetValue<string>(out var path) || !ContentFileSafety.IsSafeRelativePath(path))
                    { errors.Add($"$files.{name} must be a safe package-relative path."); continue; }
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension is not (".json" or ".csv" or ".tsv" or ".txt"))
                    { errors.Add($"$files.{name} must be a .json, .csv, .tsv or .txt file: {path}"); continue; }
                    if (!File.Exists(Combine(contentRoot, path))) { errors.Add($"$files.{name} not found: {path}"); continue; }
                    files[name] = path.Replace('\\', '/');
                }
        }

        var bytes = Encoding.UTF8.GetByteCount(merged.ToJsonString());
        if (bytes > DataSetLimits.MaxInlineBytes)
            errors.Add($"Merged data is {FormatBytes(bytes)}; the inline limit is {FormatBytes(DataSetLimits.MaxInlineBytes)}. Move large tables to $files.");
        return new ResolvedDataSet(errors.Count == 0 ? merged : null, bytes, chain, files, errors);
    }

    /// <summary>Deep merge: objects merge key by key, arrays and values replace, and <c>null</c> removes a key.</summary>
    public static void Merge(JsonObject target, JsonObject layer)
    {
        foreach (var (key, value) in layer)
        {
            if (value is null) { target.Remove(key); continue; }
            if (value is JsonObject child && target[key] is JsonObject existing) { Merge(existing, child); continue; }
            target[key] = value.DeepClone();
        }
    }

    /// <summary>Reads one data file as a JSON object, applying path, size and syntax checks.</summary>
    public static JsonObject? ReadObject(string contentRoot, string file, List<string> errors)
    {
        if (!ContentFileSafety.IsSafeRelativePath(file) || !Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
        { errors.Add($"Data file must be a safe package-relative .json path: {file}"); return null; }
        var path = Combine(contentRoot, file);
        var info = new FileInfo(path);
        if (!info.Exists) { errors.Add($"Data set file not found: {file}"); return null; }
        if (info.Length > DataSetLimits.MaxInlineBytes)
        { errors.Add($"{file} is {FormatBytes(info.Length)}; data files are limited to {FormatBytes(DataSetLimits.MaxInlineBytes)}. Move large tables to $files."); return null; }
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) is JsonObject root) return root;
            errors.Add($"{file} must contain a JSON object ({{ … }}) at the top level.");
        }
        catch (JsonException exception)
        {
            errors.Add($"{file} is not valid JSON (line {exception.LineNumber + 1}): {exception.Message.Split(" Path:")[0]}");
        }
        return null;
    }

    public static string Combine(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Data paths must stay inside the presentation folder.");
        return full;
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.##} MB"
    };
}
