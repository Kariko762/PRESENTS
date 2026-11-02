using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Tests;

public sealed class StartupCheckTests
{
    [Fact]
    public async Task PassedCheckPublishesTicksAndMessages()
    {
        var check = new StartupCheck("Library", true);
        var updates = 0;
        check.PropertyChanged += (_, _) => updates++;
        await check.RunAsync(() => Task.FromResult("3 demo packages"));
        Assert.Equal("Passed", check.Status);
        Assert.Equal("✓", check.Symbol);
        Assert.Equal("3 demo packages", check.Message);
        Assert.True(updates > 0);
    }

    [Theory]
    [InlineData(true, "Failed")]
    [InlineData(false, "Warning")]
    public async Task FailureSeverityDependsOnRequiredStatus(bool required, string status)
    {
        var check = new StartupCheck("Remote", required);
        await check.RunAsync(() => throw new IOException("Unavailable"));
        Assert.Equal(status, check.Status);
        Assert.Equal("Unavailable", check.Message);
    }

    [Fact]
    public void DisabledRemoteCheckIsSkippedNotPassed()
    {
        var check = new StartupCheck("Remote", false);
        check.Skip("Disabled by policy");
        Assert.Equal("Skipped", check.Status);
    }
}