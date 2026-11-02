using System.IO;

namespace FisPresentationPlatform.Services;

public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Category, string Message)
{
    public string TimeLabel => Timestamp.LocalDateTime.ToString("HH:mm:ss");
    public string Line => $"{Timestamp:O}\t{Level}\t{Category}\t{Message.Replace('\n', ' ').Replace('\r', ' ')}";

    public static LogEntry? Parse(string line)
    {
        var parts = line.Split('\t', 4);
        return parts.Length == 4 && DateTimeOffset.TryParse(parts[0], out var timestamp) ? new LogEntry(timestamp, parts[1], parts[2], parts[3]) : null;
    }
}

/// <summary>Application log: one tab-separated file per day plus a bounded in-memory buffer for the in-app viewer.</summary>
public static class AppLog
{
    private const int MemoryLimit = 2000;
    private static readonly Lock Gate = new();
    private static readonly List<LogEntry> Recent = [];

    public static string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FISPresents", "logs");
    public static int DaysToKeep { get; set; } = 14;
    public static event Action<LogEntry>? Written;

    public static void Info(string category, string message) => Write("INFO", category, message);
    public static void Warn(string category, string message) => Write("WARN", category, message);
    public static void Error(string category, string message, Exception? exception = null) =>
        Write("ERROR", category, exception is null ? message : $"{message} · {exception.GetType().Name}: {exception.Message}");

    public static void Write(string level, string category, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, category, message);
        lock (Gate)
        {
            Recent.Add(entry);
            if (Recent.Count > MemoryLimit) Recent.RemoveRange(0, Recent.Count - MemoryLimit);
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(FileFor(entry.Timestamp), entry.Line + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* Logging must never break the app. */ }
        }
        Written?.Invoke(entry);
    }

    public static string FileFor(DateTimeOffset day) => Path.Combine(Folder, $"studio-{day.LocalDateTime:yyyyMMdd}.log");

    public static IReadOnlyList<string> LogFiles() =>
        Directory.Exists(Folder) ? Directory.GetFiles(Folder, "studio-*.log").OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase).ToList() : [];

    /// <summary>Reads entries from a log file (newest last). Lines that are not log entries are skipped.</summary>
    public static IReadOnlyList<LogEntry> Read(string path)
    {
        lock (Gate)
        {
            if (!File.Exists(path)) return [];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var entries = new List<LogEntry>();
            while (reader.ReadLine() is { } line) if (LogEntry.Parse(line) is { } entry) entries.Add(entry);
            return entries;
        }
    }

    public static IReadOnlyList<LogEntry> Snapshot() { lock (Gate) return Recent.ToList(); }

    public static int Prune()
    {
        var removed = 0;
        foreach (var path in LogFiles().Skip(DaysToKeep))
        {
            try { File.Delete(path); removed++; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return removed;
    }
}
