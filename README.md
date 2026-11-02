# FIS Presentation Studio

A Windows proof of concept for two Studio functions: **package an existing content folder** and **edit the runtime configuration for that content**. The desktop uses the screenshot-driven charcoal and FIS-green visual style.

The Studio is not an HTML authoring tool. Content pages are created and maintained in the team's web tools, then selected as existing files in the menu configuration. Demo data is managed as data sets in the Configuration Studio (see below).

The desktop appearance follows the supplied [visual references](documentation/Design/visual-style.md): charcoal panels, FIS green navigation/selected tabs, fixed sidebar and explicit dark control templates. The official logo, supplied building banner and sidebar dot artwork are embedded for offline display. A WPF visual regression test checks the theme and image loading in addition to service tests.

## Guides

| Guide | For |
|---|---|
| [User guide](documentation/Guides/user-guide.md) | Consultants: start-up, library, downloads, presenting, settings, troubleshooting. |
| [Authoring guide](documentation/Guides/authoring-guide.md) | Demo builders: package anatomy, the Template, brand kit, data model, data sets, build, versioning, release. |
| [Administrator guide](documentation/Guides/admin-guide.md) | Repository owners: setup, assignments, releases, retiring, usage collection. |
| [Deployment guide](documentation/Guides/deployment-guide.md) | Rolling the Studio out to laptops: prerequisites, `deployment.json` first-run defaults, install, upgrade, uninstall. |
| [Test plan](documentation/Guides/test-plan.md) | A 45-minute end-to-end walkthrough; `tools\Reset-TestScenario.ps1` sets up its starting state. |
| [Data sets](documentation/Configuration/data-sets.md) | Data set reference: `$extends`, `$files`, limits, editor. |
| [MVP readiness review](documentation/Review/mvp-readiness.md) | Sign-off status and open gaps. |
| [Executive overview](documentation/Executive/FIS-Presents-Executive-Overview.html) | Leadership summary with screenshots and architecture. |

## What works

- **Shared demo loader:** Reusable animated native loading screen for preparation and page transitions, browser-completion fallback, and optional explicit PresentationRuntime.ready() messaging. See [loading contract](documentation/Runtime/demo-loading.md).

- **Demo repository & Downloads:** A shared folder (default `C:\FISDemoRepository`) publishes versioned, SHA-256-verified demos. Downloads lists them, Library/Downloads filter by L3 business and the user's assigned L8 solutions, and startup offers to install available updates. Three branded demos tell the digital-content story for pre-sales consultants, plus the **Data Lab** reference demo; `samples/Demos/Template` is the starter for new demos. See [repository, assignments and publishing tool](documentation/DemoLibrary/repository.md).

- **Settings:** Storage locations, retained ZIP backups, open-folder actions, remote feed launch/periodic checks and per-user Windows startup. See [Settings scope](documentation/Settings/settings.md); trusted automatic package installation remains deferred.

- **Welcome:** Startup opens Welcome beside Demo Library. Blog-style posts with a full-post reader, labelled offline sample content when no remote address is configured, HTTPS live/cache fallback, and internal demo-ID links that open/highlight installed library cards. See [feed contract](documentation/Welcome/feed.md); no production feed URL is preconfigured.

- **Demo Library:** Import verified local packages, browse cover cards, search/filter product/functions/tags, mark favorites, inspect details and launch demos. Recently opened and build-to-library integration are available. See [frontend scope](documentation/DemoLibrary/frontend.md) for storage and current limitations.

- **Package Builder:** Select a folder containing existing HTML pages, CSS, JavaScript, images, fonts, video, and other files, inspect its inventory, validate, and produce one `.fisxpkg`.
- **Configuration Editor:** Load or generate `presentation.json`; edit presentation identity and branding; organize menu sections and existing page references; move sections/pages to set runtime order; and inspect or edit the raw JSON. It opens from a library tile's ⋯ menu (Edit configuration); it has no sidebar entry.
- **Demo start, tracking and logs:** Launching a demo asks for a persona/data set, a reason, a client name and an Opportunity ID, and whether to track the session. Usage is queued locally and uploaded to the repository in the background on start. Logs, usage and an About dialog are available in the app (see [settings](documentation/Settings/settings.md)).
- **Data sets:** The Configuration Studio's Data Sets tab lets you create a set (as an override, full copy or blank), import, edit, check, compare, preview and remove data sets. Sets can `$extends` a base set and reference large CSV files lazily through `$files`. The **Data Lab** demo is the reference and test bench. See [data sets](documentation/Configuration/data-sets.md).
- Add an existing HTML page to a menu section using the file picker. The Builder never creates or edits the HTML document itself.
- Scan the source folder and inventory HTML, styles, scripts, images, video, audio, fonts, configuration, and other files.
- Parse HTML with AngleSharp and scan common HTML/CSS resource references; report missing/unsafe local references while identifying remote dependencies.
- Validate the entry page, menu page paths, duplicate IDs, local logo path, and accent color.
- Build one `.fisxpkg` ZIP package containing the config and source files, with a SHA-256 manifest.
- Store already-compressed media (MP4, WebM, JPEG, PNG, WebP, fonts, etc.) without re-compressing it; compress text assets. Show build progress.
- Verify every packaged file before saving; the player enforces archive budgets and rechecks hashes before isolated extraction. Concurrent opens and different same-version packages do not replace running content.
- Open the package in the shared WPF/WebView2 runtime shell. The native menu and previous/next controls come from configuration.
- Toggle menu visibility, navigate with the arrow keys, and use F11/Escape for fullscreen.
- Start at the configured entry point and return with Home. Minimal package/browser/entry preflight gates initial display; page navigation has a 15-second timeout. Export sanitized diagnostics from the Player header.
- Play packaged local MP4 video and view SVG artwork in the included offline sample.
- Run a bundled offline sample and automated tests for validation, packaging, and tamper detection.

