# FIS Presentation Studio — User guide

For pre-sales consultants who browse, download and present demos. If you build or publish demos, see the [authoring guide](authoring-guide.md). If you run the demo repository, see the [administrator guide](admin-guide.md).

## 1. What the Studio does

FIS Presentation Studio keeps a **local library** of branded demos on your laptop, kept in step with the central **demo repository**. Demos run from your laptop, so they work with no network, VPN or live system. When you start a demo, you choose who it is for and the demo personalises itself.

| Term | Meaning |
|---|---|
| Demo / package | One `.fisxpkg` file: the pages, branding, data sets and settings of a demo. |
| Demo repository | The shared location where approved demos are published. Default: `C:\FISDemoRepository`. |
| Local library | The demos installed on your PC: `%LOCALAPPDATA%\FISPresents\data`. |
| Data set / persona | The client figures a demo shows, for example *Tier 1 bank, Head of Operations*. |
| L3–L8 | FIS organisation tags. L3 is the business (Capital Markets, Banking, Payments, Merchant Solutions, Corporate); L8 is the solution. |
| My solutions | The L8 solutions assigned to your Windows user. |

**Requirements:** Windows 10/11 and the Microsoft Edge WebView2 Runtime (normally pre-installed).

## 2. Starting the Studio

On launch, the Studio runs a short list of checks. Each one is a single row: hover a row to see its detail.

