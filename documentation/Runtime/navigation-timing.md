# Local navigation diagnostic experiment

## Resolved: virtual-host hostname

The Player now maps and navigates https://fis.example, not https://fis.local. Navigation validation, document-response checks and web-message origin checks use the same constant. MicrosoftEdge/WebView2Feedback issue 2381 documents a hostname-resolution timeout even when local content is served by virtual mapping: https://github.com/MicrosoftEdge/WebView2Feedback/issues/2381.

Live measurements on 1 November 2026:

- No document-created scripts, still on .local: response → DOM about 2000 ms.
- Bare visible WebView2, no Player handlers, still on .local: total loads about 2031/2022 ms.
- Normal Player with .example, injection enabled: 115 ms first request-to-ready, 58 ms native Next, 38 ms direct HTML-link return. Response → DOM about 15–28 ms.

All 72 tests pass, including live native and direct-link navigation. These are local-machine observations, not guarantees on every enterprise device. The bare .local browser probe is deliberately retained in tests for comparison, not used for production navigation. Newer issue comments note .example may not avoid DNS waiting on every network; .localhost remains an alternative if a target-device test reproduces the stall.

Keep standard WebView2, normal TLS/security checks, local extraction and script/readiness behavior. The failed composition probe is not needed. Relative package links/assets require no rebuild. Presentations hardcoding fis.local URLs need author-side correction to relative paths; origin-bound browser state will not carry across the hostname change.

The following sections retain the historical investigations leading to this fix.

## Response-to-DOM delay probe

The composition probe was rolled back after a real Loaded-event crash. Windows Application event 1026 reported FileNotFoundException for Microsoft.Windows.SDK.NET, version 10.0.17763.10, from WebView2CompositionControl_Loaded. Compilation did not catch this runtime dependency. The current Player is back on standard WebView2 and marks logs with StandardWebViewLoading. A live STA regression now initializes the actual Player and navigates its two-page baseline successfully. The delay is not claimed fixed; composition hosting would require correct Windows SDK projection deployment before revisiting it.

The supplied session 90cf9f7c9df542c490c1f25efe04f810 shows document response within 12–21 ms, followed by 2001–2018 ms before DOMContentLoaded on both native and direct-link navigation. Package checks took 29 ms; handlers took about 1 ms for native navigation and 17 ms for the direct link. This rules out repeated extraction and slow initial document response for that run, but does not locate browser-internal waiting precisely.

The Player previously set WebView2.Visibility to Hidden during every load. The current probe uses WebView2CompositionControl, kept Visible under the opaque reusable loader, so background/visibility throttling can be tested without revealing unready content. Diagnostics mark this build's behavior with ActiveCompositionLoading. Error views may still hide the browser. Hosting, package extraction and document content are unchanged.

Repeat the same native and direct-link loop and compare DocumentResponse → DomReady. The automated suite verifies compilation and regressions; it does not prove this change eliminates the runtime delay. If the two-second gap persists, browser-internal timing/injected-script and host-resolution comparisons remain necessary.

Plain HTML Navigation Baseline version 1.1.0 contains exactly two authored HTML documents. Each body contains only a link to the other page. No CSS, scripts, images, fonts, frameworks or external resources are included. The runtime still injects its diagnostic/readiness script and uses the same loader, mapping and security policy as real demos; this is not an uninstrumented WebView test.

## Verified architecture

- WebView2 environment/control is initialized once per Player. WebViewInitialized logs that duration. It is disposed on Player close, not on page transitions.
- Native menu/Next/Previous calls Navigate on the existing CoreWebView2. Ordinary HTML links navigate directly within that same control.
- NavigationStarting validates the local HTTPS origin and configured page, updates loader/navigation state and starts browser timing. It does not hash/extract packages or retrieve content. NavigationStartingCompleted logs the host handler duration.
- No WebResourceRequested custom file-serving handler is registered. SetVirtualHostNameToFolderMapping serves the already-extracted content folder.
- Each open extracts the archive once. Subsequent pages read from that folder. Inter-process and local request resolution delays are not yet proven or excluded.

## Trace

Every request carries RequestId and configured PageId. Persistent JSONL and diagnostic exports contain UTC Timestamp, monotonic DurationMilliseconds since the request, SincePreviousMilliseconds and observed HTTP status where available.

Five primary checkpoints:

1. Requested: native page request, before loader setup/Navigate. For HTML-link requests the first host observation is NavigationStarting; no native click timestamp exists.
2. BrowserLoading: accepted NavigationStarting.
3. DocumentResponse: matching document WebResourceResponseReceived; browser response observation, not exact filesystem read/flush.
4. DomReady: matching DOMContentLoaded.
5. Available: readiness gate permits content; not an actual first-paint measurement.

Supplemental events: NativeNavigation/DirectBrowserNavigation identify route; BrowserCompleted records load completion; timeout/process/failure events indicate unsuccessful loads. NavigationStartingCompleted uses duration for handler execution only, not request elapsed. WebViewInitialized is expected once in a normal Player session.

Missing DocumentResponse remains missing when virtual mapping/browser does not expose that response. No synthetic served time is generated. Timestamps are host callback observations; WPF dispatcher congestion can delay them. Logs avoid URLs, source paths, response bodies and arbitrary script messages. Synchronous logging itself may add small overhead, so this trace should be interpreted as diagnostic rather than zero-overhead profiling.

## Live loop

Open Plain HTML Navigation Baseline v1.1.0 from Demo Library after startup installs it. Click the page's own Page two/Page one links back and forth five times, then repeat with native Next/Previous. Export Diagnostics from Player.

Compare grouped requests:

- Native Requested → BrowserLoading: host dispatch/Navigate delay.
- BrowserLoading → DocumentResponse: browser/local-host request observation delay.
- DocumentResponse → DomReady: parsing/event dispatch.
- DomReady → BrowserCompleted: remainder of load lifecycle.
- BrowserCompleted → Available: runtime/presentation readiness gating.
- NavigationStartingCompleted duration: host interception overhead.

If both link-only and native routes reproduce the delay, demo animation is unnecessary to cause it. If direct links are fast but native requests slow, inspect native dispatch/loader path. Do not attribute a missing response stage to server time. Browser-internal timings or controlled alternative hosting may still be required.

Automated package tests verify both link-only pages, versioned installation and trace correlation. They do not establish live WebView2 timing or prove the two-second delay fixed.