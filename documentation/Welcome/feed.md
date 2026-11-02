# Welcome: news, published demos and updates

Startup shows exactly two visible top-level tabs, in order: **Welcome**, **Demo Library**. Welcome is selected. Clicking Demo Library in the sidebar hides Welcome and shows Library only. The Build Package sidebar action, and Edit configuration from a library tile's ⋯ menu, each show only their own top-level tab. Welcome sidebar restores the startup pair. Editor sub-tabs (General, Menu & Pages, JSON) stay inside Configuration Studio.

The Welcome tab consumes a JSON feed from an approved HTTPS location. Set its URL in Settings → Remote Repository and save settings. No production address has been supplied. With no address configured, Welcome shows three bundled offline sample blog posts, clearly labelled Sample preview rather than official announcements. A configured address uses its own live/cache path, not the samples. This is a published-content feed, not a demo-package download or automatic update mechanism.

## Remote contract

Publish UTF-8 JSON at a directly reachable HTTPS URL, with HTTP 200. TLS validation uses the operating system defaults; it is never disabled. Redirects are not automatically followed. Public unauthenticated or transparently accessible corporate endpoints are supported; custom authentication, token management and signed publishing are future work.

Required envelope: schemaVersion = 1.0 and items array. Up to 200 items, each with a unique nonempty ID, kind (news, demo, update), title and publication timestamp. Optional summary, body (plain text, max 12,000 characters), product, version, HTTPS URL and demoId. Content is displayed as text; no remote HTML/script is executed. URL is metadata only and is not auto-opened/downloaded. Read post opens a scrollable blog-style reader with the full body.

## Internal links

An item with `demoId` gets a View demo in Library action. The ID must match the stable presentation ID from presentation.json; it cannot be a filesystem path, arbitrary command or web address. The action opens the Library-only workspace, clears prior filters and shows matching installed versions with a bright-green card outline. It never auto-launches or imports packages. If the ID is not installed, a clear message explains that it must be imported. Show all demos clears the focused view.

Example: `"demoId": "fis-demo"` targets the bundled offline sample after it has been installed. Other application destinations (builder/configuration/pages) are not yet feed-link actions.

```json
{
  "schemaVersion": "1.0",
  "items": [
    {
      "id": "payments-demo-2026-11",
      "kind": "demo",
      "title": "Payments demonstration published",
      "summary": "A newly published presentation is available from the approved distribution channel.",
      "publishedUtc": "2026-11-01T10:00:00Z",
      "product": "Payments",
      "version": "1.0.0",
      "url": "https://your-approved-host.example/demos/payments"
    }
  ]
}
```

The example is publishing documentation, not a configured/live FIS endpoint. Items sort by publication date descending. Bundled preview items are in Assets/welcome-sample.json and are not represented as actual FIS news or remote releases.

## Local fallback

Settings and cache live under LocalAppData/FISPresents/Welcome. The cache filename is derived from the feed address; changing URLs cannot display a different source's old cache. A valid response is atomically cached with its retrieval time. Timeouts, unreachable endpoints, non-success status, oversized responses or invalid feeds show the last valid local copy. If no valid cache exists, the tab reports Unavailable. Feed retrieval is bounded to five seconds and 1 MiB.

Invalid remote content never replaces a valid cached document. Local caching failure does not suppress successfully retrieved content. Clearing the feed URL disables remote news and returns to the bundled sample preview rather than an unrelated old cache. Preview JSON is saved separately as sample-news.json; real feed caches remain source-specific. Published demo announcements do not imply those packages are installed; local imports remain in Demo Library.

The tests exercise live retrieval with a controlled HTTP handler, persisted URL settings, offline fallback, source-isolated caches, invalid/oversized feeds and input rejection. WPF tests assert visible tab order and sidebar isolation. A production-network integration test requires the approved feed endpoint.