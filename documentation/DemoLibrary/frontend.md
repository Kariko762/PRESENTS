# Local Demo Library frontend

The Studio now starts on Demo Library. The screenshot-inspired layout uses real imported demo packages. Show all demos, Refresh, Downloads and Import sit on the right of the library tab strip. Action feedback appears as a short toast at the bottom of the workspace and is written to the [log](../Settings/settings.md#logs). There is no library status bar.

## Available

- Import a .fisxpkg: stage a copy, verify archive/config/hashes with PresentationService, then store the package locally and register its metadata.
- Cards show name, description, primary product, function/tag labels, version and import verification status. Missing packages are marked and cannot launch.
- CoverImage from config displays as a bounded bitmap thumbnail; absent/unreadable covers show a green branded fallback.
- Search all words across title, description, product, functions and tags; combine product filter and Favorites. Sort by name, import date or launch-request date.
- Favorite/unfavorite persists. Recent shows demos for which a launch was requested; it does not claim successful presentation completion.
- **View Details** is a rich modal. It shows:
  - the cover banner, product and version
  - the description
  - the L3 › L8 organization path as chips, with the L8 solution highlighted
  - function and tag chips
  - personas and data sets
  - the agenda by section
  - package facts: status, size, imported and last-launched dates, id and location

  **Launch demo →** runs the normal launch flow.
- **Launch Demo** opens the *demo start* dialog first. The consultant:
  - picks a persona / data set card (from the demo's `dataSets`; the default is preselected). See [data sets](../Configuration/data-sets.md).
  - chooses a demo reason (Client demo, Internal training, Rehearsal, Partner enablement, Event / conference, Other)
  - enters a client name and an Opportunity ID
  - switches *Track this session* on or off

  The last values are remembered while Studio is open. The Player then rechecks the package and injects the selection as `window.FIS_DEMO_CONTEXT` (see [presentation config](../Configuration/presentation-config-v1.md#data-sets-and-personas)). The Opportunity ID is only logged; it is never passed to the page.
- The favorite star is a centred 34 px circle that turns gold when set.
- Build Package has an explicit Add completed package to Demo Library checkbox. Library import failure does not discard successful build output. A built package replaces any other installed version of the same presentation ID, carrying favorite and launch history forward.
- Each card has a ⋯ options button (a translucent grey square beside the favorite star) with:
  - **Edit configuration**: extracts the verified package into an editable working copy at `workspaces/<presentation-id>` (generated `__fis_runtime` files are left out) and opens it in the Configuration Studio. This menu item is the only way into the Configuration Studio; it has no sidebar entry. The studio shows only the page title, its General / Menu & Pages / JSON tabs and a footer. There is no workspace tab, source-folder picker or intro text, because the source is always the demo's working copy. The footer has Validate, **Save as new demo…** and Save Configuration.
  - **Save as new demo…** asks for a new name, ID (suggested from the name) and version (default 1.0.0). The ID must not be used by an installed demo or an existing working copy. You then choose where to save the `.fisxpkg` (default `Documents\FIS Presents`). Studio copies the working copy to `workspaces/<new-id>`, writes the new identity into its `presentation.json`, builds and verifies the package, adds it to the Demo Library, and keeps editing the new demo. The original demo and its working copy are unchanged. The new demo is local only until it is published to the repository. Save, then Build Package with Add completed package to Demo Library checked to update the installed demo. An existing working copy is reused. If it was made from a different version than the one installed, Studio asks whether to replace it; closing that prompt keeps it.
  - **View details**: same as the View Details button.
  - **Uninstall demo…**: removes every installed version of the demo, its cover, its favorite and launch history, and its working copy. The Uninstall button stays disabled until the user types `delete` into the confirmation box. If the working copy is open in the Configuration Studio, the editor is cleared. Repository demos can be downloaded again from Downloads.
- Empty view offers import or installation of the actual bundled offline image/video sample.

## Storage

Personal storage is LocalAppData/FISPresents/data, writable by the current standard user. No Program Files elevation is attempted.

    library.json
    packages/<package-sha256>.fisxpkg
    covers/<package-sha256>.<bitmap-extension>
    workspaces/<presentation-id>/   editable working copies
    staging/

The frontend uses a small atomically replaced JSON index as an initial persistence adapter. SQLite, index recovery, managed roots and transactional cross-process import are not implemented. Do not treat this adapter as the final enterprise library backend.

Exact duplicate hashes reuse one entry. Different versions or different same-version hashes remain separate cards and never overwrite each other's packages. Demo grouping, conflict resolution and active-version policy are future work. Favorites are per-package in this adapter, not yet per presentation across upgrades. Preserve library.json to preserve favorites/recent information.

Import verification is not publisher signing or a promise that files cannot later change. Player validation runs again at launch. Library filtering does not assert offline compatibility for arbitrary demos. Repository downloads and updates are covered in [repository](repository.md). Local metadata overrides and user-editable library tags remain future functions.

Current thumbnail decoding is bounded but on the UI thread and card filtering uses an in-memory list; large-library paging/background image loading is still required.

## Test evidence

DemoLibraryTests covers valid import, persistence, byte-identical deduplication, favorites/launch-request times, missing-copy restoration, invalid package cleanup, combined classification filtering, editable working copies (create, reuse, replace, rebuild replaces the installed version) and uninstall (all versions, files and working copy). VisualThemeTests renders a real WPF library card from test metadata, tests sidebar switching and a no-match search. It does not launch WebView2.