## Package versus executable

The Builder creates a **single `.fisxpkg` file** containing a presentation and its assets. The package is opened by the shared FIS Presentation Studio runtime; it is not a separate Windows `.exe` for every presentation. This demonstrates the reusable-runtime model from the development plan while keeping the presentation distributable as one file.

## Configuration

The [configuration contract](documentation/Configuration/presentation-config-v1.md), [JSON Schema](documentation/Configuration/presentation.schema.json), and [Demo Library design](documentation/DemoLibrary/design.md) define the editor/library contract. The [local frontend](documentation/DemoLibrary/frontend.md) now implements imports/cards/filtering/favorites with a JSON index under LocalAppData/FISPresents/data; SQLite, managed roots, version grouping and user-editable local classification remain future work. Program Files remains an installed/read-only location, not a default standard-user write target.

`presentation.json` is at the package root. The initial prototype uses this contract:

- Presentation identity: `schemaVersion`, `id`, `name`, `description`, `version`, `entryPoint`.
- Branding: `branding.accentColor` and optional local `branding.logo`.
- Menu: `navigation.enabled` and ordered `navigation.sections[].pages[]` with `id`, `title`, and relative `path`. Section order and page order in the JSON are the runtime menu order.

If a selected folder has existing HTML but no config, the Studio creates a starter JSON config and lists discovered HTML files as pages. It does not create HTML. Review the sections/order in Configuration Editor before building.

## Run and build

Requires the .NET 10 SDK and the Microsoft Edge WebView2 Runtime on the Windows machine.

- Open `FisPresentationPlatform.csproj` in VS Code and run/debug the WPF app.
- Or build from the workspace with `dotnet build .\FisPresentationPlatform.csproj`.
- Run tests with `dotnet test tests\FisPresentationPlatform.Tests\FisPresentationPlatform.Tests.csproj`.
- Build a distributable release with `.\tools\Build-Release.ps1 -Version 1.0.0`. It runs the tests, publishes the app and DemoRepositoryTool self-contained for win-x64, adds the guides, and writes `dist\FISPresents-<version>-win-x64.zip`.

On **Build Package**, use **Load sample** to try the included three-page, offline presentation. Its original artwork and six-second MP4 are bundled in the `.fisxpkg`. Choose **Build package** and open the resulting package to test the runtime and media. To start a new branded demo, follow the [authoring guide](documentation/Guides/authoring-guide.md#2-start-from-the-template).

## Current limits

There is no installer or Studio self-update yet, and packages are fingerprinted (SHA-256) rather than signed, so hashes prove integrity against the repository index but not publisher identity. Publishing, retiring and assignments are command-line tasks, and usage data is collected as raw JSONL with no report. Dynamic JavaScript-generated resources still need runtime monitoring. See the [MVP readiness review](documentation/Review/mvp-readiness.md) for the full gap list.

## Tests

The [blueprint assessment](documentation/Review/blueprint-assessment-2026-11-01.md) records the original 17-pass/5-failure acceptance run. The [hardening follow-up](documentation/Review/hardening-follow-up-2026-11-01.md) closes those five defects and documents 45 passing automated tests. The suite now has 149 passing tests (see the [MVP readiness review](documentation/Review/mvp-readiness.md)). Live WPF/WebView2, media, performance and enterprise tests remain separate acceptance work.

The xUnit suite covers config generation and validation, traversal/duplicate IDs, local and remote asset references, development-file exclusions, media MIME metadata and storage, package extraction, and tamper rejection.
