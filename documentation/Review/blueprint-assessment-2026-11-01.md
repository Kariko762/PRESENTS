# Blueprint implementation review

Date: 1 November 2026. Baseline: original attached “FIS Interactive Presentation Runtime & Packaging Platform” v1.0 development blueprint, with later decisions that Studio packages existing files and edits config, datasets are deferred, and a Demo Library is planned.

## Verdict

The local directory → package → integrity validation → native navigation → playback concept is implemented. It is a development prototype, not the blueprint's successful first release and not enterprise-ready.

The first-release definition requires the runtime to establish readiness before presentation display, monitor execution, and produce diagnostics. Those capabilities are not complete. A green unit-test suite alone is not proof of the complete first-release journey.

Production code was not changed during this review. Acceptance tests and this assessment were added to expose gaps rather than redefine passing criteria to match the implementation.

## Tests actually executed

- Baseline solution tests: 10 passed, 0 failed.
- Expanded solution tests: 22 executed, 17 passed, 5 failed, 0 skipped.
- C# and XAML compile successfully. The expanded test run exits unsuccessfully because the acceptance assertions fail, not because compilation fails.
- Report: [TRX results](../../TestResults/blueprint-acceptance.trx).
- New tests: [BlueprintAcceptanceTests.cs](../../tests/FisPresentationPlatform.Tests/BlueprintAcceptanceTests.cs).
- Existing tests: [UnitTest1.cs](../../tests/FisPresentationPlatform.Tests/UnitTest1.cs).

The five failures are intentionally retained as open acceptance defects under the `BlueprintGap` category. They are not skipped. A normal full solution test run is now red until these requirements are fixed.

### Passing evidence

The baseline checks cover packaging/extraction, tampered content, asset discovery, static references, missing paths/duplicate IDs, starter config, array-order serialization, stored media and generated media wrappers.

New checks demonstrate:

1. Unknown JSON properties reject on deserialization.
2. Unsupported schema versions produce validation errors.
3. Extra archive files reject before extraction.
4. An unmanifested traversal entry rejects before extraction.
5. A build cancelled before starting preserves the previous output bytes.
6. A package can be extracted/validated after its source directory is removed.
7. The real sample SVG and MP4 survive packaging/extraction byte-for-byte.

Important limits: cancellation is tested before work starts, not mid-compression or disk-full. The traversal test does not cover all Windows path/reparse variants. Byte-preserving real media does not prove decoding, video seeking, visible rendering or playback inside WPF/WebView2. Existing synthetic MP4 tests use arbitrary bytes and cannot establish codec compatibility.

## Confirmed acceptance failures

| ID | Requirement | Observed result | Priority |
| --- | --- | --- | --- |
| A01 | Mandatory config fields must be present | Deserializing `{}` supplies model defaults rather than rejecting missing required fields | High |
| A02 | Invalid data types/nulls produce useful validation errors | Explicit null branding reaches validation and throws NullReferenceException | High |
| A03 | Enabled navigation has a meaningful page structure | A section with no pages validates successfully | Medium |
| A04 | Menu references are presentation pages | An existing notes.txt is accepted as a menu page | Medium |
| A05 | Manifest presentation identity agrees with config | Changing manifest presentation ID without changing config is accepted on extraction | High |

