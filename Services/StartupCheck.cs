using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace FisPresentationPlatform.Services;

public sealed class StartupCheck(string name, bool required) : INotifyPropertyChanged
{
    public string Name { get; } = name;
    public bool Required { get; } = required;
    public string Status { get; private set; } = "Waiting";
    public string Message { get; private set; } = "Pending check";
    public string Symbol => Status switch { "Passed" => "✓", "Warning" => "!", "Failed" => "✕", "Checking" => "↻", "Skipped" => "–", _ => "○" };
    public double DurationMilliseconds { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task RunAsync(Func<Task<string>> action)
    {
        Status = "Checking"; Message = "Checking…"; Notify();
        var start = Stopwatch.GetTimestamp();
        try { Message = await action(); Status = "Passed"; }
        catch (Exception exception) { Message = exception.Message; Status = Required ? "Failed" : "Warning"; }
        DurationMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Notify();
    }

    public void Skip(string reason) { Status = "Skipped"; Message = reason; Notify(); }
    private void Notify()
    {
        foreach (var property in new[] { nameof(Status), nameof(Message), nameof(Symbol), nameof(DurationMilliseconds) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}