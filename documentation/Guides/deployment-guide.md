# FIS Presentation Studio — Deployment guide

For whoever puts the Studio on consultants' laptops. Do these steps before anyone runs the exe. Repository administration (releasing demos, assignments, usage) is in the [administrator guide](admin-guide.md).

## 1. What's in the release

`FISPresents-<version>-win-x64.zip`, produced by `tools\Build-Release.ps1`:

| Folder | Contents |
|---|---|
| `App\` | `FisPresentationPlatform.exe` and its runtime, self-contained (.NET is bundled), plus `deployment.json` |
| `Tools\` | `DemoRepositoryTool\DemoRepositoryTool.exe` (admin CLI) and `Reset-TestScenario.ps1` (test only) |
| `Documentation\` | User, authoring, admin, deployment and test guides; configuration and data-set reference; executive overview |
| `README.txt` | Quick start |

## 2. Prerequisites (each laptop)

| Requirement | Notes |
|---|---|
| Windows 10 (1809+) or Windows 11, 64-bit | x64 build. Build with `-Runtime win-arm64` for ARM laptops. |
| Microsoft Edge WebView2 Runtime | Already present on Windows 11 and on updated Windows 10. If the Player shows a blank window or an error about WebView2, install the Evergreen runtime from Microsoft. |
| .NET | **Not needed.** It's bundled. |
| Admin rights | **Not needed.** Everything runs and writes per-user. |
| Disk | About 170 MB for the app, plus room for downloaded demos (start-up check needs ≥ 64 MB free). |
| Network | Only to reach the repository share. Demos run fully offline once downloaded. |

## 3. Prepare the repository (once)

1. Create the share, for example `\\fileserver\FISDemoRepository`, and initialise it. See [admin guide §2](admin-guide.md#2-setting-up-a-repository).
2. Permissions:
   - Consultants: **read** on the share, **write** on `usage\` only.
   - Publishers: **write** on the whole share.
3. Add each consultant's Windows user name to `assignments.json` ([admin guide §3](admin-guide.md#3-assigning-solutions-to-consultants)). Unassigned users still see every demo.

## 4. Set the first-run defaults

`App\deployment.json` supplies settings for any user who has **not yet saved** settings in the Studio. Set the repository once here instead of on each laptop:

```json
{
  "demoRepositoryPath": "\\\\fileserver\\FISDemoRepository",
  "checkOnLaunch": true,
  "trackingEnabledByDefault": true,
  "uploadUsage": true
}
```

- Either build it in, with `.\tools\Build-Release.ps1 -Version 1.0.0 -RepositoryPath "\\fileserver\FISDemoRepository"`, or edit the file after unzipping.
- Any setting from [settings](../Settings/settings.md) can be included; unlisted ones keep their defaults. `%USERPROFILE%`-style variables are expanded. Comments are allowed.
- Once a user presses **Save Settings**, their own `%LOCALAPPDATA%\FISPresents\Settings\settings.json` is used and `deployment.json` is ignored for them.
- An invalid value (for example a relative path) makes the start-up **Settings** check fail with the reason, so test the file before you roll it out.

## 5. Install on a laptop

1. Unzip `App\` to a per-user folder the user can write to, for example `%LOCALAPPDATA%\Programs\FISPresents\`. Avoid `Program Files`: it works, but later updates then need admin rights.
2. Create a shortcut to `FisPresentationPlatform.exe` on the desktop or Start menu. The exe carries the FIS icon.
3. Optional: copy `Documentation\Guides\user-guide.md` (or a PDF of it) somewhere consultants can find it.

Distribution options for the folder: a shared-drive copy script, Intune / SCCM "copy folder + shortcut" package, or a manual unzip for the pilot.

## 6. What happens on first run

No setup is required. At start-up the Studio runs its checks and creates any folder it needs:

| Created | Purpose |
|---|---|
| `%LOCALAPPDATA%\FISPresents\data` | Local demo library (cache of downloaded packages), tracking queue |
| `%LOCALAPPDATA%\FISPresents\Backups` | Library snapshots before imports |
| `%LOCALAPPDATA%\FISPresents\Settings` | `settings.json`, created on first **Save Settings** |
| `%LOCALAPPDATA%\FISPresents\logs` | Application log (viewable in the app) |
| `%LOCALAPPDATA%\FISPresents\Welcome` | Cached Welcome feed |
| `%LOCALAPPDATA%\FIS\PresentationRuntime\Cache` | Extracted demo runtime files |
| `%LOCALAPPDATA%\FIS\PresentationRuntime\Logs`, `BrowserProfiles` | Player diagnostics and per-session browser profiles |

Then:

- **Repository online:** the Library opens on the user's assigned solutions (empty until they download), and **Downloads** lists what's published. If the user already has demos and newer versions exist, the update prompt appears.
- **Repository offline:** start-up shows *Repository offline*. Installed demos still open and run, and usage queues locally for the next online start.

## 7. Verify a deployment (2 minutes)

1. Launch. All start-up checks are green (Repository and Assignments amber is acceptable off-network).
2. The sidebar shows the user name and **Repo: Online**.
3. **Downloads** → download one demo → it appears in the **Demo Library**.
4. Open it, start it, and step through two pages.
5. **?** (About) shows the expected app version and repository path.

## 8. Upgrading the Studio

1. Close the Studio.
2. Replace the contents of the install folder with the new `App\` (keep your edited `deployment.json` if it differs).
3. User data in `%LOCALAPPDATA%` is untouched, so demos, settings and queued usage carry over.

If **Start with Windows** is on, keep the install path the same, because the startup entry points to the exe's location.

## 9. Uninstalling

1. In the Studio, turn off **Start with Windows** if it's on.
2. Delete the install folder and shortcut.
3. To remove user data too, delete `%LOCALAPPDATA%\FISPresents` and `%LOCALAPPDATA%\FIS\PresentationRuntime`. Upload any queued usage first by starting once while online.

## 10. Known limits

- It's a zip, not an installer: no Add/Remove Programs entry and no auto-update of the Studio itself (demos *do* auto-update). See [MVP readiness](../Review/mvp-readiness.md).
- The exe isn't code-signed, so SmartScreen may warn on first launch from a downloaded zip. Unblock the zip (**Properties › Unblock**) before extracting, or sign the build before wide roll-out.
