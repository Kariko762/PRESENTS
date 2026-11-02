# Blueprint hardening follow-up

Date: 1 November 2026. Follows the [original assessment](blueprint-assessment-2026-11-01.md), which remains a historical record of the failing acceptance run.

## Verified results

All 45 automated tests pass, including the five prior BlueprintGap tests. Application and tests compile with no compiler warnings. [Latest TRX](../../TestResults/hardening-acceptance.trx).

The null-branding test now wraps both deserialization and semantic validation: rejecting null during schema validation is a valid earlier rejection, rather than forcing invalid data through to the typed model. Its rejection/no-NullReference requirement was retained.

## Acceptance defects closed

- A01: required identity fields are enforced by the actual embedded JSON Schema before typed defaults are populated.
- A02: explicit nulls and invalid types reject with schema instance paths. Typed invalid config is also schema-checked before dereferencing groups.
- A03: enabled menu requires at least one configured page.
- A04: menu references must be HTML or the supported direct-media entry point; arbitrary files reject.
- A05: manifest package ID, presentation ID and version must match configuration before cache registration/extraction.
- N01: a shared, tested navigation plan starts the configured entry page, including when it is later in the menu, outside the menu or navigation is disabled. Native Home returns there.

## Package hardening

- Injectable limits: 10,000 archive entries, 8 GiB total expanded bytes, 4 GiB per file, 8 MiB manifest, 1 MiB configuration; compression ratio above 1000 is rejected for files larger than 1 MiB. These are development policy defaults, not measured performance guarantees.
- Budgets, duplicate/case-colliding archive paths and unsafe paths are checked before extraction; manifest duplicate entries and malformed hashes reject cleanly.
- Source scanning rejects symbolic links/junctions. ZIP Unix symbolic-link entries reject. Windows reserved names, colon/alternate streams, trailing dots/spaces and traversal segments reject.
- Source output is written to a unique staging archive and verified against the same budgets before replacing a prior package. Cancellation during assembly removes staging and preserves previous bytes.
- Extraction uses a read-only open handle for hashing and archive processing, with a free-space check and a final staging-directory move.
- Extraction layout is now Cache/<presentation-id>/<package-sha256>/<launch-id>. Each open gets its own isolated content directory, preventing same-version packages or concurrent players from overwriting running content. Player close attempts to remove its launch directory after browser disposal.
- The browser profile is separate per session under BrowserProfiles/<session-id>.
- Nested assets named presentation.json/manifest.json are retained; only root metadata is treated specially.

This is not a complete defense against hostile local users changing files between checks. Filesystem ACLs, publisher trust/signing, cross-process installation policy and retention/abandoned-session cleanup still require work. No signed updates or Demo Library are delivered here.

## Minimal runtime preflight and diagnostics

The Player records sequential checks for package/config integrity, WebView2 initialization and entry-page navigation. A failed check prevents launch; the initial content remains hidden until entry navigation succeeds. PASS/FAILED/BLOCKED results include durations and severity. This is a minimal sequential gate, not the full dependency-aware preflight engine from the blueprint.

Browser navigation is limited to configured runtime pages at the local HTTPS origin. There is a 15-second timeout; later page failures show a runtime-owned error view, with a retry action for sessions that previously passed startup. Startup failures require reopening the Player. Home and diagnostics-export controls are available in the native header.

Fixed diagnostic event types capture navigation duration/failure, timeouts, process failure and reported JS/resource errors. Script instrumentation reports only error kinds, not page content, full URLs, tokens or arbitrary JS error text. Persistent logs are capped at 500 events per session. Diagnostics export includes preflight statuses/timings and events, omitting raw exception text. Logging I/O failures do not stop playback.

Locations: per-session JSONL logs under LocalAppData/FIS/PresentationRuntime/Logs; JSON export through Player's Diagnostics button. Browser profiles and abandoned logs/content do not yet have retention cleanup.

Resource/JS error reports are diagnostic warnings, not proof that content is ready or correct. The entry navigation check proves successful browser navigation only. Remote subresource allowlisting, full browser smoke/error policy, connectivity checks, approved runtime minimum versions, renderer restart and analytics remain future work. Browser initialization depends on installed WebView2; no silent installation was added.

## Automated evidence

New regressions cover schema defaults/nulls/tag bounds, Windows unsafe/safe paths, entry selection in/out of menu and with menu disabled, archive count/expanded size, duplicate manifest entries, ZIP symlinks, concurrent package opens, different hashes at the same version, cancellation during assembly, nested assets, preflight failure gating and persisted diagnostic export.

These tests do not run WPF/WebView2. Live hidden-first-navigation, timeout/retry behavior, JS/resource reporting and H.264 media play/seek still require an interactive Windows run. The previously reported navigation slowdown has not been benchmarked or proven fixed; timing diagnostics now provide more evidence.

## Next

1. Exercise the Player on Windows with reordered entry pages, real media, a stalled page and renderer/resource failures.
2. Add runtime resource policy, profile/cache/log retention and a proper error/preflight dashboard.
3. Resolve editor unsaved changes and config/catalog controls before implementing verified Demo Library imports.
4. Keep deployment/signing and enterprise testing as separate release gates.