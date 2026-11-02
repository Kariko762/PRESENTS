# Startup checks

Startup presents a live checklist of uniform 50 px tiles. Each tile shows only a status symbol, the check name and the status word; the detailed message is in its tooltip.

| Check | Required | Notes |
|---|---|---|
| Settings | Yes | Loads and validates settings.json |
| Configuration schema | Yes | Embedded presentation schema |
| Local storage | Yes | Library/cache/backup writable; at least 64 MiB free on the cache volume |
| Demo library | Yes | Registered/available/missing demo counts |
| Demo repository | No | Reads repository.json; Warning when the cached index is used or the repository is missing |
| User assignments | No | L8 solutions assigned to the current Windows user |
| Demo updates | No | Compares the repository with the library and reports update/new-demo counts |
| WebView2 runtime | No | Installed runtime version |
| News feed | No | Configured HTTPS feed; Skipped when disabled or blank |
| Welcome content | No | Cache/sample content |

Checks show Waiting/Checking/Passed/Warning/Failed/Skipped with durations. Required failures route to Settings. Optional failures are warnings. If updates are available when the checks finish, Studio prompts **Yes, update / Not now**, and Yes downloads, verifies and installs them. See [demo repository](../DemoLibrary/repository.md).

The storage probe creates and deletes a unique empty file in each configured location. Demo count checks index/file availability, not full hashes; Player validates packages on launch.

Tests cover startup result severity, the startup panel render (WPF) and the repository publish/download/update/cache-fallback flow. Live network connectivity, UNC shares and WebView2 behaviour depend on the local Windows environment.
