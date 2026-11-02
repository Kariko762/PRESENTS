using System.Diagnostics;

namespace FisPresentationPlatform.Services;

public sealed record PreflightResult(string Id, string Name, string Status, string Severity, string Message, double DurationMilliseconds)
{
    public bool CanContinue => Status is "PASS" or "WARNING";
}

public sealed class PreflightRunner
{
    private readonly List<PreflightResult> _results = [];
    public IReadOnlyList<PreflightResult> Results => _results;
    public bool CanLaunch => _results.Count > 0 && _results.All(result => result.CanContinue);

    public async Task<bool> RunAsync(string id, string name, Func<Task> check)
    {
        if (_results.Any(result => !result.CanContinue))
        {
            _results.Add(new PreflightResult(id, name, "BLOCKED", "High", "Prerequisite check failed.", 0));
            return false;
        }
        var started = Stopwatch.GetTimestamp();
        try
        {
            await check();
            _results.Add(new PreflightResult(id, name, "PASS", "Informational", "Check passed.", Stopwatch.GetElapsedTime(started).TotalMilliseconds));
            return true;
        }
        catch (Exception exception)
        {
            _results.Add(new PreflightResult(id, name, "FAILED", "Critical", exception.Message, Stopwatch.GetElapsedTime(started).TotalMilliseconds));
            return false;
        }
    }
}