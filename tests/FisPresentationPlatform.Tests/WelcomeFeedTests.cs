using System.Net;
using System.Net.Http;
using System.Text;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Tests;

public sealed class WelcomeFeedTests
{
    private const string ValidFeed = """
        {"schemaVersion":"1.0","items":[
          {"id":"news-one","kind":"news","title":"News","summary":"Announcement","publishedUtc":"2026-10-31T10:00:00Z"},
          {"id":"demo-one","kind":"demo","title":"New demo","summary":"Published demo","publishedUtc":"2026-11-01T10:00:00Z","product":"Payments","version":"1.0.0"}
        ]}
        """;

    [Fact]
    public async Task SuccessfulFeed_IsCachedAndSortedAndSurvivesRemoteFailure()
    {
        using var fixture = new Fixture();
        var handler = new FeedHandler(ValidFeed);
        using var service = new WelcomeFeedService(fixture.Root, handler);
        await service.SaveUrlAsync("https://news.example.test/feed.json");
        Assert.Equal("https://news.example.test/feed.json", await service.LoadUrlAsync());
        var live = await service.RefreshAsync(await service.LoadUrlAsync());
        Assert.Equal("Live", live.Source);
        Assert.Equal("demo-one", live.Items[0].Id);
        handler.Fail = true;
        var cached = await service.RefreshAsync(await service.LoadUrlAsync());
        Assert.Equal("Cached", cached.Source);
        Assert.Equal(2, cached.Items.Count);
        Assert.NotNull(cached.CachedUtc);
        var changedAddress = await service.RefreshAsync("https://other.example.test/feed.json");
        Assert.Equal("Unavailable", changedAddress.Source);
        Assert.Empty(changedAddress.Items);
    }

    [Fact]
    public async Task InvalidRemoteFeed_DoesNotReplaceLastValidCache()
    {
        using var fixture = new Fixture();
        var handler = new FeedHandler(ValidFeed);
        using var service = new WelcomeFeedService(fixture.Root, handler);
        await service.RefreshAsync("https://news.example.test/feed.json");
        handler.Json = "{\"schemaVersion\":\"99.0\",\"items\":[]}";
        Assert.Equal("Cached", (await service.RefreshAsync("https://news.example.test/feed.json")).Source);
        handler.Json = new string('a', 1024 * 1024 + 1);
        Assert.Equal("Cached", (await service.RefreshAsync("https://news.example.test/feed.json")).Source);
    }

    [Fact]
    public async Task UnconfiguredFeed_HasHonestEmptyStateAndHttpIsRejected()
    {
        using var fixture = new Fixture();
        using var service = new WelcomeFeedService(fixture.Root, new FeedHandler(ValidFeed));
        Assert.Equal("Not configured", (await service.RefreshAsync("")).Source);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveUrlAsync("http://news.example.test/feed.json"));
    }

    [Fact]
    public async Task BundledSampleNews_HasBlogBodyAndInternalDemoLinkAndSeparateCache()
    {
        using var fixture = new Fixture();
        using var service = new WelcomeFeedService(fixture.Root, new FeedHandler(ValidFeed));
        var sample = await service.LoadSampleAsync();
        Assert.Equal("Sample preview", sample.Source);
        Assert.Equal(3, sample.Items.Count);
        Assert.All(sample.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Body)));
        Assert.Contains(sample.Items, item => item.DemoId == "fis-demo" && item.HasDemoLink);
        Assert.True(File.Exists(Path.Combine(fixture.Root, "sample-news.json")));
        Assert.Equal("Live", (await service.RefreshAsync("https://news.example.test/feed.json")).Source);
    }

    [Fact]
    public void InternalDemoLinks_RejectPathsAndDoNotInterpretArbitraryCommands()
    {
        var json = """
            {"schemaVersion":"1.0","items":[{"id":"post","kind":"demo","title":"Demo","publishedUtc":"2026-11-01T10:00:00Z","demoId":"../outside"}]}
            """;
        Assert.Throws<System.Text.Json.JsonException>(() => WelcomeFeedService.Parse(json));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":\"1.0\",\"items\":[null]}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"items\":[{\"id\":\"a\",\"kind\":\"execute\",\"title\":\"Bad\"}]}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"items\":null}")]
    public void InvalidFeedItems_AreRejected(string json) => Assert.Throws<System.Text.Json.JsonException>(() => WelcomeFeedService.Parse(json));

    private sealed class FeedHandler(string json) : HttpMessageHandler
    {
        public bool Fail { get; set; }
        public string Json { get; set; } = json;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("Offline");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fis-news-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}