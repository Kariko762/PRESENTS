using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FisPresentationPlatform.Services;

public sealed class WelcomeFeed
{
    public string SchemaVersion { get; set; } = "1.0";
    public List<WelcomeFeedItem> Items { get; set; } = [];
}

public sealed class WelcomeFeedItem
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "news";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Body { get; set; } = "";
    public string DemoId { get; set; } = "";
    public DateTimeOffset PublishedUtc { get; set; }
    public string Product { get; set; } = "";
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    [JsonIgnore] public string TypeLabel => Kind switch { "demo" => "NEW DEMO", "update" => "DEMO UPDATE", _ => "NEWS" };
    [JsonIgnore] public string DateLabel => PublishedUtc.LocalDateTime.ToString("dd MMM yyyy");
    [JsonIgnore] public string Metadata => string.Join(" · ", new[] { Product, Version }.Where(value => !string.IsNullOrWhiteSpace(value)));
    [JsonIgnore] public bool HasDemoLink => !string.IsNullOrEmpty(DemoId);
}

public sealed record WelcomeFeedResult(IReadOnlyList<WelcomeFeedItem> Items, string Source, string Message, DateTimeOffset? CachedUtc);

public sealed class WelcomeFeedService : IDisposable
{
    private const int MaximumBytes = 1024 * 1024;
    private readonly HttpClient _http;
    private readonly string _root;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public WelcomeFeedService(string? root = null, HttpMessageHandler? handler = null)
    {
        _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FISPresents", "Welcome");
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
    }

    public async Task<string> LoadUrlAsync() => File.Exists(Path.Combine(_root, "feed-url.txt"))
        ? (await File.ReadAllTextAsync(Path.Combine(_root, "feed-url.txt"))).Trim() : "";

    public async Task SaveUrlAsync(string url)
    {
        if (!string.IsNullOrWhiteSpace(url)) ValidateUrl(url);
        ContentFileSafety.EnsureNoReparseAncestors(_root);
        Directory.CreateDirectory(_root);
        await AtomicWriteAsync(Path.Combine(_root, "feed-url.txt"), url.Trim());
    }

    public async Task<WelcomeFeedResult> RefreshAsync(string url, CancellationToken cancellationToken = default, bool checkRemote = true)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new WelcomeFeedResult([], "Not configured", "Set an approved HTTPS feed address to receive news, new demos and updates.", null);
        var uri = ValidateUrl(url);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant();
        var cachePath = Path.Combine(_root, key + ".json");
        try
        {
            if (!checkRemote) throw new HttpRequestException("Remote checks disabled; use local cache.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumBytes) throw new InvalidDataException("Welcome feed exceeds the 1 MiB limit.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (bytes.Length + count > MaximumBytes) throw new InvalidDataException("Welcome feed exceeds the 1 MiB limit.");
                bytes.Write(buffer, 0, count);
            }
            var feed = Parse(Encoding.UTF8.GetString(bytes.ToArray()));
            var cached = new CachedFeed { RetrievedUtc = DateTimeOffset.UtcNow, Feed = feed };
            string message = "Latest published content from the remote feed.";
            try
            {
                ContentFileSafety.EnsureNoReparseAncestors(_root);
                Directory.CreateDirectory(_root);
                await AtomicWriteAsync(cachePath, JsonSerializer.Serialize(cached, JsonOptions));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { message += " Local caching could not be saved."; }
            return new WelcomeFeedResult(feed.Items.OrderByDescending(item => item.PublishedUtc).ToList(), "Live", message, cached.RetrievedUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException or UnauthorizedAccessException)
        {
            try
            {
                ContentFileSafety.EnsureNoReparseAncestors(cachePath);
                if (File.Exists(cachePath))
                {
                    if (new FileInfo(cachePath).Length > MaximumBytes * 2) throw new InvalidDataException("Cached feed exceeds size limit.");
                    var cached = JsonSerializer.Deserialize<CachedFeed>(await File.ReadAllTextAsync(cachePath, cancellationToken), JsonOptions)
                        ?? throw new InvalidDataException("Invalid cached welcome feed.");
                    var feed = Parse(JsonSerializer.Serialize(cached.Feed, JsonOptions));
                    return new WelcomeFeedResult(feed.Items.OrderByDescending(item => item.PublishedUtc).ToList(), "Cached",
                        "Remote feed unavailable or invalid. Showing the last valid local copy.", cached.RetrievedUtc);
                }
            }
            catch (Exception cacheError) when (cacheError is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { }
            return new WelcomeFeedResult([], "Unavailable", "Remote feed unavailable and no valid cache is saved for this address.", null);
        }
    }

    public async Task<WelcomeFeedResult> LoadSampleAsync()
    {
        using var stream = typeof(WelcomeFeedService).Assembly.GetManifestResourceStream("FIS.welcome.sample.json")
            ?? throw new InvalidDataException("Bundled sample news is missing.");
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        var feed = Parse(json);
        // Keep preview content separate from source-specific live-feed caches.
        try
        {
            ContentFileSafety.EnsureNoReparseAncestors(_root);
            Directory.CreateDirectory(_root);
            await AtomicWriteAsync(Path.Combine(_root, "sample-news.json"), json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return new WelcomeFeedResult(feed.Items.OrderByDescending(item => item.PublishedUtc).ToList(), "Sample preview",
            "Bundled offline sample posts — design preview, not official announcements.", null);
    }

    public static WelcomeFeed Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("schemaVersion", out _)
            || !document.RootElement.TryGetProperty("items", out _))
            throw new JsonException("Welcome feed requires schemaVersion and items.");
        var feed = JsonSerializer.Deserialize<WelcomeFeed>(json, JsonOptions) ?? throw new JsonException("Missing welcome feed.");
        if (feed.SchemaVersion != "1.0" || feed.Items is null || feed.Items.Count > 200) throw new JsonException("Unsupported or oversized welcome feed.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in feed.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 80 || !ids.Add(item.Id)
                || item.Kind is not ("news" or "demo" or "update") || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 160
                || item.Summary is null || item.Summary.Length > 2000 || item.Product is null || item.Product.Length > 100
                || item.Version is null || item.Version.Length > 80 || item.Url is null || item.Url.Length > 2048 || item.PublishedUtc == default)
                throw new JsonException("Welcome feed contains an invalid news/demo/update item.");
            if (item.Body is null || item.Body.Length > 12000 || item.DemoId is null
                || (item.DemoId.Length > 0 && (!ContentFileSafety.IsSafeRelativePath(item.DemoId)
                    || !System.Text.RegularExpressions.Regex.IsMatch(item.DemoId, "^[a-zA-Z0-9][a-zA-Z0-9._-]{1,79}$"))))
                throw new JsonException("Welcome post body or internal demo ID is invalid.");
            if (item.Url.Length > 0) ValidateUrl(item.Url);
        }
        return feed;
    }

    private static Uri ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0)
            throw new InvalidDataException("Use an approved HTTPS feed address without embedded credentials.");
        return uri;
    }

    private static async Task AtomicWriteAsync(string path, string value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, value); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose() => _http.Dispose();
    private sealed class CachedFeed
    {
        public DateTimeOffset RetrievedUtc { get; set; }
        public WelcomeFeed Feed { get; set; } = new();
    }
}