# FIS Presentation Studio — Administrator guide

For whoever runs the demo repository: setting it up, publishing releases, assigning solutions to consultants and collecting usage. Technical reference: [demo repository](../DemoLibrary/repository.md).

## 1. The model

```
          Authors                       Central repository                    Consultant laptops
  demo folder ──release──►  repository.json  assignments.json  ──download──►  local library (cache)
                            packages\<id>\<ver>\*.fisxpkg     ◄──usage────   usage\pending
                            usage\<user>\*.jsonl
```

- The **repository** is a folder or file share. It is the single source of truth for which demos and versions exist.
- Each laptop keeps a **cache**: installed packages plus a copy of the index and assignments. It presents fully offline.
- At start-up, the Studio compares the cache with the repository, offers updates, and uploads queued usage files.

## 2. Setting up a repository

1. Choose the location. For a pilot, use a file share all consultants can **read**, for example `\\fileserver\FISDemoRepository`. Only publishers need **write** access. `C:\FISDemoRepository` is the local default used for testing.
2. Build the publishing tool once: `dotnet build tools\DemoRepositoryTool`.
3. Seed it:
   ```powershell
   $tool = "C:\Projects\DemoBuilder\tools\DemoRepositoryTool\bin\Debug\net10.0-windows\DemoRepositoryTool.exe"
   & $tool init \\fileserver\FISDemoRepository
   ```
   This publishes the bundled demos at 1.0.0 and assigns two solutions to the current user. Replace those assignments in step 4.
4. Create a `usage` folder in the repository that consultants can **write** to (each user writes only into `usage\<DOMAIN_user>\`).
5. Point each laptop at it: Settings › Remote Repository › Demo repository, then **Save Settings**. (For a wider rollout, this should be preset by the installer; see the [MVP readiness review](../Review/mvp-readiness.md).)

## 3. Assigning solutions to consultants

Assignments decide what **My solutions** shows. Values are **L8 solution names** and must match `catalog.organization.l8` in the demos exactly (case-insensitive).

```powershell
& $tool assign \\fileserver\FISDemoRepository "CORP\alex.smith" "Instant Payments Hub" "Trade Lifecycle Manager"
```

- `assign` **replaces** that user's list. Include every solution each time.
- `CORP\alex.smith` matches only that domain account. `alex.smith` matches the name in any domain.
- To remove a user, edit `assignments.json` and delete the entry.
- Users with no assignment see every demo; *My solutions* is disabled for them.

## 4. Releasing a demo

```powershell
& $tool release \\fileserver\FISDemoRepository C:\Demos\InstantPayments 1.1.0 "Added Northwind data set; fixed KPI labels"
& $tool list \\fileserver\FISDemoRepository
```

The tool validates the folder, adds the shared brand kit if it sits beside the folder, writes `shared/version.js`, renders the tile cover, builds the package, records its SHA-256 and updates `repository.json` in one step.

Release rules:
- **Always increase the version.** The tool refuses to release a version that is already published (exit code 3). `--force` replaces it, but laptops that already installed that version will not be told it changed, so use it only for a version nobody has downloaded yet.
- Write release notes for consultants: they show on the Downloads tile.
- Before announcing, download it on a second laptop, launch it and click through (see the [authoring checklist](authoring-guide.md#8-pre-release-checklist)).
- Releases appear on each laptop at its next start, or after **Refresh** in Downloads.

### Withdrawing a version
```powershell
& $tool retire \\fileserver\FISDemoRepository fis-instant-payments 1.1.0      # withdraw one version
& $tool retire \\fileserver\FISDemoRepository fis-instant-payments --force    # withdraw the whole demo
```
`retire` updates `repository.json` first, then deletes the package folders. Laptops that already installed the withdrawn version keep it; their Downloads tile shows **LOCAL IS NEWER**. Release a fixed, **higher** version to replace it. A retired version number can be released again.

## 5. Collecting usage

Each tracked session is a JSON Lines file. Laptops copy closed files into `usage\<DOMAIN_user>\` when the repository is reachable.

| Event | Contains |
|---|---|
| `app_start` | user, machine, app version |
| `demo_session_start` | demo id, name and version, L3–L8, data set, persona, reason, client name, OID |
| `page_view` | page id and title, per page shown |
| `demo_session_end` | duration in seconds |

Every line also carries `timestampUtc`, `sessionId`, `user`, `machine` and `appVersion`. Files are plain text and can be loaded into Excel, Power BI or a script. There is no built-in dashboard yet.

## 6. Backups and housekeeping

- Back up the repository folder (the index, assignments and `packages\`). It can be fully rebuilt from it.
- Laptops back up their own library before changes (Settings › Storage & Backups).
- Old package versions can be kept for audit. Laptops always choose the highest version.

## 7. Troubleshooting

A repeatable test scenario (two pending updates, one demo to download, a Template folder to build) is set up by `tools\Reset-TestScenario.ps1`; see the [test plan](test-plan.md).

| Symptom | Cause and fix |
|---|---|
| A laptop says *Repository offline* | Path wrong or share unreachable. Check Settings › Remote Repository and the user's read access. |
| A demo fails with a fingerprint (SHA-256) mismatch | The package was changed or damaged after release. Release again with a new version. |
| A user doesn't see a demo under *My solutions* | The assignment value doesn't match the demo's L8, or the account format (domain) differs. Check with `list` and `assignments.json`. |
| An update isn't offered | The version isn't higher than the installed one, or the laptop hasn't restarted or refreshed. |
| No usage arriving | The user turned tracking off, background upload is off, or the user can't write to `usage\`. |
