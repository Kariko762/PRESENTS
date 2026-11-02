# FIS Presentation Studio — Authoring guide

How to build a demo package, design its data, add and update data sets, and release it to consultants. To use the Studio, see the [user guide](user-guide.md). To run the repository, see the [administrator guide](admin-guide.md).

Reference material:
- [presentation.json contract](../Configuration/presentation-config-v1.md) and [JSON Schema](../Configuration/presentation.schema.json)
- [Data sets: how demo data works](../Configuration/data-sets.md): `$extends`, `$files`, limits and the data-set editor in detail
- [Demo repository](../DemoLibrary/repository.md): layout, assignments and the publishing tool
- [Demo loading](../Runtime/demo-loading.md): the optional loader and the ready signal

## 1. What a package is

A demo is a folder of ordinary HTML pages plus a `presentation.json` that tells the Studio how to present them. **Build Package** zips the folder into a `.fisxpkg` file, which the repository distributes and the Player runs offline.

```
MyDemo/
├─ presentation.json        identity, branding, L3–L8 tags, menu, data sets
├─ index.html               entry page
├─ numbers.html, …          one HTML file per menu page
├─ data/
│  ├─ base.json             complete default data set
│  └─ client-x.json         override: { "$extends": "data/base.json", … }
├─ assets/                  images, video, CSV (optional)
└─ shared/                  FIS brand kit (copy of samples/Demos/shared)
   ├─ brand.css  deck.js  fis-logo.png  version.js
```

Rules that validation enforces:
- Everything a page uses must be **inside the folder**. No web URLs, CDNs or `..\` paths. Demos must run with no network.
- Every menu page must exist and be HTML. The entry point must be HTML or a supported media file.
- IDs (demo, sections, pages, data sets) must be unique; use lower-case-with-hyphens.
- `version` is `major.minor.patch`.
- Data: up to 1 MB of inline data per set (256 KB recommended); see [limits](../Configuration/data-sets.md#5-limits).

## 2. Start from the Template

`samples/Demos/Template` is a working three-page demo with two data sets. It shows a headline, KPIs, a table and a conditional callout.

1. Copy `samples\Demos\Template` to a new folder, for example `C:\Demos\InstantPayments`.
2. Copy `samples\Demos\shared` **into** that folder as `shared\`. A package must contain everything it uses.
3. In `presentation.json`, change:
   - `id`: a new, permanent id, for example `fis-instant-payments`. **Never change it after release.** Updates are matched by id.
   - `name`, `description` (one or two sentences; it is the tile text) and `version` (`1.0.0`).
   - `catalog.organization` (`l3`–`l8`). `l8` must be the exact solution name used in assignments.
   - `branding.tileTheme`: `green`, `raspberry`, `eggplant`, `navy` or `charcoal`.
4. Rename and rewrite the pages, and update `navigation.sections[].pages[]` to match.
5. Replace the data in `data\` (sections 4–5).

The bundled demos (`DesignedForDigital`, `DigitalAdvantage`, `ConfidentDelivery`, `DataLab`) are fuller examples of the same kit. *Data Lab* shows every data pattern: tables, trade blotters, time series, CSV files and limit benchmarks.

## 3. Writing pages with the brand kit

Every page follows the same skeleton:

```html
<link rel="stylesheet" href="shared/brand.css">
<script src="shared/version.js"></script>
<script defer src="shared/deck.js"></script>
<body data-demo="Instant Payments" data-chapter="Operations" data-page="2" data-pages="6">
<main class="stage">
  <canvas class="backdrop"></canvas>
  …content…
