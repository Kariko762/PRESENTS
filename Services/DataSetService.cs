using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FisPresentationPlatform.Models;

namespace FisPresentationPlatform.Services;

public enum DataSetCreateMode
{
    /// <summary>Full standalone copy of the template's JSON (and its private <c>$files</c>).</summary>
    Duplicate,
    /// <summary>Small file containing <c>$extends</c> → template file; edit only what differs.</summary>
    Override,
    /// <summary>Empty object; the author fills it in.</summary>
    Blank
}

public sealed record DataSetHealth(
    string Id,
    long Bytes,
    int Fields,
    int Tables,
    int Rows,
    IReadOnlyList<string> Chain,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool Ok => Errors.Count == 0;
}

public sealed record DataSetComparison(IReadOnlyList<string> Missing, IReadOnlyList<string> Extra, IReadOnlyList<string> TypeChanged)
{
    public bool Matches => Missing.Count == 0 && TypeChanged.Count == 0;
}

/// <summary>Authoring operations behind the Configuration Studio "Data sets" tab. All paths are relative to the presentation folder.</summary>
public static class DataSetService
{
    public const string DataFolder = "data";

    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static DataSetHealth Inspect(string contentRoot, DemoDataSet dataSet)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(dataSet.File))
            return new DataSetHealth(dataSet.Id, 0, 0, 0, 0, [], new Dictionary<string, string>(), [], ["No data file — pages only receive the name and persona."]);
        var resolved = DataSetResolver.Resolve(contentRoot, dataSet.File);
        int fields = 0, tables = 0, rows = 0;
        if (resolved.Data is not null)
        {
            foreach (var (path, kind) in FlattenPaths(resolved.Data))
            {
                if (kind == "array") tables++; else if (kind != "object") fields++;
            }
            rows = CountRows(resolved.Data);
            if (resolved.Bytes > DataSetLimits.RecommendedInlineBytes)
                warnings.Add($"{DataSetResolver.FormatBytes(resolved.Bytes)} is injected into every page; above {DataSetResolver.FormatBytes(DataSetLimits.RecommendedInlineBytes)} consider moving large tables to $files.");
        }
        foreach (var (name, path) in resolved.Files)
        {
            var length = new FileInfo(DataSetResolver.Combine(contentRoot, path)).Length;
            if (length > 25L * 1024 * 1024) warnings.Add($"$files.{name} is {DataSetResolver.FormatBytes(length)}; files over 25 MB parse slowly in the browser.");
        }
        return new DataSetHealth(dataSet.Id, resolved.Bytes, fields, tables, rows, resolved.Chain, resolved.Files, resolved.Errors, warnings);
    }

    /// <summary>Flattens a JSON tree into dotted paths. Arrays of objects are described by their first element as <c>path[]</c>.</summary>
    public static Dictionary<string, string> FlattenPaths(JsonNode? node)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(node, "", result, 0);
        return result;
    }

    public static DataSetComparison Compare(JsonNode? baseline, JsonNode? candidate)
    {
        var expected = FlattenPaths(baseline);
        var actual = FlattenPaths(candidate);
        var missing = expected.Keys.Where(key => !actual.ContainsKey(key) && !IsCoveredByEmptyArray(key, actual)).Order().ToList();
        var extra = actual.Keys.Where(key => !expected.ContainsKey(key)).Order().ToList();
        var changed = expected.Where(pair => actual.TryGetValue(pair.Key, out var kind) && kind != pair.Value && kind != "null" && pair.Value != "null")
            .Select(pair => $"{pair.Key} ({pair.Value} → {actual[pair.Key]})").Order().ToList();
        return new DataSetComparison(missing, extra, changed);
    }

    public static string SuggestId(PresentationConfig config, string name)
    {
        var stem = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (stem.Length == 0) stem = "data-set";
        if (stem.Length > 60) stem = stem[..60].Trim('-');
        var candidate = stem;
        for (var index = 2; config.DataSets.Any(set => set.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase)); index++) candidate = $"{stem}-{index}";
        return candidate;
    }

    public static bool IsValidId(string id) => Regex.IsMatch(id ?? "", "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$", RegexOptions.CultureInvariant);

    /// <summary>Creates a new data set from a template (or blank), writes its file into <c>data/</c> and appends it to the config.</summary>
    public static DemoDataSet Create(string contentRoot, PresentationConfig config, string? templateId, string id, string name, string persona, DataSetCreateMode mode)
    {
        EnsureNewId(config, id);
        var template = templateId is null ? null : config.DataSets.FirstOrDefault(set => set.Id.Equals(templateId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Template data set '{templateId}' was not found.");
        if (mode != DataSetCreateMode.Blank && string.IsNullOrWhiteSpace(template?.File))
            mode = DataSetCreateMode.Blank;

        var relative = UniqueFile(contentRoot, $"{DataFolder}/{id}.json");
        JsonObject body;
        switch (mode)
        {
            case DataSetCreateMode.Override:
                body = new JsonObject { [DataSetLimits.ExtendsKey] = template!.File };
                if (!string.IsNullOrWhiteSpace(persona)) body["persona"] = persona.Trim();
                break;
            case DataSetCreateMode.Duplicate:
                var errors = new List<string>();
                body = DataSetResolver.ReadObject(contentRoot, template!.File, errors) ?? throw new InvalidDataException(string.Join(" ", errors));
                CopyPrivateFiles(contentRoot, body, template.File, id);
                break;
            default:
                body = new JsonObject();
                if (!string.IsNullOrWhiteSpace(persona)) body["persona"] = persona.Trim();
                break;
        }
        WriteJson(contentRoot, relative, body);
        var created = new DemoDataSet
        {
            Id = id,
            Name = name.Trim(),
            Persona = persona.Trim(),
            Description = template is null ? "" : $"Based on {template.Name}.",
            File = relative,
            Default = config.DataSets.Count == 0
        };
        config.DataSets.Add(created);
        return created;
    }

    /// <summary>Copies an external JSON file into <c>data/</c> as a new data set after checking it.</summary>
    public static DemoDataSet Import(string contentRoot, PresentationConfig config, string externalFile, string id, string name, string persona)
    {
        EnsureNewId(config, id);
        var info = new FileInfo(externalFile);
        if (info.Length > DataSetLimits.MaxInlineBytes)
            throw new InvalidDataException($"{info.Name} is {DataSetResolver.FormatBytes(info.Length)}. Data set files are limited to {DataSetResolver.FormatBytes(DataSetLimits.MaxInlineBytes)}; put large tables in a CSV and reference it from $files.");
        JsonNode? node;
        try { node = JsonNode.Parse(File.ReadAllText(externalFile), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException exception) { throw new InvalidDataException($"{info.Name} is not valid JSON: {exception.Message}"); }
        if (node is not JsonObject body) throw new InvalidDataException($"{info.Name} must contain a JSON object at the top level.");
        if (body.ContainsKey(DataSetLimits.ExtendsKey) || body.ContainsKey(DataSetLimits.FilesKey))
            throw new InvalidDataException("Imported files cannot use $extends or $files; copy referenced files into the demo and add those keys in the editor.");
        var relative = UniqueFile(contentRoot, $"{DataFolder}/{id}.json");
        WriteJson(contentRoot, relative, body);
        var created = new DemoDataSet { Id = id, Name = name.Trim(), Persona = persona.Trim(), File = relative, Default = config.DataSets.Count == 0 };
        config.DataSets.Add(created);
        return created;
    }

    /// <summary>Removes a data set. Its file is deleted only when nothing else (another set or an <c>$extends</c>) still uses it.</summary>
    public static IReadOnlyList<string> Remove(string contentRoot, PresentationConfig config, string id)
    {
        var target = config.DataSets.FirstOrDefault(set => set.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Data set '{id}' was not found.");
        var dependants = DependantsOf(contentRoot, config, target);
        if (dependants.Count > 0)
            throw new InvalidOperationException($"'{target.Name}' is the base for {string.Join(", ", dependants)}. Remove or re-point those first.");
        config.DataSets.Remove(target);
        if (target.Default && config.DataSets.Count > 0 && !config.DataSets.Any(set => set.Default)) config.DataSets[0].Default = true;
        var deleted = new List<string>();
        if (string.IsNullOrWhiteSpace(target.File) || config.DataSets.Any(set => SamePath(set.File, target.File))) return deleted;
        var path = DataSetResolver.Combine(contentRoot, target.File);
        if (File.Exists(path))
        {
            File.Delete(path);
            deleted.Add(target.File);
        }
        return deleted;
    }

    /// <summary>Data sets whose <c>$extends</c> chain passes through the given set's file.</summary>
    public static List<string> DependantsOf(string contentRoot, PresentationConfig config, DemoDataSet target)
    {
        if (string.IsNullOrWhiteSpace(target.File)) return [];
        return config.DataSets
            .Where(set => !ReferenceEquals(set, target) && !string.IsNullOrWhiteSpace(set.File) && !SamePath(set.File, target.File))
            .Where(set => DataSetResolver.Resolve(contentRoot, set.File).Chain.Skip(1).Any(file => SamePath(file, target.File)))
            .Select(set => set.Name)
            .ToList();
    }

    public static string ReadText(string contentRoot, string file) => File.ReadAllText(DataSetResolver.Combine(contentRoot, file));

    /// <summary>Validates and saves raw JSON text for a data file, keeping the author's formatting (compact rows stay compact).</summary>
    public static string SaveText(string contentRoot, string file, string json)
    {
        if (!ContentFileSafety.IsSafeRelativePath(file) || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Data file must be a safe package-relative .json path.");
        Parse(json);
        var text = json.Replace("\r\n", "\n").TrimEnd() + "\n";
        if (System.Text.Encoding.UTF8.GetByteCount(text) > DataSetLimits.MaxInlineBytes)
            throw new InvalidDataException($"Data files are limited to {DataSetResolver.FormatBytes(DataSetLimits.MaxInlineBytes)}. Move large tables to $files.");
        var path = DataSetResolver.Combine(contentRoot, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return text.Replace("\n", Environment.NewLine);
    }

    public static JsonObject Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                ?? throw new InvalidDataException("A data set must be a JSON object ({ … }) at the top level.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid JSON (line {exception.LineNumber + 1}, position {exception.BytePositionInLine + 1}): {exception.Message.Split(" Path:")[0]}");
        }
    }

    private static void CopyPrivateFiles(string contentRoot, JsonObject body, string templateFile, string id)
    {
        if (body[DataSetLimits.FilesKey] is not JsonObject files) return;
        foreach (var (name, node) in files.ToList())
        {
            if (node is not JsonValue value || !value.TryGetValue<string>(out var source) || !ContentFileSafety.IsSafeRelativePath(source)) continue;
            var sourcePath = DataSetResolver.Combine(contentRoot, source);
            if (!File.Exists(sourcePath)) continue;
            var target = UniqueFile(contentRoot, $"{DataFolder}/{id}/{Path.GetFileName(source)}");
            var targetPath = DataSetResolver.Combine(contentRoot, target);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath);
            files[name] = target;
        }
    }

    private static void WriteJson(string contentRoot, string relative, JsonObject body)
    {
        var path = DataSetResolver.Combine(contentRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, body.ToJsonString(WriteOptions) + Environment.NewLine);
    }

    private static string UniqueFile(string contentRoot, string relative)
    {
        var directory = Path.GetDirectoryName(relative.Replace('/', Path.DirectorySeparatorChar))!.Replace(Path.DirectorySeparatorChar, '/');
        var stem = Path.GetFileNameWithoutExtension(relative);
        var extension = Path.GetExtension(relative);
        var candidate = relative;
        for (var index = 2; File.Exists(DataSetResolver.Combine(contentRoot, candidate)); index++) candidate = $"{directory}/{stem}-{index}{extension}";
        return candidate;
    }

    private static void EnsureNewId(PresentationConfig config, string id)
    {
        if (!IsValidId(id)) throw new InvalidOperationException("Data set ids use letters, numbers, '.', '_' or '-' (max 80) and start with a letter or number.");
        if (config.DataSets.Any(set => set.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A data set with id '{id}' already exists.");
    }

    private static bool SamePath(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && left.Replace('\\', '/').Equals(right.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static void Walk(JsonNode? node, string path, Dictionary<string, string> result, int depth)
    {
        if (depth > 12) return;
        switch (node)
        {
            case JsonObject obj:
                if (path.Length > 0) result[path] = "object";
                foreach (var (key, child) in obj)
                {
                    if (key == DataSetLimits.ExtendsKey) continue;
                    Walk(child, path.Length == 0 ? key : $"{path}.{key}", result, depth + 1);
                }
                break;
            case JsonArray array:
                result[path] = "array";
                var sample = array.FirstOrDefault(item => item is not null);
                if (sample is JsonObject or JsonArray) Walk(sample, path + "[]", result, depth + 1);
                break;
            case JsonValue value:
                result[path] = value.GetValueKind() switch
                {
                    JsonValueKind.String => "string",
                    JsonValueKind.Number => "number",
                    JsonValueKind.True or JsonValueKind.False => "boolean",
                    _ => "null"
                };
                break;
            default:
                result[path] = "null";
                break;
        }
    }

    private static bool IsCoveredByEmptyArray(string key, Dictionary<string, string> actual)
    {
        var index = key.IndexOf("[]", StringComparison.Ordinal);
        while (index >= 0)
        {
            var prefix = key[..index];
            if (actual.TryGetValue(prefix, out var kind) && kind == "array" && !actual.Keys.Any(other => other.StartsWith(prefix + "[]", StringComparison.Ordinal))) return true;
            index = key.IndexOf("[]", index + 2, StringComparison.Ordinal);
        }
        return false;
    }

    private static int CountRows(JsonNode? node, int depth = 0)
    {
        if (depth > 12) return 0;
        return node switch
        {
            JsonArray array => array.Count + array.Sum(item => item is JsonObject ? CountRowsNested(item, depth + 1) : 0),
            JsonObject obj => obj.Sum(pair => CountRows(pair.Value, depth + 1)),
            _ => 0
        };
    }

    private static int CountRowsNested(JsonNode node, int depth) =>
        node is JsonObject obj ? obj.Where(pair => pair.Value is JsonArray).Sum(pair => CountRows(pair.Value, depth + 1)) : 0;
}
