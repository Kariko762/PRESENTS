# Shared demo loading screen

DemoLoadingView is a native reusable animated control owned by Player, not by the HTML. It appears while verifying/extracting a package, preparing WebView2, and loading each document. The browser is hidden while the loader is shown (avoiding WebView2 native-window overlay issues). Loader animation stops when hidden. Errors replace the loader with the runtime error screen, never an endless spinner.

Existing v1 configurations default to successful browser-navigation completion. This preserves ordinary HTML/media demos without requiring new presentation scripts. Navigation success is not proof that every API request, image or interaction is ready.

For demos that prepare content asynchronously, opt into explicit readiness in presentation.json:

```json
"loading": {
  "readyMode": "message",
  "timeoutSeconds": 30
}
```

Supported modes: navigation (default), message. Timeout: 5–120 seconds, default 15. These properties can currently be edited through the JSON editor. Message mode applies to every page, including the entry point. Every HTML page must signal readiness; a page that never signals times out. Direct-media wrappers currently use navigation mode; do not opt them into message mode without a readiness script.

## Presentation API

The runtime injects this API before top-level page scripts run:

```javascript
async function prepareDemo() {
  await loadRequiredData();
  renderInitialContent();
  window.PresentationRuntime.ready();
}
```

`ready()` sends an allowlisted message through WebView2. An early call is queued until the runtime binds a fresh per-navigation token after DOMContentLoaded. It cannot dismiss loading until that navigation also succeeds. Old page tokens, foreign origins, frame messages and failure/timeout messages cannot complete a new page's load. This is a coordination API, not publisher authentication or proof that a malicious page is safe.

Errors in presentation preparation should not call ready; the runtime timeout provides a bounded failure state. Normal browser-navigation mode ignores the need for an explicit signal and continues to work. No preloading or hidden execution of all pages is added.

Timing diagnostics now distinguish browser PageLoaded from PageReady completion. This lifecycle does not fix the previously measured ~2-second navigation overhead; performance investigation remains outstanding.

## Verification

The 70-test suite includes readiness before/after successful navigation, stale token rejection, failure/timeout rejection, loading config bounds and WPF control layout. It does not execute the message bridge in a live WebView2 process. Manual validation: open ordinary samples; verify animation during package/startup/page changes; try a message-mode authored page with asynchronous preparation and a page that never calls ready; confirm timeout shows an error and diagnostics remain exportable.