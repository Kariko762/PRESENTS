using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FisPresentationPlatform.Models;

namespace FisPresentationPlatform.Services;

/// <summary>What the consultant entered on the demo start dialog.</summary>
public sealed class DemoLaunchContext
{
    public bool Track { get; set; } = true;
    public string Reason { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string OpportunityId { get; set; } = "";
    public DemoDataSet? DataSet { get; set; }
}

public sealed class UsageEvent
{
    public string Type { get; set; } = "";
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string SessionId { get; set; } = "";
    public string User { get; set; } = DemoRepositoryService.CurrentUser;
    public string Machine { get; set; } = Environment.MachineName;
    public string AppVersion { get; set; } = UsageTrackingService.AppVersion;
    public string? DemoId { get; set; }
    public string? DemoName { get; set; }
    public string? DemoVersion { get; set; }
    public OrganizationConfig? Organization { get; set; }
    public string? DataSet { get; set; }
    public string? Persona { get; set; }
    public string? Reason { get; set; }
    public string? ClientName { get; set; }
    public string? OpportunityId { get; set; }
    public string? PageId { get; set; }
    public string? PageTitle { get; set; }
    public double? DurationSeconds { get; set; }
}

/// <summary>Summary of one tracked demo session for the Settings › Usage Tracking view.</summary>
public sealed record UsageSessionSummary(string SessionId, DateTimeOffset StartedUtc, string DemoName, string DemoVersion, string ClientName,
    string OpportunityId, string Reason, string Persona, int Pages, double? DurationSeconds, bool Uploaded)
{
    public string StartedLabel => StartedUtc.LocalDateTime.ToString("g");
    public string DealLabel => string.Join(" · ", new[] { ClientName, OpportunityId }.Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } deal ? deal : "No deal data";
    public string DurationLabel => DurationSeconds is null ? (Uploaded ? "Not closed" : "In progress") : TimeSpan.FromSeconds(DurationSeconds.Value) is var span && span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    public string StateLabel => Uploaded ? "Uploaded" : "Pending";
}

public sealed record UsageStats(int TrackedSessions, int PendingFiles, int UploadedFiles, DateTimeOffset? LastUploadUtc);

/// <summary>
/// Records usage locally as JSON Lines (one file per app run or demo session) under <c>&lt;library&gt;\usage\pending</c>.
/// Closed files are copied to <c>&lt;repository&gt;\usage\&lt;user&gt;\</c> and then moved to <c>usage\uploaded</c>.
/// Nothing about a demo session is recorded when the consultant switches tracking off.
/// </summary>
public sealed class UsageTrackingService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly Lock _gate = new();
    private readonly HashSet<string> _activeFiles = new(StringComparer.OrdinalIgnoreCase);

    public static string AppVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public UsageTrackingService(string libraryRoot)
    {
        Root = Path.Combine(libraryRoot, "usage");
        PendingFolder = Path.Combine(Root, "pending");
        UploadedFolder = Path.Combine(Root, "uploaded");
    }

    public string Root { get; }
    public string PendingFolder { get; }
    public string UploadedFolder { get; }

    public void RecordAppStart()
    {
        var id = "app-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        Append(id, new UsageEvent { Type = "app_start", SessionId = id });
        AppLog.Info("Usage", "Recorded app start.");
    }

    /// <summary>Starts a demo session. Returns <c>null</c> when the consultant chose not to track it.</summary>
    public UsageSession? StartSession(PresentationConfig config, DemoLaunchContext context)
    {
        if (!context.Track) { AppLog.Info("Usage", $"Untracked session started for '{config.Name}'."); return null; }
        var id = "demo-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        lock (_gate) _activeFiles.Add(FileFor(id));
        var session = new UsageSession(this, id, config, context);
        Append(id, session.Event("demo_session_start"));
        AppLog.Info("Usage", $"Tracked session started for '{config.Name}' v{config.Version}" + (string.IsNullOrWhiteSpace(context.ClientName) ? "." : $" · {context.ClientName}."));
        return session;
    }

