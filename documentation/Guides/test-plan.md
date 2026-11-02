# End-to-end test plan

About 45 minutes. It covers every MVP feature, using a known starting state you can reset at any time.

## 0. Reset to the test scenario

Close the Studio, then run:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Projects\DemoBuilder\tools\Reset-TestScenario.ps1
```

It backs up your current repository, library and settings to `C:\FISDemoTest\backup-<time>` and then sets up:

| Demo | Repository | Your laptop | Expected |
|---|---|---|---|
| Designed for Digital | 1.0.0, **1.1.0** | 1.0.0 | Update offered |
| The Digital Advantage | 1.0.0, **1.1.0** | 1.0.0 | Update offered |
| Confident, Repeatable Delivery | 1.0.0 | 1.0.0 | Up to date |
| Data Lab | 1.0.0 | — | Available in Downloads |

You are assigned all three solutions (Instant Payments Hub, Digital Account Origination, Trade Lifecycle Manager). `C:\FISDemoTest\TemplateDemo` is ready for the Build Package test.

In the commands below, `$tool` is:
```powershell
$tool = "C:\Projects\DemoBuilder\tools\DemoRepositoryTool\bin\Debug\net10.0-windows\DemoRepositoryTool.exe"
```

## 1. Start-up and updates
1. Start the Studio. Every check row is the same thin height. *Demo updates* passes.
2. As soon as the checks finish, a prompt lists **2 demo updates** with their release notes. Click **Yes, update**.
3. ✔ Progress shows, then both tiles read **v1.1.0**. The status card shows *No updates pending*.

## 2. Demo Library
1. **My solutions** is ticked and three demos show. Try **Search** (`data`), **Business** (*Banking*), **Sort**, and **★** then **Favorites**.
2. Click **View Details** on one demo: organisation path, personas, agenda.
3. Resize the window, or use **Ctrl −/+/0**. The layout still fits.

## 3. Downloads
1. Open **Downloads**. Data Lab is **AVAILABLE**; the others are **INSTALLED**.
2. Click **↓ Download** on Data Lab. ✔ It becomes INSTALLED and appears in the library.

## 4. Present with a persona and tracking
1. Launch **Data Lab**. In the start dialog, pick a persona other than the default, reason *Rehearsal*, client `Northwind Bank`, OID `OPP-1001`, with **Track this session** on.
2. ✔ Pages show *Prepared for Northwind Bank* and that persona's figures. Try Previous/Next, the arrow keys, F11/Esc, Hide menu.
3. Close the player. Settings › **Usage Tracking** lists the session with client and OID. Click **Upload now**. ✔ A file appears in `C:\FISDemoRepository\usage\<DOMAIN_user>\`.
4. Launch again with tracking **off**. ✔ No new session is listed.

## 5. Edit configuration, theme and description
1. On Confident Delivery, click **⋯** › **Edit configuration**.
2. In General, change the **description**, set **Library tile theme** to *Raspberry*, then click **Save Configuration**.
3. ✔ Back in the library, the tile shows the new colour and description.

## 6. Data sets
1. Edit Data Lab › **Data Sets** › **+ New data set…**, named `Acme Test`, Override of the default set.
2. Add `"client": { "name": "Acme Test Bank" }`, then click **Check** (✔ Valid) and **▶ Preview** (✔ the name shows).
3. Click **Save data set**, then **Save Configuration**. Launch Data Lab: ✔ *Acme Test* is a persona card.

## 7. Save as new demo
1. In the Configuration Studio, change the name to `Data Lab – Acme`, then click **Save as new demo…** and save the file.
2. ✔ A second Data Lab tile appears with the new name. The original is unchanged.

## 8. Uninstall and re-download
1. On `Data Lab – Acme`, click **⋯** › **Uninstall demo…**. The button stays disabled until you type `delete`. Confirm.
2. ✔ The tile is gone. Uninstall the original Data Lab too, then download it again from Downloads.

## 9. Build a new demo from the Template
1. **Build Package** › **Choose folder** › `C:\FISDemoTest\TemplateDemo`. Click **Validate source**: ✔ no errors.
2. With **Add completed package to Demo Library** ticked, click **Build .fisxpkg** and save.
3. ✔ *Template Demo* is in the library (clear My solutions; its L8 is a placeholder). Launch it and choose **Example client (override)**: USD figures, two rows in the table.

## 10. Publish, guard and retire (administrator)
With the Studio closed:

```powershell
& $tool release C:\FISDemoRepository ConfidentDelivery 1.0.0 "same version"   # ✔ Refused (exit 3)
& $tool release C:\FISDemoRepository ConfidentDelivery 1.1.0 "Guard test release"
& $tool retire  C:\FISDemoRepository fis-digital-advantage 1.1.0
```

Start the Studio:
- ✔ The update prompt offers Confident Delivery **1.1.0**. Click **Not now**. The status card shows 1 pending; Downloads shows **UPDATE** (hover for release notes). Then click **Update** in Downloads.
- ✔ The Digital Advantage tile in Downloads shows **LOCAL IS NEWER**: you have 1.1.0, and the repository now only has 1.0.0.

Optional, publishing your own edit: release the edited working copy from step 5 so everyone gets the new theme.
```powershell
& $tool release C:\FISDemoRepository "$env:LOCALAPPDATA\FISPresents\data\workspaces\fis-confident-delivery" 1.2.0 "Raspberry theme"
```

## 11. Offline
1. Close the Studio and rename `C:\FISDemoRepository` to `C:\FISDemoRepository-off`.
2. Start it. ✔ *Demo repository* warns, the status card says **Offline**, Downloads uses the cached list, and installed demos still launch.
3. Rename the folder back.

## 12. Settings, logs and About
- **Settings › Logs**: entries for the downloads, updates and uploads above; try the filters.
- The **?** button: versions, stats, **Copy details**.
- Change the display size and click **Save Settings**. ✔ It applies.

## Restore
Close the Studio and copy `repository`, `library` and `settings` from `C:\FISDemoTest\backup-<time>` back to `C:\FISDemoRepository`, `%LOCALAPPDATA%\FISPresents\data` and `%LOCALAPPDATA%\FISPresents\Settings`. Or run the reset again.