Evidence: [contract failures](../../tests/FisPresentationPlatform.Tests/BlueprintAcceptanceTests.cs#L15-L69), [deserialization/validation](../../Services/PresentationService.cs#L14-L148), and [extraction](../../Services/PresentationService.cs#L267-L354).

## Requirement coverage

“Implemented” means code exists; only checks listed above are automated evidence. “Partial” means some behavior exists but the blueprint requirement is not fully met.

| Blueprint area | Status | Current evidence / missing portion |
| --- | --- | --- |
| WPF + WebView2 desktop host | Implemented | PlayerWindow initializes WebView2 and maps local files; enterprise device approval not established |
| Native folder selection | Implemented | Studio selects existing folders and scans content |
| Single presentation per folder | Implemented | One root presentation.json and entry point |
| Config discovery/generation | Partial | Generates starter JSON from existing files but silently saves while selecting a folder |
| Authoritative config model | Partial | Shared model exists; runtime schema validation is not wired up; required/default/null semantics differ |
| JSON Schema | Partial | Schema document exists; application uses deserialization plus handwritten semantic checks, not execution of the JSON Schema |
| Property-path validation errors | Partial | Some property names appear, but not consistent indexed paths for all fields |
| Full config editor | Partial | Identity/branding, menu enabled, multiple sections/pages, ordering, JSON; no visual catalog controls, page/section ID edits, cross-section moves, undo or unsaved-change protections |
| Directory inventory | Partial | Paths, extensions, size, category; not full modified-date, referenced/declared classification or configurable exclusions |
| Static dependency inspection | Partial | AngleSharp parses HTML; common CSS URLs use regex; dynamic JS, complete CSS syntax/imports and remote availability checks not covered |
| Versioned ZIP .fisxpkg | Implemented | Config, manifest, content; hashes verified before output replacement |
| Manifest contract | Partial | Per-file size/hash/type and build timestamp/version; lacks explicit config hash field and trust/signing; embedded identity consistency incomplete |
| Archive validation/extraction | Partial | Exact file list/hash checks and lexical traversal protection; staging/swap rollback; no entry/expanded-size limits or robust Windows reparse protections |
| Build report/log | Partial | UI messages and progress, no persistent build report/log export |
| Source-independent execution | Partial | Extraction after deleting source proven; independent approved Windows-device playback not tested |
| Local image/video packaging | Implemented | Real assets preserved; actual packaged WPF video/seek test pending |
| Offline operation | Partial | All-local sample; no runtime offline policy/classification or mandatory remote dependency decision |
| Native sidebar, sections, previous/next, progress | Implemented | Configuration-driven controls outside HTML |
| Home, correct entry-page launch | Partial | Player starts first menu item rather than explicitly resolving entryPoint; no native Home control |
| Top/immersive/guided navigation | Partial | Menu can collapse and fullscreen works; no configured modes, branching/completion policies |
| JavaScript runtime API | Missing | No validated navigation/event/fullscreen messaging bridge |
| Pre-flight check framework | Missing | No modular checks, dependency graph, statuses/severities/readiness evaluator |
| Integrity/config checks before launch | Partial | Checks run before WebView init, but contract gaps above remain |
| Environment/runtime requirements | Missing | No configured minimum runtime/WebView2 version, storage/permission preflight |
| Connectivity/endpoint checks | Missing | No approved HTTPS response checks, proxy-aware classification or endpoint severity |
| Asset resolution/recovery | Missing | No declared optional/mandatory resources, verified remote/cache fallback |
| Hidden browser smoke validation | Missing | Entry loads directly; no readiness check for JS/resource faults before reveal |
| Browser error monitoring | Partial | Navigation result/timings/process failure only; no JS console/errors, resource failures or persistent diagnostics |
| Browser recovery | Partial | Error screen for startup/process failure, no retry/reinitialization/state recovery; navigation failure is footer-only |
| Page-load budgets | Missing | No timeout/watchdog or measured performance acceptance; reported five-second issue is unresolved |
| WebView security restrictions | Partial | Denies permissions/downloads/new windows, disables dev tools, filters top-level host; no remote-resource allowlist or per-demo browser profile isolation |
| Standard-user operation | Partial | LocalAppData extraction; enterprise managed-device and filesystem ACL tests pending |
| Publisher trust | Missing | No signed packages/manifests or approved trusted distribution mechanism |
| Package lifecycle/updates | Missing | Extraction cache exists, but no trusted update sequence, active pointer/version policy or retention |
| Structured logging / internal event collector | Missing | No logging infrastructure/event abstraction/exported diagnostics |
| SQLite/local telemetry queue/API | Deferred | Not implemented; should remain deferred for analytics |
| Signed MSIX/approved installer | Missing | Development build only; no deployment/signing approval evidence |
| Demo Library | Planned | Catalog model and documents only; no import/index/card UI/tag persistence |
| Dataset management | Deferred | Later scope, not an acceptance blocker for this slice |

Source examples: [runtime](../../PlayerWindow.xaml.cs), [packaging/validation](../../Services/PresentationService.cs), [editor](../../MainWindow.xaml.cs), [schema](../Configuration/presentation.schema.json), [models](../../Models/PresentationConfig.cs).

## Additional verified issues from code review

### N01 — entry point is not necessarily the launch page

[Player startup](../../PlayerWindow.xaml.cs#L108-L113) loads index zero of the flattened menu. When a configured entry point is not first, another page opens. When navigation is disabled but configured pages remain, those pages still determine startup. Fix startup selection independently of menu ordering; test entry both in and outside menu.

### S01 — browser navigation restrictions do not govern all resource requests

[NavigationStarting](../../PlayerWindow.xaml.cs#L134-L148) checks the host only; there is no HTTPS-only check or declared-page allowlist, frame/resource policy or configured approved external domains. Scripts/fetch/fonts/iframes can request remote resources despite a local top-level page. DenyCors virtual mapping is not a blanket network deny policy. Add least-privilege resource policy and real WebView tests.

### S02 — storage/cache isolation and concurrency

[Extraction destination](../../Services/PresentationService.cs#L279) is based on sanitized manifest ID/version, not package hash. Different packages sharing these values target the same directory and backup name. Reopening a package moves/deletes files that another running Player may be using. Add hash-addressed immutable content directories and serialized installation/cleanup; test two concurrent players and conflicting packages.

### S03 — bounded archive and filesystem handling

Lexical relative-path validation exists, but there is no entry count, manifest size, per-file/expanded-byte limit, decompression budget or disk-space guard. Source traversal uses recursive file enumeration without explicit reparse-point rejection. Validate Windows reserved names, trailing dots/spaces, aliases and symlinks/junctions. Do not infer these protections from a simple StartsWith check.

### C01 — schema, runtime and docs disagree

The target schema allows labels of 80 characters and up to 30 per list; C# rejects labels over 60 but imposes no list count. Schema uses two-character IDs for sections/pages; C# permits one. Product and title limits are not consistently enforced. JSON Schema defaults describe defaults but do not themselves populate documents. Align one executable contract rather than add more UI fields over contradictory validators.

### E01 — configuration changes can be lost

There is no unsaved-change prompt/undo. Page label/path controls require Apply; choosing another section/page or building can discard unapplied text. Metadata dirty flags can overwrite raw JSON fields when both are edited. Protect a single document state and test both editing directions and folder/reload transitions.

### E02 — current build is not strictly “no HTML generation”

Ordinary HTML sources are copied, not authored. However direct-media entry points cause [CreateMediaPlayerPage](../../Services/PresentationService.cs#L438-L451) to generate wrapper HTML. This was an earlier feature and remains present despite the later user scope. Decide explicitly: remove media-only wrapper generation from Studio, or define it as runtime hosting rather than content authoring. Local media embedded in externally authored HTML should remain supported.

## Security interpretation corrections

- Excluding manifest.json from its own hash list is not itself a defective self-hash design. A manifest cannot trivially hash itself. Missing publisher authenticity must be addressed with a signature or trusted external expected hash, not a self-referential hash.
- Unknown JSON properties are already rejected by UnmappedMemberHandling.Disallow; the passing test confirms it. Schema enforcement is still missing for required properties and bounds.
- Config is revalidated after extraction; do not claim the Player blindly trusts paths without that check.
- No standalone WebView2 “sandbox attribute” should be invented. Evaluate documented WebView2 settings, navigation/resource policy, profiles and approved capabilities.
- HTML strings in WPF TextBlocks are displayed as text, not executed as HTML. Intended JS execution is not by itself XSS. Network policy and trust still need hardening.
- Authentication, encrypted presentation payloads and adaptive video streaming are not mandatory for this local proof of concept. Prioritize blueprint requirements rather than unrelated infrastructure.

## Phase gate assessment

| Phase | Gate |
| --- | --- |
| 0 feasibility | Local host proven in development; approved enterprise deployment/standard-user device evidence missing |
| 1 package/basic runtime | Substantial implementation, incomplete: build reports, structured logging/events and full acceptance evidence missing |
| 2 native navigation | Basic flow implemented; entry behavior, configured modes and bridge incomplete |
| 3 preflight | Not delivered as an engine/readiness policy |
| 4 remote assets/offline fallback | Local bundling works; resolver/cache/recovery missing |
| 5 updates | Not implemented |
| 6 automated interaction/performance QA | Not implemented |
| 7 analytics | Deferred |

## Recommended next tranche

1. Close A01–A05, align schema/runtime semantic rules, and make all 22 tests green without weakening assertions.
2. Fix N01; add runtime tests for start page, failed page, menu disabled, keyboard navigation and unsaved editor fields.
3. Add archive budgets, safe source traversal and hash-keyed package isolation. Establish publisher-trust policy before untrusted distribution.
4. Implement minimal preflight results/readiness evaluator plus structured local logs and resource/JS error monitoring. Add a page timeout and diagnose the reported navigation delay with actual runtime measurements.
5. Complete config/catalog editor with IDs, cross-section moves, atomic saves and unsaved-change protection; then build the Demo Library on verified package import.
6. Gate enterprise distribution on signing, installer/WebView2 approval and standard-user/proxy/offline tests.

## Manual / WebView acceptance still needed

- Independent Windows installation: open sample package with original source unavailable and network disabled.
- Entry page different from first menu page, navigation disabled with existing sections, failed HTML navigation and rapid/keyboard navigation.
- Packaged SVG/raster image display; H.264 MP4 play/pause/seek/fullscreen; codec failure produces a useful runtime diagnostic.
- JS exception, broken CSS/image request, unavailable required API and stalled load: expected block/warn/recovery policy must be explicit.
- Representative package/page sizes with measured load times; no performance claim until tested on target devices.
- Renderer termination, interrupted extraction, low disk, restricted permissions, enterprise proxy/TLS inspection and installed WebView2 compatibility.

No new WPF UI, packaged WebView2 video, browser execution/error or enterprise device tests were run in this review. Service assertions and source inspection must not be reported as those tests passing.