</main>
```

`data-demo`, `data-chapter`, `data-page` and `data-pages` drive the top bar and progress ticks.

| Building block | Use |
|---|---|
| `.hero`, `.lede`, `.eyebrow` | Opening statement, supporting text and the small label above a heading. |
| `.grid-2/3/4`, `.card`, `.split`, `.row` | Layout. |
| `.kpi` | A big number tile. |
| `.chip`, `.chip.neutral` | Tags. |
| `.callout` | A highlighted message. |
| `.reveal` | Animates in when the page opens. |
| `.segmented` with `data-tabs` / `data-tab` | Tabs inside a page (`FISDeck.onTab` reacts to changes). |

Keep pages short. Design for a **1366×768 laptop**, then check on a large monitor.

## 4. Formulating the data model

Design the data **before** you write the pages. A demo's data set is one JSON object; every page reads from it.

**Step 1: list what each page shows.** Use a worksheet like this one, which is the Template's:

| Page | Shows | Field | Type | Default |
|---|---|---|---|---|
| Opening | Headline | `headline` | text | "Your headline goes here." |
| Opening | Client | `client.name`, `client.segment` | text | "your client", "Segment" |
| Numbers | KPIs | `kpis.paymentsToday`, `kpis.stpRate`, `kpis.valueToday`, `kpis.openExceptions` | number (rate 0–1) | 0 |
| Numbers | Accounts table | `accounts[]`: `id, name, type, balance, status` | rows | empty |
| All | Formatting | `locale`, `currency` | text | `en-GB`, `GBP` |

**Step 2: shape the JSON.**
- Group fields by meaning (`client`, `kpis`, `accounts`), not by page.
- Use real numbers, not formatted text: `2450000000`, not `"$2.45bn"`. Pages format with `locale` and `currency`, so one set works in any currency.
- Store rates as fractions (`0.962`) and dates as ISO text (`2026-03-31`).
- Store tables as arrays of flat objects with the same keys in each row.
- Large or page-specific tables (more than about 2,000 rows) go in `$files` as CSV or JSON.

**Step 3: build the base set.** `data/base.json` holds **every** field with realistic values. It is the default persona and the template for every other set. For more than a few dozen rows, generate it with a script (see `tools/DataGenerators/data_lab.py`, which is seeded and repeatable).

**Step 4: derive the other sets.** Each persona or client is an override that names only what differs:

```json
{ "$extends": "data/base.json",
  "client": { "name": "Northwind Savings Bank", "shortName": "Northwind" },
  "locale": "en-US", "currency": "USD",
  "kpis": { "paymentsToday": 96000, "openExceptions": 4 } }
