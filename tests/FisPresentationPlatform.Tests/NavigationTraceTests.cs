using System.Text.Json;
using FisPresentationPlatform.Services;

namespace FisPresentationPlatform.Tests;

public sealed class NavigationTraceTests
{
    [Fact]
    public void CheckpointsAreCorrelatedMonotonicAndDoNotInventMissingResponse()
    {
        var root = Path.Combine(Path.GetTempPath(), "fis-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var diagnostics = new RuntimeDiagnostics(root);
            var trace = new NavigationTrace(diagnostics);
            trace.Begin("first");
            var firstRequest = trace.RequestId;
            trace.Mark("BrowserLoading"); trace.Mark("DocumentResponse", 200); trace.Mark("DomReady"); trace.Mark("Available");
            trace.Mark("Available");
            trace.Begin("second"); trace.Mark("BrowserLoading"); trace.Mark("DomReady"); trace.Mark("Available");
            using var json = JsonDocument.Parse(diagnostics.Export([]));
            var events = json.RootElement.GetProperty("Events").EnumerateArray().ToList();
            var first = events.Where(item => item.GetProperty("RequestId").GetString() == firstRequest).ToList();
            Assert.Equal(["Requested", "BrowserLoading", "DocumentResponse", "DomReady", "Available"], first.Select(item => item.GetProperty("EventId").GetString()));
            Assert.Equal(200, first[2].GetProperty("HttpStatus").GetInt32());
            var timings = first.Select(item => item.GetProperty("DurationMilliseconds").GetDouble()).ToArray();
            Assert.Equal(timings.OrderBy(value => value), timings);
            Assert.All(first, item => Assert.Equal("first", item.GetProperty("PageId").GetString()));
            Assert.DoesNotContain(events.Where(item => item.GetProperty("RequestId").GetString() == trace.RequestId), item => item.GetProperty("EventId").GetString() == "DocumentResponse");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PlainHtmlBaselineInstallsWithTwoLinkOnlyPages()
    {
        var root = Path.Combine(Path.GetTempPath(), "fis-baseline-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var library = new DemoLibraryService(Path.Combine(root, "library"));
            Assert.Equal(1, await NavigationBaselineInstaller.InstallAsync(library));
            Assert.Equal(0, await NavigationBaselineInstaller.InstallAsync(library));
            var demo = Assert.Single(await library.LoadAsync());
            var content = await new PresentationService(cacheRoot: Path.Combine(root, "extract")).ExtractAndValidateAsync(demo.PackagePath);
            Assert.Equal("1.1.0", content.Config.Version);
            Assert.Equal(2, content.Config.Navigation.Sections.Single().Pages.Count);
            foreach (var page in content.Config.Navigation.Sections.Single().Pages)
            {
                var html = await File.ReadAllTextAsync(Path.Combine(content.Directory, page.Path));
                Assert.DoesNotContain("<script", html);
                Assert.DoesNotContain("<img", html);
                Assert.DoesNotContain("<link", html);
                var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);
                Assert.Single(document.Body!.Children);
                Assert.Equal("A", document.Body.Children[0].TagName);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}