using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Tests;

public sealed class PageReadinessTests
{
    [Fact]
    public void DefaultModeCompletesOnlyAfterNavigationSuccess()
    {
        var gate = new PageReadinessGate(); gate.Begin(false);
        Assert.False(gate.IsReady);
        gate.NavigationFinished(true);
        Assert.True(gate.IsReady);
    }

    [Fact]
    public void MessageModeWaitsForBothNavigationAndCurrentPageSignal()
    {
        var gate = new PageReadinessGate(); gate.Begin(true);
        gate.NavigationFinished(true);
        Assert.False(gate.IsReady);
        Assert.False(gate.ReceiveReady("wrong-token"));
        Assert.True(gate.ReceiveReady(gate.Token));
        Assert.True(gate.IsReady);
    }

    [Fact]
    public void EarlySignalNeverBypassesFailedNavigation()
    {
        var gate = new PageReadinessGate(); gate.Begin(true);
        gate.ReceiveReady(gate.Token);
        Assert.False(gate.IsReady);
        gate.NavigationFinished(false);
        Assert.False(gate.IsReady);
        Assert.False(gate.ReceiveReady(gate.Token));
    }

    [Fact]
    public void OldPageSignalAndTimedOutSignalCannotDismissNewLoader()
    {
        var gate = new PageReadinessGate(); gate.Begin(true);
        var oldToken = gate.Token;
        gate.Begin(true); gate.NavigationFinished(true);
        Assert.False(gate.ReceiveReady(oldToken));
        gate.Fail();
        Assert.False(gate.ReceiveReady(gate.Token));
        Assert.False(gate.IsReady);
    }

    [Fact]
    public void LoadingPolicyRejectsInvalidModeAndTimeout()
    {
        var service = new PresentationService();
        var config = new FisPresentationPlatform.Models.PresentationConfig();
        config.Loading.ReadyMode = "skip-validation";
        Assert.Throws<System.Text.Json.JsonException>(() => service.Deserialize(service.Serialize(config)));
        config.Loading.ReadyMode = "message"; config.Loading.TimeoutSeconds = 121;
        Assert.Throws<System.Text.Json.JsonException>(() => service.Deserialize(service.Serialize(config)));
    }
}