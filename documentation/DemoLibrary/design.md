# FIS Presents: configuration editor and Demo Library

Status: proposed implementation contract, 1 November 2026. The library described here is not implemented yet. The existing Studio packages folders, edits config, and opens packages.

## Product boundaries

Studio has two author workflows: Package Builder and Configuration Editor. Demo Library is the catalog/launcher for their outputs, not an HTML editor. Dataset management is deferred.

Creators maintain HTML, CSS, scripts and media in external tools. Studio edits JSON, validates existing content and builds packages. Player owns navigation, loading and runtime diagnostics. Imported packages are immutable; editing a presentation creates an editable workspace and a new package version.

## Configuration contract

Keep the current flat v1 contract rather than introduce a second incompatible interpretation. See [v1 specification](../Configuration/presentation-config-v1.md) and [JSON Schema](../Configuration/presentation.schema.json).

| Group | Fields | Editor |
| --- | --- | --- |
| Identity | schemaVersion, id, name, description, version, entryPoint | Details form, existing entry-file picker |
| Branding | accentColor, logo | Color picker, local bitmap picker, header preview |
| Catalog | product, functions[], tags[], coverImage | Primary-product field, label chips, existing cover picker, library-card preview |
| Navigation | enabled, sections[].id/title/pages[] | Section/page tree, ordering controls, runtime-menu preview |
| Page | id, title, path | Stable ID, displayed label, existing file picker |

Required identity fields: schemaVersion, id, name, version, entryPoint. Optional groups use documented defaults. Navigation enabled requires at least one configured page; disabled allows no sections. Section IDs are unique within a presentation; page IDs are unique across all sections. IDs survive label changes. Array order is authoritative; do not add a competing numeric order field.

Tags/functions are trimmed labels, case-insensitively deduplicated, with at most 30 labels per list and 80 characters per label. Product is one primary product/family in v1, max 80 characters. Use tags for additional product labels until a versioned multi-product model is approved. Blank catalog fields are allowed; show “Unclassified” rather than invent tags.

Path values use forward slashes relative to package root; no machine-specific paths. Reject traversal, device paths, reserved Windows names, alternate data streams, unsafe links and case-colliding files. Cover/logo images must be readable supported local bitmaps. Catalog artwork must not need network access. Missing cover uses a branded fallback tile.

Static validation and browser validation are distinct. Schema validates structure; semantic validation handles cross-field rules, IDs and paths; package validation verifies content/hashes. Browser checks validate execution, not just presence.

The schema is now documented but is not yet wired into runtime JSON Schema validation. Current C# checks cover only part of these constraints. Required implementation work must align defaults/limits and null handling across schema, editor and runtime.

Do not accept fields with no runtime behavior. Resolution, guided modes, conditional availability, completion rules, endpoints, updates, analytics, dataset references and richer security permissions are future versioned extensions, not silent v1 placeholders.

## Full editor behavior

- One document model shared by form/tree/JSON views; no stale copies competing to overwrite edits.
- Explicit Apply JSON with structure validation and precise property-path errors. Failed parsing preserves the last good document.
- All section/page properties editable, including IDs; move pages between sections and reorder with buttons/drag-and-drop.
- Existing file pickers only. Removing menu entries never deletes content files.
- Unsaved-change prompt on folder change, reload, workspace close and app exit. Undo/redo and reset-to-saved.
- Separate Save draft (structurally valid config with missing-content warnings) from Validate/Build (all mandatory checks pass).
- Atomic config saves; backup prior valid config. Preview menu/card without executing presentation scripts.
- New config generated in memory for approval, not silently saved when browsing folders.

## Storage decision

Requested location: C:\Program Files\FISPresents\data. Windows normally protects Program Files; a standard-user app cannot assume it can write there. Do not run Studio/Player elevated or weaken Program Files ACLs to solve this.

Recommended deployment layout:

| Location | Contents | Writer |
| --- | --- | --- |
| Program Files\FISPresents | Installed app, approved bundled read-only demos | Installer / endpoint management |
| ProgramData\FISPresents\data | Shared managed demo library | Approved publisher/installer; permissions set during deployment |
| LocalAppData\FISPresents\data | Personal imported demos and editable workspaces | Current standard user |
| LocalAppData\FISPresents | Local library index, preferences, thumbnails, diagnostics/cache | Current standard user |

Program Files\FISPresents\data can remain an optional read-only managed library source if that exact path is required. A machine-wide writable ProgramData library is supported only when IT provides deliberate access controls; it is not automatically writable. Initial default: user-writable LocalAppData, plus optional managed-library discovery. Never silently change a selected storage root on failure; show a clear permissions error and an explicit alternative.

Inside a library root:

    packages/<presentation-id>/<version>/<package-sha256>.fisxpkg
    covers/<package-sha256>.png
    staging/<import-id>/

