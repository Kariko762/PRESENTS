# Demo repository, downloads and updates

Studio treats one shared folder as the **demo repository** (the source of truth) and the local library as a **cache** of installed demos. The default repository is `C:\FISDemoRepository`. Change it in Settings › Remote Repository › Demo repository. It can be a local folder or a UNC share, but it must not sit inside the library, cache or backup folders.

## Repository layout

```
C:\FISDemoRepository\
  repository.json        index of published demos and versions
  assignments.json       Windows user → assigned L8 solutions
  packages\<id>\<version>\<id>-<version>.fisxpkg
```

`repository.json` lists each demo's `id`, `name`, `description`, `accentColor`, `organization` (L3–L8) and `versions[]`. Each version entry has `version`, `package`, `sha256`, `bytes`, `publishedUtc` and `releaseNotes`. The latest version is chosen by semantic-version order, so 1.10.0 sorts above 1.9.0 and a release sorts above its pre-release.

`assignments.json` maps users to the solutions they demonstrate:

```json
{ "schemaVersion": "1.0", "assignments": [ { "user": "CORP\\alex", "solutions": ["Instant Payments Hub"] } ] }
```

A `DOMAIN\user` entry matches only that domain. A bare `user` entry matches the user in any domain. Values are L8 solution names.

## Organization tags

Each `presentation.json` carries `catalog.organization` with fields `l3` to `l8`. L3 is the business line: Capital Markets, Banking, Payments, Merchant Solutions or Corporate. L8 is the actual solution. Library and Downloads filter by L3 business. **My solutions** narrows both views to the current user's assigned L8s. It is checked by default when the user has assignments and disabled when they have none.

## Repo versus cache

- On load, the index and assignments are read from the repository and copied to `<library>\repository-cache\*.cache.json`. When the repository is unreachable, the cached copies are used and the UI says so.
- Downloads copy the package into `repository-cache\downloads` and verify its SHA-256 against the index. They then import it through the normal verified library path and check that the package id and version match the index entry. Older local versions of the same demo are then retired, with favorite and last-launched state carried over. A hash or identity mismatch installs nothing.
- Compare states: **Not installed**, **Update available**, **Up to date** and **Local is newer**.

## Startup

The startup checklist adds three repository checks: Demo repository, User assignments and Demo updates. Each tile is a fixed 50 px header; details are in the tile tooltip. If updates are available after the checks, Studio asks **"Yes, update"** or **"Not now"**. Yes downloads and installs them with progress. Nothing is installed without the prompt. The old bundled performance examples are removed from the library once on first run of this version (`.legacy-examples-retired` marker in the library root).

## Downloads view

The sidebar **Downloads** button lists every published demo with its business, state and assignment badges. You can search, filter by business, use My solutions, show updates only, download or update a single demo, or use **Update all**. Refresh re-reads the repository.

## Bundled demos

The three demos in `samples/Demos` tell one story about digital content for pre-sales consultants. *Data Lab* (`fis-data-lab`) is the data-set reference and test bench, and `samples/Demos/Template` is the starter for new demos (not published by `init`). All of them share the FIS brand kit in `samples/Demos/shared`. Step-by-step guides: [authoring](../Guides/authoring-guide.md) and [administration](../Guides/admin-guide.md).

| Demo | Angle | L3 | L8 |
|---|---|---|---|
| Designed for Digital (`fis-designed-for-digital`) | What we can design: journeys, live data, scenarios, components | Payments | Instant Payments Hub |
| The Digital Advantage (`fis-digital-advantage`) | Why digital content beats static decks: currency, reach, impact | Banking | Digital Account Origination |
| Confident, Repeatable Delivery (`fis-confident-delivery`) | How consultants prepare, rehearse, deliver and improve | Capital Markets | Trade Lifecycle Manager |

The L4–L7 values are illustrative placeholders until the real taxonomy is supplied.

## Publishing tool

`tools/DemoRepositoryTool` is a console tool. It stages a demo folder with the shared brand kit and a generated `version.js`, renders a branded cover, builds a verified package and updates `repository.json` atomically.

```powershell
$tool = "tools\DemoRepositoryTool\bin\Debug\net10.0-windows\DemoRepositoryTool.exe"
& $tool init C:\FISDemoRepository                 # publish the bundled demos (3 story demos + Data Lab) at 1.0.0 and assign the current user
& $tool release C:\FISDemoRepository fis-digital-advantage 1.1.0 "Release notes"   # refuses an existing version unless --force
& $tool retire C:\FISDemoRepository fis-digital-advantage 1.1.0                     # withdraw a version (--force and no version: the whole demo)
& $tool install C:\FISDemoRepository "$env:LOCALAPPDATA\FISPresents\data" fis-designed-for-digital 1.0.0
& $tool assign C:\FISDemoRepository "CORP\alex" "Instant Payments Hub" "Trade Lifecycle Manager"
& $tool list C:\FISDemoRepository
```

### Update test scenario

1. Run `init` against the repository. Then `install` Designed for Digital and Digital Advantage at 1.0.0 into the library.
2. Run `release` for Digital Advantage at 1.1.0.
3. Start Studio. The Demo updates check reports one update, and the prompt offers to update The Digital Advantage. Choose **Yes, update**.
4. Open Downloads. Confident Delivery shows as available (it is not assigned, so clear My solutions to see it), and the other two show Up to date.
5. Rename the repository folder and restart. The repository check warns and Downloads works from the cached index.

## Limits

SHA-256 detects corruption or tampering relative to the index; it does not prove who published the package. Signing, access control on the share and concurrent publishers are not covered. Releasing an already-published version is refused unless `--force` is given; `retire` withdraws versions. For a full walkthrough, run `tools\Reset-TestScenario.ps1` and follow the [test plan](../Guides/test-plan.md).
