using System.Diagnostics;

namespace FisPresentationPlatform.Services;

public sealed class NavigationTrace(RuntimeDiagnostics diagnostics)
{
    private long _started;
    private long _previous;
    private readonly HashSet<string> _observed = [];
    public string RequestId { get; private set; } = "";
    public string PageId { get; private set; } = "";
    public double ElapsedMilliseconds => _started == 0 ? 0 : Stopwatch.GetElapsedTime(_started).TotalMilliseconds;

    public void Begin(string pageId)
    {
        _started = _previous = Stopwatch.GetTimestamp();
        RequestId = Guid.NewGuid().ToString("N");
        PageId = pageId;
        _observed.Clear();
        Mark("Requested");
    }

    public void Mark(string stage, int? httpStatus = null)
    {
        if (_started == 0 || !_observed.Add(stage)) return;
        var now = Stopwatch.GetTimestamp();
        diagnostics.Record("NavigationTrace", stage, durationMilliseconds: Stopwatch.GetElapsedTime(_started, now).TotalMilliseconds,
            requestId: RequestId, pageId: PageId, sincePreviousMilliseconds: Stopwatch.GetElapsedTime(_previous, now).TotalMilliseconds,
            httpStatus: httpStatus);
        _previous = now;
    }
}