# Data sets: how demo data works

A **data set** is the client, persona and figures a demo shows. One demo can have several sets, for example a Tier 1 bank, a regional broker and an asset manager. The consultant chooses one on the start dialog, and every page then renders that client's numbers. The HTML is the same; only the data changes.

This guide covers:
- how data reaches a page
- how to store it
- the limits
- how to **add a new data set to a demo that already exists**

The **Data Lab** demo (`samples/Demos/DataLab`, id `fis-data-lab`) is the reference implementation and test bench for everything below.

## 1. How it fits together

```
presentation.json            dataSets[]  →  cards on the start dialog
data/tier1-bank.json         the set's JSON: injected into every page
data/asset-manager.json      { "$extends": "data/tier1-bank.json", …only what differs… }
data/shared/history.csv      big tables, referenced by "$files", fetched only by the page that needs them
```

1. The consultant launches a demo, and the start dialog lists `dataSets` (the default set is preselected).
2. The Player resolves the chosen file:
   - It follows `$extends` and deep-merges.
   - It checks `$files` and the size limits.
3. Before any page script runs, the Player defines a frozen `window.FIS_DEMO_CONTEXT`. It does this on every page.
4. Pages read the context directly, or through the `FISDeck` helpers in `shared/deck.js`.

The resolver is `Services/DataSetResolver.cs`. Validation, the Player and the Configuration Studio all share it, so the Studio shows exactly what a page will receive.

## 2. Declaring data sets (`presentation.json`)

```json
"dataSets": [
  { "id": "tier1-bank", "name": "Tier 1 bank", "persona": "Head of Trade Operations",
    "description": "Base data set: 400 trades, 6 desks …", "file": "data/tier1-bank.json", "default": true },
  { "id": "asset-manager", "name": "Asset manager (UK)", "persona": "COO, Investment Operations",
    "description": "Extends Tier 1 bank …", "file": "data/asset-manager.json" }
]
```

| Field | Rules |
|---|---|
| `id` | Required and unique. Use letters, numbers, `.`, `_` or `-` (at most 80 characters). It is stored with tracked sessions. |
| `name` | Required. This is the card title on the start dialog. |
| `persona` | Optional. Shown on the card and passed to pages. |
| `description` | Optional. One or two lines on the card. |
| `file` | Optional. A safe, package-relative `.json` file that must exist. Without a file, the pages get `{}` and fall back to their HTML defaults. |
| `default` | At most one set can be the default. Without a default, the first set is used. |

## 3. What a page receives

`window.FIS_DEMO_CONTEXT` (frozen):

| Field | Meaning |
|---|---|
| `dataSet`, `dataSetName`, `persona` | The chosen set. |
| `clientName`, `reason`, `tracked` | Values from the start dialog. The Opportunity ID is **never** exposed to pages. |
| `data` | The merged JSON object. `$extends` has been removed; `$files` is kept. |
| `files` | The `$files` map (name → package path). |
| `dataBytes` | The size of `data` in bytes. Data Lab shows this as the size meter. |
| `dataError` | Set when the file could not be resolved. `data` is then `{}`. |

If no set was chosen (for example in Preview, or a direct launch), the default set is used.

### `FISDeck` helpers (`shared/deck.js`)

| Helper | Use |
|---|---|
| `FISDeck.data('kpis.stpRate', 0.9)` | Dotted-path lookup with a fallback. |
| `FISDeck.rows('trades')` | Always returns an array: `[]` if the value is missing or not an array. |
| `await FISDeck.load('history')` | Fetches a `$files` entry (or a package path) and caches it per page. `.csv` and `.tsv` become arrays of objects with numeric values converted; `.json` is parsed; `.txt` stays text. `FISDeck.timings` records the fetch and parse time. |
| `FISDeck.format.number / money / compact / percent / date / dateTime` | Use the set's `locale` and `currency`. Null, blank and NaN values become `—`. |
| `FISDeck.table(host, rows, columns, options)` | A sortable, searchable and paginated table. Columns: `{ key, label, format, digits, align, className(value,row), sortValue(row) }`. Options: `{ pageSize, search, placeholder, onRow(row), empty }`. It returns `{ render, setRows, setFilter, visible, lastRenderMs }`. |

Markup without script:

```html
<h1 data-fis-field="headline">Default headline if the set has none</h1>
<b data-fis-count="kpis.tradesToday">0</b>          <!-- animated KPI -->
<p data-fis-when="client.lei">LEI: <span data-fis-field="client.lei"></span></p>  <!-- hidden if missing -->
```

The paths `clientName`, `persona` and `dataSetName` read from the context instead of the data file.

**Rule for page authors:** every value must have a sensible default in the HTML, and every array read must handle `[]`. The *Edge cases* set exists to test this.

## 4. Storage patterns

Pick a pattern by shape and size. Data Lab uses all of them.