```

Objects merge field by field; arrays and values replace. Fixes to the base flow into every override.

**Step 5: bind the pages.** Every bound element keeps a sensible default in the HTML, so a set that leaves a field out still reads well.

| Need | Markup or script |
|---|---|
| Text | `<b data-fis-field="client.name">your client</b>` |
| Input or slider starting value | `<input data-fis-value="inputs.volume" value="1000">` |
| Counting KPI | `<div class="kpi" data-count="100000" data-fis-count="kpis.paymentsToday">0</div>` (`data-count` is the default) |
| Formatted money, percentage or date | Script: `el.textContent = FISDeck.format.money(FISDeck.data('kpis.valueToday'), {compact:true})` |
| Show only when a value is present or true | `data-fis-when="kpis.openExceptions"` |
| Read in script | `FISDeck.data('kpis.stpRate', 0)` (path, fallback), `FISDeck.rows('accounts')` (always an array) |
| Format | `FISDeck.format.money(v, {compact:true})`, `.percent`, `.number`, `.date` |
| Table with search, sort and paging | `FISDeck.table(el, rows, columns)` |
| File from `$files` | `await FISDeck.load('history')` (CSV is parsed for you) |

Always assign data with `textContent`, never `innerHTML`. Full API: [data-sets.md §3](../Configuration/data-sets.md#3-what-a-page-receives).

**Step 6: declare the sets** in `presentation.json`:

```json
"dataSets": [
  { "id": "base", "name": "Base client", "persona": "Head of Operations",
    "description": "Complete base data set.", "file": "data/base.json", "default": true },
  { "id": "northwind", "name": "Northwind Savings Bank", "persona": "Chief Operating Officer",
    "description": "US mid-tier bank, USD.", "file": "data/northwind.json" }
]
```

Each entry becomes a card on the **Start demo** dialog. Exactly one should be `default`.

## 5. Adding and updating data sets

### Add a set (in the app, recommended)
1. Demo Library → **⋯** → **Edit configuration** → **Data Sets** tab → **+ New data set…**
2. Name it, choose the template set and choose **Override** (recommended), **Full copy** or **Blank**.
3. Add the differences, then click **Check**: it validates the syntax, the size and the structure against the default set.
4. **▶ Preview** plays the demo with this set (never tracked). **Save data set**, then **Save Configuration**.

### Update an existing set
1. Open the same **Data Sets** tab and select the set.
2. Edit the JSON: change values, add rows, or add a new field.
   - **New field?** Add it to the **base** set first, so every override inherits it, then add the page binding with a default.
   - **Renamed field?** Update every page that reads it, and every set that overrides it. **Check** flags sets that no longer provide fields the pages read.
3. **Check**, **▶ Preview** every set that extends it, then **Save data set** and **Save Configuration**.
4. To make it the default, tick **Default**. To delete a set, click **Remove** (blocked while other sets extend it).

### Who gets the change
| You want | Do this |
|---|---|
| Only you, for your next meeting | **Save Configuration**. Your library copy is updated. A newer published version will replace it, so keep a copy with **Save as new demo…** if it matters. |
| A client-specific copy | **Save as new demo…**: a separate `.fisxpkg` with a new id, added to your library. |
| Every consultant | Increase the version and release it (section 7). |

More detail, including manual editing and `$files`: [data-sets.md §6–7](../Configuration/data-sets.md#6-creating-a-new-data-set-for-an-existing-demo).

## 6. Building and editing in the Studio

### First build
1. **Build Package** → **Choose folder** (your demo folder).
2. **Validate source.** Fix every error: missing files, external URLs, invalid ids, oversize data. If there is no `presentation.json`, a starter one is generated from the HTML pages.
3. Keep **Add completed package to Demo Library** ticked and click **Build .fisxpkg**.

### Editing an installed demo
Demo Library → **⋯** → **Edit configuration** opens the Configuration Studio on a working copy (`%LOCALAPPDATA%\FISPresents\data\workspaces\<id>`).

| Tab | Edit |
|---|---|
| General | Name, description, version, entry point, accent colour, logo, library tile theme, L3–L8 organisation, product, functions and tags, loading behaviour. |
| Menu & Pages | Sections and pages: add, rename, reorder, remove. |
| Data Sets | Data sets (section 5). |
| { } JSON | The raw `presentation.json`, for advanced edits. |

**Validate** checks everything. **Save Configuration** rebuilds the package and replaces your library copy; tiles update immediately. **Save as new demo…** creates a new demo with its own id.

## 7. Versioning and release

Use semantic versions. Consultants are offered an update only when the published version is **higher** than theirs.

| Change | Version |
|---|---|
| Text fixes, data corrections | `1.0.0 → 1.0.1` |
| New page, new data set, new feature | `1.0.1 → 1.1.0` |
| Story rewritten, data model changed | `1.1.0 → 2.0.0` |

**Never re-release an existing version number.** The publishing tool refuses it unless forced, because laptops that already have that version would not pick up the change.

To release, set the new `version` in General, **Save Configuration**, then publish the working folder:

```powershell
$tool = "C:\Projects\DemoBuilder\tools\DemoRepositoryTool\bin\Debug\net10.0-windows\DemoRepositoryTool.exe"
& $tool release C:\FISDemoRepository "$env:LOCALAPPDATA\FISPresents\data\workspaces\fis-instant-payments" 1.1.0 "Added Northwind data set"
```

The tool validates and packages the folder, renders the tile cover, writes `shared/version.js` and fingerprints the file. On their next start, consultants see *"1 demo update available"*.

## 8. Pre-release checklist

- [ ] **Validate** shows no errors; ids, name, description and L3–L8 tags are final.
- [ ] Every page reads well at 1366×768 and on a large monitor; nothing is cut off.
- [ ] Every data set: **Check** is ✓ Valid and **▶ Preview** shows each page correctly, with the right currency and locale.
- [ ] An empty or partial set still shows sensible defaults.
- [ ] Works with Wi-Fi turned off.
- [ ] Videos are MP4 (H.264), images are compressed, and the package is a reasonable size (under 100 MB).
- [ ] The version is higher than the published one, and the release notes say what changed.
- [ ] After release: download or update on a second laptop and run through it once.