| Check | If it fails or warns |
|---|---|
| Settings, Configuration schema, Local storage, Demo library | Required. The Studio cannot continue; see [troubleshooting](#10-troubleshooting). |
| Demo repository | You can still present installed demos. Downloads uses the last cached list. |
| User assignments | *My solutions* is unavailable; all demos are shown. |
| Demo updates | Lists how many installed demos have a newer version. |
| Usage upload | Runs in the background only when the repository is reachable. |
| WebView2 runtime | Demos cannot play until WebView2 is installed. |
| News feed, Welcome content | Welcome shows sample or cached news. |

When the checks finish and updates are available, you are asked **"Yes, update"** or **"Not now"**, with the release notes for each demo. *Yes* downloads, verifies and installs them. *Not now* leaves your current versions in place; you can update later from Downloads. Then click **Open Studio**.

The **status card** under the FIS logo always shows your user name, whether the repository is online, pending updates and how many demos are installed.

**Screen size:** the Studio fits itself to your screen. Use **Ctrl +**, **Ctrl −** and **Ctrl 0** to adjust, or set a fixed size in Settings › Startup & Display.

## 3. Navigating

| Sidebar | Use it to |
|---|---|
| Welcome | Read news and announcements of new and updated demos. |
| Demo Library | Find and launch installed demos. |
| Downloads | Install or update demos from the repository. |
| Recent | See demos you launched recently. |
| Settings | Storage, repository, display, tracking and logs. |
| Build Package | (Authors) Package a demo folder. |
| Open Demo | Play a `.fisxpkg` file without installing it. |
| **?** (bottom of the sidebar) | About: versions, stats and support information. |

## 4. Finding a demo — Demo Library

- **My solutions** is ticked by default when you have assignments. Clear it, or click **Show all demos**, to see everything installed.
- **Search** matches names, descriptions, products, tags and organisation levels.
- **Business** filters by L3. **Sort** by Name, Newest imported or Recently launched.
- **★** marks a favourite; tick **Favorites** to show only those.
- Each tile shows the business, title, a short description, the version and when it was last updated.

**View Details** shows the full organisation path, functions and tags, the personas and data sets on offer, the agenda and package facts.

The **⋯** button on each tile offers:
- **Edit configuration**: opens the Configuration Studio (see the [authoring guide](authoring-guide.md#editing-an-installed-demo)).
- **View details**.
- **Uninstall demo…**: removes the demo, its favourite and history, and any local edits. Type `delete` to confirm. Repository demos can be downloaded again at any time.

## 5. Getting demos — Downloads

Downloads lists every demo published in the repository.

| Badge | Meaning |
|---|---|
| AVAILABLE | Not installed. Click **↓ Download**. |
| UPDATE | A newer version is published. Click **Update**, or **Update all**. |
| INSTALLED | You have the latest version. |
| LOCAL IS NEWER | Your copy is newer than the published one (usually a local edit). |

The footer of each tile shows the version, release date, size and **★ Assigned to you** where relevant. Hover the tile for release notes. Use **My solutions**, **Not installed / updates** and the business filter to narrow the list.

Every download is checked against the repository's fingerprint (SHA-256) before it is installed. If the check fails, nothing is installed.

## 6. Presenting a demo

1. Click **Launch Demo** on a tile (or **Launch demo →** in details).
2. In the **Start demo** dialog:
   - Pick a **persona & data set** card. The default is preselected.
   - Choose a **Demo reason**: Client demo, Internal training, Rehearsal, Partner enablement, Event / conference or Other.
   - Enter the **Client name**. It appears in the demo as "Prepared for …".
   - Enter the **Opportunity ID (OID)** if you have one. It is logged with the session but never shown in the demo.
   - Leave **Track this session** on to record usage, or turn it off. Untracked sessions record nothing.
3. Click **Start demo →**.

### In the player

| Control | Action |
|---|---|
| Menu (left) | Jump to any page. |
| **← Previous / Next →**, or the arrow keys | Step through the pages. |
| Hide menu | More room for the content. |
| Fullscreen, or **F11** | Full screen. **Esc** leaves full screen. |
| Home | Return to the opening page. |
| Diagnostics | Export a support file if something goes wrong. |
| Close player | End the session. Tracked sessions are saved for upload. |

**Before a client meeting:** launch the demo once with the reason *Rehearsal* and click through every page. Because the demo runs from your laptop, it behaves the same in the room.

## 7. Recent and Open Demo

- **Recent** lists the demos you launched, most recent first.
- **Open Demo** plays any `.fisxpkg` file, for example one shared by a colleague, without adding it to your library. To keep it, use **+ Import .fisxpkg** in the Demo Library.

## 8. Settings

| Tab | What you can change |
|---|---|
| Storage & Backups | Library, playback cache and backup folders. **Back up now**, and automatic backups before changes (keeps 1–50). |
| Remote Repository | The demo repository folder or share, and the optional news feed address and check interval. |
| Startup & Display | Display size (Auto or 80–125%) and **Start with Windows**. |
| Usage Tracking | Whether *Track this session* starts on, background upload, your recent sessions, **Upload now**. |
| Logs | The Studio's daily log, with level and text filters. |

Click **Save Settings** (top right) to apply your changes.

## 9. Your data and privacy

- Tracked sessions record: you, your machine, the demo and version, its L3–L8 tags, the data set, reason, client name, OID, the pages viewed and the duration.
- Records are queued on your PC and copied to the repository's `usage` folder in the background, only when the repository is reachable.
- Untracked sessions write nothing. App starts are always recorded (user, machine, app version).
- Logs stay on your PC for 14 days unless you share them.

## 10. Troubleshooting

| Symptom | What to do |
|---|---|
| Repository shows **Offline** | Check you are on the network/VPN and that Settings › Remote Repository points at the right folder. Installed demos still work. |
| A demo is missing from the library | Clear **My solutions** and any filters, or download it from Downloads. |
| *My solutions* is greyed out | You have no assignments. Ask your administrator to assign your L8 solutions. |
| An update failed | Check the Logs tab. A fingerprint mismatch means the published file is damaged; tell the administrator. |
| A page is blank or stalls | Use **Diagnostics** in the player and send the file with the log (About › **Copy details**). |
| The window is too large or too small | **Ctrl 0** resets the size. Use Settings › Startup & Display for a fixed size. |

For support, open **?** › About, click **Copy details** and include it with your request.