| Pattern | Example | When |
|---|---|---|
| **Scalars and objects** | `client`, `kpis`, `locale`, `currency`, `headline` | Headlines, KPIs, labels. |
| **Row arrays** (array of objects) | `trades`, `positions`, `exceptions` | Tables. Keep each set under about 2,000 rows inline. |
| **Columnar series** | `"series": { "dates": [...], "pnl": [...], "var": [...] }` | Charts. This is about 3× smaller than an array of objects; keep it under about 5,000 points. |
| **Joined tables** | `exceptions[].tradeId` → `trades[].id`, `events[].tradeId` | Drill-downs and detail drawers (see Data Lab *Exceptions* and *Trade blotter*). |
| **Hierarchy by grouping** | flat `positions` grouped by desk › book › instrument on the page | Trees and subtotals. Store the data flat and group it in script. |
| **`$extends` override** | `asset-manager.json` is 15 lines | A new client that looks like an existing one. |
| **`$files` CSV** | `"$files": { "history": "data/shared/trade-history.csv" }` | Large tables (10k–100k rows), loaded only on the page that needs them. |
| **Shared file** | Several sets point `$files.history` at the same CSV | Saves package size when the history doesn't need to differ. |

### `$extends`: merge rules

```json
{ "$extends": "data/tier1-bank.json",
  "client":   { "name": "Kestrel Asset Management" },
  "currency": "GBP",
  "kpis":     { "openBreaks": 6 },
  "lei":      null }
```

- **Objects merge** key by key: `client.name` changes and `client.region` is inherited.
- **Arrays and values replace**: a `trades` array in the child replaces the whole base array.
- **`null` removes** a key.
- `$extends` is removed from the result. A chain may have up to **4 files** (the set plus 3 bases). Loops are rejected.
- `$files` is inherited and can be overridden per name.

### `$files`

- Names map to package-relative paths. The file must exist and be a `.csv`, `.tsv`, `.json` or `.txt` file.
- The files are **not** injected. A page fetches one with `FISDeck.load(name)`.
- They are packaged with the demo and served through the Player's virtual host. This was verified in the Player: a 10 MB CSV is fetched in about 100 ms.
- A page should handle a set that has no such file. Data Lab's *History* page shows a callout instead of an error.

## 5. Limits

