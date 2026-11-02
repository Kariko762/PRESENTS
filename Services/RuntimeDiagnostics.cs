using System.IO;
using System.Text.Json;

namespace FisPresentationPlatform.Services;

public sealed record DiagnosticEvent(DateTimeOffset Timestamp, string SessionId, string Component, string EventId, string Severity, double? DurationMilliseconds,
    string? RequestId = null, string? PageId = null, double? SincePreviousMilliseconds = null, int? HttpStatus = null);

public sealed class RuntimeDiagnostics
{
    private readonly List<DiagnosticEvent> _events = [];
    private readonly object _gate = new();
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public string LogPath { get; }
    public RuntimeDiagnostics(string? logDirectory = null)
    {
        var directory = logDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FIS", "PresentationRuntime", "Logs");
        LogPath = Path.Combine(directory, $"session-{SessionId}.jsonl");
    }

    // Only fixed event identifiers are recorded; page content, URLs, tokens and JS messages are not persisted.
    public void Record(string component, string eventId, string severity = "Information", double? durationMilliseconds = null,
        string? requestId = null, string? pageId = null, double? sincePreviousMilliseconds = null, int? httpStatus = null)
    {
        lock (_gate)
        {
            if (_events.Count >= 500) return;
            var item = new DiagnosticEvent(DateTimeOffset.UtcNow, SessionId, component, eventId, severity, durationMilliseconds,
                requestId, pageId, sincePreviousMilliseconds, httpStatus);
            _events.Add(item);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, JsonSerializer.Serialize(item) + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Diagnostics failures must not stop playback; export retains in-memory events.
            }
        }
    }

    public string Export(IReadOnlyList<PreflightResult> checks)
    {
        lock (_gate)
            return JsonSerializer.Serialize(new
            {
                SessionId,
                RuntimeVersion = typeof(RuntimeDiagnostics).Assembly.GetName().Version?.ToString(),
                // Raw exception text may contain source paths; export check codes/timings, not content.
                Preflight = checks.Select(check => new { check.Id, check.Name, check.Status, check.Severity, check.DurationMilliseconds }),
                Events = _events
            }, new JsonSerializerOptions { WriteIndented = true });
    }
}