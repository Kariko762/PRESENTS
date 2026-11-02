# Settings

Settings is a sidebar workspace whose own tabs are Storage & Backups, Remote Repository, Startup & Display, Usage Tracking and Logs. There is no outer "Settings" tab: the workspace tab strip is hidden and **Save Settings** (with its status line) sits on the right of the settings tab strip. Welcome no longer contains the feed setup form.

## First-run defaults

Until a user saves settings, Studio reads `deployment.json` beside the exe (if present) instead of the built-in defaults. It may contain any subset of these settings (camelCase or PascalCase, comments allowed, environment variables such as `%USERPROFILE%` expanded) and is validated like saved settings. See the [deployment guide](../Guides/deployment-guide.md#4-set-the-first-run-defaults).

## Storage & Backups

Library, cache and backup paths are independently configurable absolute folders. Save validates writable locations and rejects nested folders and symbolic links/junctions. Changing library location selects another library; files are not automatically moved or deleted. Cache changes apply to new Player sessions.

Back up now uses saved settings. ZIP snapshots contain the library index, packages and covers, excluding staging. Retention keeps 1–50 snapshots. Optional automatic backups run before library imports and favorite changes when an index exists; launch-history writes do not trigger backups. Failed automatic backups prevent the mutation.

Restore manually into a separate folder, then select it as the library location. Automatic restore/migration and cache cleanup are future work. Backups do not include news caches, browser profiles or preferences. Avoid deleting active caches while Players are open.

## Remote Repository

**Demo repository** is the absolute local or UNC folder that holds `repository.json`, `assignments.json` and published packages (default `C:\FISDemoRepository`). It must be separate from the library, cache and backup folders. Saving re-reads the repository. See [repository](../DemoLibrary/repository.md).

**News feed:** the URL points to the HTTPS Welcome JSON feed, for news/demo/version announcements. Saved check-on-launch controls whether startup requests remote content; when disabled the existing cache is loaded without HTTP. Periodic checks run while Studio is open at intervals of 5–10080 minutes. Check now uses saved settings. A blank URL shows sample news and disables polling.

Demo package updates come from the demo repository, not the news feed. They are always confirmed by the user and are hash-verified but not publisher-signed. Signed update manifests and credentials remain deferred. Legacy Welcome feed URL settings are read when no new repository URL is saved.

## Startup & Display

**Display size**: Auto (fit to screen) or a fixed 80–125%, applied immediately and saved as `UiScale`. Ctrl + / Ctrl − / Ctrl 0 adjust it from anywhere in Studio. See [visual style](../Design/visual-style.md#screen-fit).

Start with Windows sets/removes the current user's FISPresents Windows Run registry entry on Save Settings. No elevation or other-user changes are requested. Dotnet-hosted builds register the DLL path too; moving the app requires saving again. Registry failures are reported separately from saved preferences. Enterprise policy may still disable startup execution.

Tests never enable startup registration on the user's account. Opt-in save and Windows sign-in remain a manual check.

## Persistence / verification

Settings are atomically stored in LocalAppData/FISPresents/Settings/settings.json. Full regression: 60 passing tests including path/polling persistence, validation, backup content/retention, cache-only HTTP behavior and WPF Settings isolation. Windows sign-in, long-lived timers, multi-process writes and interrupted large backups are not covered.

## Usage Tracking

- **Track demo sessions by default** (`TrackingEnabledByDefault`) is the initial state of the *Track this session* switch on the demo start dialog. The consultant can change it for each launch.
- **Upload usage to the demo repository in the background on start** (`UploadUsage`). The *Usage upload* startup check runs only if the *Demo repository* check passed. It is not awaited, so startup is never blocked. Otherwise it is skipped and events stay queued locally.
- Events are JSON lines in `<library>\usage\pending\*.jsonl`:
  - `app_start` is always recorded.
  - Tracked sessions record `demo_session_start`, `page_view` and `demo_session_end`.
  - Each event carries the user, machine, app version, demo id, name and version, and L3–L8 tags. Sessions also carry the persona/data set, reason, client name and Opportunity ID.
- Untracked sessions write nothing.
- Upload copies closed files to `<repository>\usage\<MACHINE_user>\` and then moves them to `usage\uploaded`. A session still open in a Player is never uploaded.
- The tab lists recent sessions (client · OID, persona, reason, pages, duration, status). It also offers Open folder and Upload now.

## Logs

- Studio writes a tab-separated daily log to `%LOCALAPPDATA%\FISPresents\logs\studio-yyyyMMdd.log` and keeps 14 days.
- Logged events include startup checks, downloads and updates, imports, uninstalls, library actions, usage uploads, Player launches and errors.
- The Logs tab:
  - shows the selected day newest-first
  - can filter by level (All, Warnings, Errors) and by text
  - offers Refresh and Open folder
  - refreshes live while it is open
- The About dialog's **View logs** button opens this tab.