Editable workspaces live separately from immutable packages. Content extraction caches are keyed by package hash, not only ID/version, to avoid collisions and concurrent playback changes. Database and user preferences remain writable even when managed demos are read-only.

## Portable versus local metadata

Portable catalog metadata comes from presentation.json and travels in the package. Local annotations (extra tags/functions/product overrides, favorites, notes) live separately in the user database and do not alter package bytes or hashes. The UI labels overrides explicitly and offers Reset to packaged metadata.

Editor classification changes become portable after rebuild/reimport. Library tagging defaults to local annotation; do not pretend those changes are already in an exported original package. Local-only tags are preserved across index rebuilds and package upgrades by presentation ID, with per-version exceptions only when explicitly selected.

## Library records

Use SQLite for the local catalog; no analytics transmission. Core entities:

- Demo: stable presentation ID, active package hash, favorite, local overrides.
- PackageVersion: presentation ID, version, package hash, managed/user origin, safe relative package path, byte size, import timestamp, verification state, extracted portable metadata.
- Classification: normalized tag/function values and demo joins; retain friendly display labels.
- LaunchHistory: package hash, last launched timestamp, local launch result (no personal profiling).

Enforce uniqueness on package hash; group versions into one demo card. Same ID/version with different hashes is a conflict requiring a user decision, not automatic overwrite. No blind latest-version selection: activate only after verification and compatibility checks. Retain last known-good package.

The content index is rebuildable from packages; local annotations and favorites are not disposable cache and need backup/export. Discover demos registered under configured roots, not arbitrary executable files across the machine.

## Visual library

Home view: responsive card grid with cover, name, product, function chips, version, offline capability when actually verified, and verification badge. Actions: Launch, Details, Favorite, Edit config (editable workspace), Reveal file, Remove local copy. Managed copies are read-only and explain ownership.

Search title/description/product/functions/tags; combinable product/function/tag filters; Favorites and Recently used views. Sort by name, product, import date or last launched. Show a count and empty-state Import package / Build from folder actions. Details include version history, origin, size, page count, verification result and local-versus-packaged metadata.

## Import / build-to-library lifecycle

1. Select package or build source to temporary output.
2. Enforce archive entry-count, expanded-size and compression-ratio limits; reject unsafe paths, links, duplicates and malformed manifests.
3. Read/validate config and verify exact archive contents/hashes. Hashes prove consistency, not publisher trust.
4. Determine identity/version/hash and duplicate/conflict behavior.
5. Stage immutable package and generate a bounded thumbnail from a validated bitmap (no hidden HTML execution).
6. Commit files and SQLite transaction with recovery markers for interruption; preserve previous active package on failure.
7. Register the demo and show its card. Launch only validated files via Player.

Removing a local demo never deletes source folders or managed copies. Confirm removal scope: selected version versus all local versions. Rebuild index recovers valid packages and reports missing/corrupt ones. Do not launch corrupt/missing copies.

## Delivery sequence

1. Contract alignment: approve v1 fields/defaults, add executable schema validation, semantic/path tests and migration rules. Preserve existing v1 packages with absent catalog defaults.
2. Complete editor: product/function/tag/cover controls, editable IDs, cross-section page moves, unsaved-change handling and one synchronized document model.
3. Library backend: configurable roots, SQLite, verified import, safe version storage, local annotations, duplicate/conflict handling and recovery.
4. Library UI: cards, search/filter/sort, launch/details/favorites, managed/user origin and Edit workspace action.
5. Packaging integration: Build and add to library, export package, reimport edited version, thumbnail generation and deployment permission tests.

## Acceptance tests

- Old v1 package without catalog loads with defaults; unsupported version and explicit null mandatory data reject cleanly.
- Form/JSON round trips preserve all supported fields and menu order; malformed edits preserve last good config.
- Tags/functions trim and deduplicate case-insensitively; oversized labels/lists report property paths.
- Save/reload and moving pages between sections preserve IDs; deleting menu items never changes HTML/media bytes.
- Import valid offline HTML/image/video demo; launch with source folder removed and network disabled.
- Duplicate hash produces no duplicate card; same ID/version different hash presents conflict; version changes preserve favorites/tags.
- Interrupted import/build and disk-full leave previous working packages/index intact.
- Bad hashes, extra archive files, traversal, case collisions, reparse points, archive bombs and malicious cover files reject safely.
- Read-only Program Files root never requests elevation; library works for standard users under configured writable storage.
- Search plus combined filters returns correct cards; local metadata is visibly distinct from packaged metadata.
- Index rebuild preserves annotations and flags missing files. Removing local copies leaves managed copies and author workspaces untouched.
- Thumbnail/card layout is responsive and accessible; large-library queries are paged and do not decode every cover on the UI thread.