These were measured in Data Lab on headless Chromium at 1366×640 (`limits.html` → **Run benchmarks** shows your machine's figures).

| What | Limit | Measured |
|---|---|---|
| Inline data (`data`, merged) | **Recommended ≤ 256 KB**, **hard limit 1 MB** (the Studio and validation reject anything larger). | 145 KB: about 0.6 ms to parse. 887 KB: about 2.7 ms. |
| Each data file | 1 MB | — |
| Inline row arrays | About 2,000 rows per set | — |
| Full-DOM table render | Paginate above about 1,000 rows. `FISDeck.table` only renders one page. | 100 rows: 10 ms · 1k: 85 ms · 5k: 640 ms · 10k: 2.1 s · 20k: 8.3 s |
| Chart points | About 5,000 | — |
| `$files` CSV | About 100k rows per file. The Studio warns above 25 MB. | 2 MB / 20k rows: 47 ms fetch + 130 ms parse. 10 MB / 100k rows: about 100 ms + 430–670 ms. |

Why inline data is bounded: it is serialized into **every** page as a script, so 1 MB costs 1 MB on every page change. Move anything large or used on only one page to `$files`.

## 6. Creating a new data set for an existing demo

This is the everyday task: *"I have a demo next week for Acme. I want their name, currency and a few of their numbers."*

### In the app (recommended)

1. **Demo Library** → **⋯** on the demo tile → **Edit configuration**.
2. Open the **Data Sets** tab. The left list shows each set with its size badge:
   - green: up to 256 KB
   - amber: up to 1 MB
   - red: invalid
3. Click **+ New data set…**, then:
   - Enter a **name** (the ID is suggested automatically) and an optional **persona**.
   - Choose a **template** set.
   - Choose a mode:
     - **Override (recommended):** writes `{ "$extends": "<template file>" }`. Add only what differs. Later fixes to the template flow through automatically.
     - **Full copy:** a standalone copy of the template JSON, plus copies of any `$files` the template owns. Use this when most of the data will change.
     - **Blank:** an empty object. Pages show their HTML defaults until you add fields.
4. Edit the JSON in the editor. Typical override:
   ```json
   { "$extends": "data/tier1-bank.json",
     "client": { "name": "Acme Securities", "shortName": "Acme" },
     "currency": "EUR", "locale": "de-DE",
     "kpis": { "tradesToday": 3150 } }
   ```
   To find field names, open the template set. The structure line lists the top-level fields, tables and row counts.
5. **Check** validates the JSON:
   - syntax, with line numbers
   - `$extends` and `$files`
   - the size limits
   - **structure compared with the default set**, which flags fields the pages read that this set doesn't provide
   
   Missing fields are warnings: pages fall back to their defaults.
6. **▶ Preview** opens the demo with this set. Preview is never tracked.
7. **Save data set** writes the file and keeps your formatting. **Save Configuration** saves the `presentation.json` changes (name, persona, description, default).
8. Choose how to make it available:
   - **Save Configuration** (when editing an installed demo) also rebuilds and replaces your library copy, which now offers the new set on the start dialog. It keeps the same id and version, so a newer repository version is still offered as an update and **would replace your local set**.
   - **Save as new demo…:** a separate `.fisxpkg` with its own id. Use this for a client-specific copy.
   - **Publish for everyone:** bump the version and release the folder to the repository (see below).

Other buttons:
- **Import JSON…** brings in a JSON file from elsewhere, checking size and syntax.
- **Remove** deletes a set. It is blocked while other sets `$extends` it, and the default moves to another set.
- **Format** pretty-prints the JSON. **Revert** discards unsaved edits. **Open folder** opens the working copy.

### Manually (for authors with the source folder)

1. Add `data/acme.json`, usually a `$extends` override.
2. Add a `dataSets` entry in `presentation.json`.
3. Run the tests. The `DataLabSampleTests` pattern resolves every set and asserts the limits; copy it for your demo.
4. Release a new version:
   ```powershell
   $tool = "tools\DemoRepositoryTool\bin\Debug\net10.0-windows\DemoRepositoryTool.exe"
   & $tool release C:\FISDemoRepository <demo folder or bundled id> 1.1.0 "Added Acme data set"
   ```
   Consultants are then offered the update when the app starts.

## 7. Building a new demo with several data sets

Start from `samples/Demos/Template`, which already has a base set and an override set wired to its pages. The [authoring guide](../Guides/authoring-guide.md#4-formulating-the-data-model) walks through designing the data model.

1. **Design the data model first.** List every field each page reads, with a default for each.
2. **Build one complete base set**, for example `tier1-bank.json`. Generate it with a script rather than writing it by hand. Data Lab's generator is `tools/DataGenerators/data_lab.py`; it is seeded, so it produces the same output every time (`python tools\DataGenerators\data_lab.py`).
3. **Derive the other sets** with `$extends`, overriding only what differs.
4. **Move large tables to `$files`.** Share a file between sets when the content doesn't need to differ.
5. **Add test sets:**
   - a *stress* set near the limits
   - an *edge-case* set with nulls, blanks, zero and negative values, long unicode names, HTML in text, and empty arrays
6. **Write pages defensively:**
   - Use `FISDeck.rows`, `format` (which handles nulls) and `data-fis-when`.
   - Set `textContent`, never `innerHTML`, for data values.
7. Register the demo and test it (section 8).

## 8. Test checklist

- [ ] Every set shows **✓ Valid** in the Data Sets tab, and the structure comparison shows no unexpected missing fields.
- [ ] Each set previews without errors on every page (Diagnostics in the Player, or the browser console).
- [ ] The *Edge cases* set:
  - HTML in names shows as text.
  - Empty arrays show "no rows".
  - Null numbers show `—`.
  - Missing headlines fall back to their defaults.
- [ ] The *Stress* set stays within the limits, and Data Lab's *Limits lab* benchmark looks acceptable on a laptop.
- [ ] `$files` pages show their file's rows, and show a sensible message when the set has no file.
- [ ] Locale and currency switch correctly (en-US/USD, de-DE/EUR, en-GB/GBP).
- [ ] `dotnet test` passes (`DataSetResolverTests`, `DataSetServiceTests`, `DataLabSampleTests`).

## 9. Data Lab reference

| Page | Shows |
|---|---|
| What the page received | A data inspector: set, size meter against the 1 MB limit, top-level fields, `$files`. |
| KPIs & formats | `data-fis-count`, locale and currency formatting, three ways a page reads data. |
| Trade blotter | `FISDeck.table` with status and desk filters, and a detail drawer joining exceptions and events. |
| Positions | A desk › book › instrument tree with subtotals, and a donut by asset class. |
| Time series | A columnar series with range buttons, a P&L vs VaR line chart and volume bars. |
| Exceptions | Severity filters, `sortValue`, and a lifecycle stage strip. |
| History file | A `$files` CSV loaded on demand, with a monthly chart and a paginated table. |
| Limits lab | Inline size and parse cost, and a benchmark of table rendering and file loading. |

| Set | Demonstrates |
|---|---|
| Tier 1 bank (default) | A complete base set: 145 KB inline plus a 2 MB / 20k-row CSV. |
| Regional broker (DE) | `$extends` with its own tables, EUR / de-DE, an inherited series and history. |
| Asset manager (UK) | A 15-line override. |
| Stress test | 888 KB inline (amber warning) plus a 10 MB / 100k-row CSV. |
| Edge cases | Graceful degradation. |