    internal void Append(string sessionId, UsageEvent usageEvent)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(PendingFolder);
                File.AppendAllText(FileFor(sessionId), JsonSerializer.Serialize(usageEvent, Json) + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("Usage", "Could not record a usage event.", exception);
            }
        }
    }

    internal void Close(string sessionId) { lock (_gate) _activeFiles.Remove(FileFor(sessionId)); }

    private string FileFor(string sessionId) => Path.Combine(PendingFolder, sessionId + ".jsonl");

    /// <summary>Copies closed pending files to the repository usage folder for the current user, then archives them locally.</summary>
    public async Task<int> UploadAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(repositoryRoot)) throw new DirectoryNotFoundException($"Demo repository not found: {repositoryRoot}");
        if (!Directory.Exists(PendingFolder)) return 0;
        string[] files;
        lock (_gate) files = Directory.GetFiles(PendingFolder, "*.jsonl").Where(file => !_activeFiles.Contains(file)).ToArray();
        if (files.Length == 0) return 0;
        var target = Path.Combine(repositoryRoot, "usage", SafeUserFolder(DemoRepositoryService.CurrentUser));
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(UploadedFolder);
        var uploaded = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Environment.MachineName + "-" + Path.GetFileName(file);
            await using (var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true))
            await using (var destination = new FileStream(Path.Combine(target, name + ".tmp"), FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await source.CopyToAsync(destination, cancellationToken);
            File.Move(Path.Combine(target, name + ".tmp"), Path.Combine(target, name), overwrite: true);
            File.Move(file, Path.Combine(UploadedFolder, Path.GetFileName(file)), overwrite: true);
            uploaded++;
        }
        AppLog.Info("Usage", $"Uploaded {uploaded} usage file(s) to {target}.");
        return uploaded;
    }

    public static string SafeUserFolder(string user)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(user.Select(character => invalid.Contains(character) || character == '\\' ? '_' : character).ToArray()).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }

    public UsageStats GetStats()
    {
        var pending = Files(PendingFolder);
        var uploaded = Files(UploadedFolder);
        var sessions = pending.Concat(uploaded).Count(file => Path.GetFileName(file).StartsWith("demo-", StringComparison.OrdinalIgnoreCase));
        DateTimeOffset? last = uploaded.Count == 0 ? null : uploaded.Max(file => new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero));
        return new UsageStats(sessions, pending.Count, uploaded.Count, last);
    }

    public IReadOnlyList<UsageSessionSummary> LoadSessions(int limit = 200)
    {
        var results = new List<UsageSessionSummary>();
        foreach (var (folder, uploaded) in new[] { (PendingFolder, false), (UploadedFolder, true) })
        foreach (var file in Files(folder).Where(file => Path.GetFileName(file).StartsWith("demo-", StringComparison.OrdinalIgnoreCase)))
        {
            var events = ReadEvents(file);
            var start = events.FirstOrDefault(item => item.Type == "demo_session_start");
            if (start is null) continue;
            var end = events.LastOrDefault(item => item.Type == "demo_session_end");
            results.Add(new UsageSessionSummary(start.SessionId, start.TimestampUtc, start.DemoName ?? start.DemoId ?? "", start.DemoVersion ?? "",
                start.ClientName ?? "", start.OpportunityId ?? "", start.Reason ?? "", start.Persona ?? start.DataSet ?? "",
                events.Count(item => item.Type == "page_view"), end?.DurationSeconds, uploaded));
        }
        return results.OrderByDescending(item => item.StartedUtc).Take(limit).ToList();
    }

    public static List<UsageEvent> ReadEvents(string file)
    {
        var events = new List<UsageEvent>();
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { if (JsonSerializer.Deserialize<UsageEvent>(line, Json) is { } item) events.Add(item); }
                catch (JsonException) { }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return events;
    }

    private static List<string> Files(string folder) => Directory.Exists(folder) ? Directory.GetFiles(folder, "*.jsonl").ToList() : [];
}

/// <summary>A tracked demo session; page views and the end event append to the same file.</summary>
public sealed class UsageSession
{
    private readonly UsageTrackingService _owner;
    private readonly PresentationConfig _config;
    private readonly DemoLaunchContext _context;
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private string? _lastPage;
    private bool _ended;

    internal UsageSession(UsageTrackingService owner, string id, PresentationConfig config, DemoLaunchContext context)
    {
        _owner = owner; Id = id; _config = config; _context = context;
    }

    public string Id { get; }

    internal UsageEvent Event(string type) => new()
    {
        Type = type, SessionId = Id, DemoId = _config.Id, DemoName = _config.Name, DemoVersion = _config.Version,
        Organization = _config.Catalog.Organization, DataSet = _context.DataSet?.Id, Persona = _context.DataSet?.Persona is { Length: > 0 } persona ? persona : _context.DataSet?.Name,
        Reason = Blank(_context.Reason), ClientName = Blank(_context.ClientName), OpportunityId = Blank(_context.OpportunityId)
    };

    public void PageViewed(string pageId, string title)
    {
        if (_ended || pageId == _lastPage) return;
        _lastPage = pageId;
        var item = Event("page_view");
        item.PageId = pageId; item.PageTitle = title;
        item.Organization = null;
        _owner.Append(Id, item);
    }

    public void End()
    {
        if (_ended) return;
        _ended = true;
        var item = Event("demo_session_end");
        item.Organization = null;
        item.DurationSeconds = Math.Round((DateTimeOffset.UtcNow - _started).TotalSeconds, 1);
        _owner.Append(Id, item);
        _owner.Close(Id);
        AppLog.Info("Usage", $"Tracked session for '{_config.Name}' ended after {TimeSpan.FromSeconds(item.DurationSeconds.Value):m\\:ss}.");
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
