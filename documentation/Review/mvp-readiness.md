# MVP readiness review — FIS Presentation Studio v1.0

**Date:** 2 November 2026  **Build:** 1.0.0 (.NET 10, WebView2)  **Automated tests:** 151 passing

**Recommendation: sign off as MVP v1 for a controlled pilot** (one business line, 10–20 consultants, one shared repository). The product does what it set out to do end to end. Before a **wider rollout**, close the four open *Must* items in section 2. None of them changes the design; they are packaging, operations and governance.

## 1. What v1 delivers

| Area | Delivered | Evidence |
|---|---|---|
| Branded, offline demos | `.fisxpkg` packages that play fully offline in a hardened WebView2 player (local virtual host, no external URLs, navigation guard, diagnostics export). | Runtime hardening tests; bundled demos |
| Demo content | Three story demos (*Designed for Digital*, *The Digital Advantage*, *Confident, Repeatable Delivery*), *Data Lab* and a starter *Template*. | Brand-kit authoring tests |
| Organisation tagging | L3–L8 on every demo; business filter; tile shows L3. | Schema and library tests |
| Assignments | Windows user → L8 solutions; **My solutions** default view. | Repository tests |
| Central repository and cache | Index, assignments and packages in a shared folder; local cache for offline use; SHA-256 verified downloads; update prompt at start; Downloads view with Update all. | Repository and update tests |
| Start-up health | 11 single-row checks (required and optional), with offline fallback. | Startup check tests |
| Library management | Search, sort, favourites, details, recent, import, uninstall (type `delete`), automatic backups. | Library tests |
| Authoring | Build Package with validation; Configuration Studio (General, Menu & Pages, Data Sets, JSON); Save as new demo; tile themes. | Blueprint acceptance tests |
| Data sets and personas | Persona selection on start; `$extends` overrides; `$files`; size limits; in-app create, check, preview and save. | Data set resolver and service tests; Data Lab |
| Deal tracking | Reason, client name, OID, per-session tracking switch; local JSONL queue; background upload when online. | Usage tracking tests |
| Operability | In-app logs, About (versions, stats, support details), display scaling for laptops and desktops. | Manual |
| Documentation | [User guide](../Guides/user-guide.md), [authoring guide](../Guides/authoring-guide.md), [administrator guide](../Guides/admin-guide.md), [data sets](../Configuration/data-sets.md), [executive overview](../Executive/FIS-Presents-Executive-Overview.html). | — |

**Fixed during this review:**
- A demo built from the brand kit failed Build Package validation because `shared/version.js` only existed after CLI publishing. A placeholder now ships in the kit.
- Added the *Template* starter demo and tests that prove the Template, Designed for Digital and Data Lab folders build in-app.
- Corrected stale text (Build Package scope, README limits, config docs).
- **Re-release guard:** the publisher refuses to overwrite a published version unless `--force` is given (gap 3, closed).
- **`retire` command:** withdraws one version, or a whole demo with `--force` (part of gap 6, closed).
- **Test scenario:** `tools\Reset-TestScenario.ps1` backs up the current state and builds a known end-to-end scenario; the [test plan](../Guides/test-plan.md) walks through every feature.

## 2. Gaps found

| # | Gap | Impact | Priority |
|---|---|---|---|
| 1 | **No installer.** `tools\Build-Release.ps1` now produces a self-contained zip (no .NET needed) with the FIS icon and a `deployment.json` that presets the repository; see the [deployment guide](../Guides/deployment-guide.md). Still no MSI/MSIX, Start-menu entry, code signing or Studio self-update. | Fine for a pilot by unzip or script; wide roll-out needs a signed package. | **Should** |
| 2 | **Repository location and access.** Default is `C:\FISDemoRepository`; each laptop must be pointed at the share. No defined read/write permissions. | Pilot users could publish or alter packages; setup per laptop. | **Must** |
| 3 | ~~Re-releasing an existing version overwrites it silently.~~ | Closed: refused unless `--force`. | Done |
| 4 | **L4–L7 taxonomy and assignments are placeholders.** | *My solutions* and filters are only as good as this data. | **Must** (data, not code) |
| 5 | **Pilot acceptance on real FIS laptops** (corporate image, proxy, VPN, WebView2 policy, small screens, projectors). | Unknown environment issues. | **Must** |
| 6 | Publishing and assignment changes are command-line only (`retire` now exists; no in-app publishing or assignment admin). | Needs a technical owner. | Should |
| 7 | Usage is collected as raw JSONL; no report or dashboard. | Value of tracking is not visible to leadership yet. | Should |
| 8 | Packages are fingerprinted, not signed. | Proves integrity against the index, not who published it. | Should (before wide rollout) |
| 9 | Data privacy sign-off for client name and OID in usage files. | Compliance. | Should |
| 10 | No clean-up of the playback cache or browser profile; logs are kept 14 days. | Disk use grows slowly. | Later |
| 11 | No production news-feed URL. | Welcome shows sample news. | Later |
| 12 | Concurrent publishers are not coordinated. | Two simultaneous releases could conflict. | Later (single publisher in pilot) |
| 13 | AI-assisted authoring and converting Coast demos to offline packages. | Faster content production. | Later (v2) |

## 3. Sign-off criteria

**MVP v1 (pilot), met:**
- [x] Consultant can find, download, update, personalise, present and uninstall demos offline.
- [x] Demos are tagged L3–L8 and filtered by assignment.
- [x] Repository vs cache, verified downloads and update prompt work end to end.
- [x] Authors can build a package, edit configuration and data sets, and save as a new demo.
- [x] Usage and deal data are captured with user consent per session.
- [x] User, authoring and administration guides exist.
- [x] Automated tests pass (151).

**Before wider rollout (v1.0 general availability):**
- [ ] Installer (MSIX or MSI) with the runtime and a preset repository path. *(Gap 1–2)*
- [ ] Production repository share with read-only access for consultants and write access for publishers. *(Gap 2)*
- [x] Publisher refuses to overwrite an existing version. *(Gap 3)*
- [ ] Real L4–L7 taxonomy and pilot assignments loaded. *(Gap 4)*
- [ ] Pilot of at least two weeks with five or more consultants; no blocking defects. *(Gap 5)*
- [ ] Privacy review of usage data. *(Gap 9)*

## 4. Suggested next steps

1. **This week:** run the [test plan](../Guides/test-plan.md) and package an installer for the pilot (gap 1).
2. **Pilot (2–4 weeks):** one business line on a real share, real assignments, and a weekly usage export.
3. **v1.1:** usage report, in-app publishing for authorised publishers, package signing.
4. **v2:** AI-assisted authoring and Coast-to-offline conversion.
