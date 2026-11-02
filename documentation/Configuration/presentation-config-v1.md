# Presentation configuration v1.0

The configuration is the contract between the Builder, runtime, and Demo Library. It is stored as `presentation.json` at the content root and in every `.fisxpkg`.

The machine-readable JSON Schema is [presentation.schema.json](presentation.schema.json). It is embedded in the app and enforced with JsonSchema.Net before typed deserialization and semantic validation. Filesystem-aware checks add file existence, unique IDs, entry point suitability, safe Windows package-relative paths and case-insensitive label uniqueness. Optional groups retain model defaults for old v1 packages; explicit null groups reject.

See [Demo Library design](../DemoLibrary/design.md) for editor acceptance criteria, metadata ownership, storage permissions and the delivery sequence. The library, repository and data sets are implemented; see the [authoring guide](../Guides/authoring-guide.md) for the end-to-end build process.

## Responsibilities

- **Presentation identity:** stable ID, display name, summary, semantic version, and entry point.
- **Branding:** accent color and optional local logo.
- **Demo Library classification:** product, business functions, freeform search tags, and optional cover image. This metadata is descriptive only; it is not presentation content or a dataset.
- **Navigation:** whether the runtime menu is enabled, ordered sections, and ordered references to existing pages.
- **Loading:** optional loading.readyMode (navigation/message) and loading.timeoutSeconds (5–120, default 15). Existing packages use navigation completion; message-mode pages call PresentationRuntime.ready(). See [shared loader](../Runtime/demo-loading.md).

The Builder edits this data; it does not author HTML, JavaScript, CSS, images, or video.

## Ordering rules

The array order is authoritative. `navigation.sections[0]` appears first in the menu; each section's `pages[0]` appears first within that section. The Builder's move-up/down actions update these arrays and the JSON editor immediately.

IDs are stable identifiers, not labels. Use titles for text shown to users. Page paths and branding/cover assets are relative to the package root and must resolve inside it. Each configured page points to an existing HTML page. A media-only presentation may use one supported media file as `entryPoint`; wrap media in an existing HTML page when it needs to be one page among a navigation menu.

## Required validation

- `schemaVersion` is `1.0` and unknown properties are rejected.
- Presentation, section, and page IDs are present and unique (case-insensitive).
- `version` follows semantic-version `major.minor.patch` syntax.
- The entry point exists and is HTML or a supported direct-media type.
- Every navigation page points to an existing HTML file.
- Logo and cover image, if present, resolve to local package files.
- Accent color is six-digit hex.
- Static local HTML/CSS references do not escape the package and resolve to files.

## Catalog metadata

- `catalog.product`: primary FIS product or product family.
- `catalog.functions`: business capabilities, e.g. Banking, Payments, Fraud, Treasury.
- `catalog.tags`: searchable additional labels, e.g. Client demo, Digital, Onboarding.
- `catalog.coverImage`: optional local PNG/JPEG/BMP card image.
- `catalog.organization`: optional organizational tags `l3`–`l8`. `l3` is the business line (Capital Markets, Banking, Payments, Merchant Solutions, Corporate), `l4`–`l7` are intermediate levels, and `l8` is the actual solution. They are used for business filtering and for matching user assignments in the [demo repository](../DemoLibrary/repository.md). Edit them in the Configuration Editor's Organization card.

The planned library index derives portable metadata from the package. `presentation.json` inside the package remains the portable source of truth. Favorites, personal notes and local classification overrides are separate durable user records; rebuilding the package index must preserve them. Retagging a library card must not silently change an immutable package.

## Future versions

Remote services, update policies, analytics settings and permissions are intentionally not v1 config fields (updates come from the repository index, and tracking is chosen per session). `dataSets` and `branding.tileTheme` are optional v1 additions that older packages simply omit. Add any further fields only with documented runtime behavior and explicit compatibility rules; do not silently accept settings that the current player ignores.

## Example

```json
{
  "schemaVersion": "1.0",
  "id": "fis-payments-overview",
  "name": "Payments Overview",
  "description": "A short overview of payment capabilities.",
  "version": "1.0.0",
  "entryPoint": "index.html",
  "branding": {
    "accentColor": "#4BCD3E",
    "logo": "assets/fis-logo.png",
    "tileTheme": "navy"
  },
  "catalog": {
    "product": "Payments",
    "functions": ["Real-time payments", "Merchant solutions"],
    "tags": ["Client demo", "Digital"],
    "coverImage": "assets/library-cover.jpg",
    "organization": { "l3": "Payments", "l4": "Money Movement", "l5": "Real-Time Payments", "l6": "Payment Orchestration", "l7": "Payment Journey", "l8": "Instant Payments Hub" }
  },
  "navigation": {
    "enabled": true,
    "sections": [
      {
        "id": "overview",
        "title": "Overview",
        "pages": [
          { "id": "welcome", "title": "Welcome", "path": "index.html" },
          { "id": "capabilities", "title": "Capabilities", "path": "pages/capabilities.html" }
        ]
      }
    ]
  }
}
```

## Library tile theme

`branding.tileTheme` colours the Demo Library and Downloads tile banner (and the details dialog / package cover) using the FIS brand palette. The banner content never changes (gradient, swoosh, dots, logo, business line and title); only its colours do.

| Value | Banner | Swoosh, dots and business line |
|---|---|---|
| `green` (default, or empty) | Dark → green #113426 | The demo's `accentColor` |
| `raspberry` | Dark → Raspberry #B21A53 | Raspberry tint |
| `eggplant` | Dark → Eggplant #431C5B | Eggplant tint |
| `navy` | Dark → Navy #1D1F48 | Navy tint |
| `charcoal` | Dark → Charcoal #3D3D40 | Core Green #4BCD3E |

Pick it in the Configuration Studio (General → Runtime & Branding → Library tile theme). Tiles draw the banner from the installed config, so a theme, name or description change shows as soon as Save Configuration updates the library copy. Downloads tiles use the theme published in `repository.json`.

## Data sets and personas

> Full guide: [data sets: how demo data works](data-sets.md) covers `$extends` overrides, `$files`, the limits, and creating new sets in the Configuration Studio.

`dataSets` is optional. Each entry is offered as a persona card when the demo is launched from the library:

```json
"dataSets": [
  { "id": "regional-bank", "name": "Regional retail bank", "persona": "Head of Payments",
    "description": "1.2M instant payments a day with a retail evening peak.", "file": "data/regional-bank.json", "default": true }
]
```

**Validation**
- `id` must be unique and use the same rules as page ids.
- `name` is required.
- `file` is optional. When set, it must be a safe, package-relative `.json` file that exists.
- At most one entry may be `default`.

**What the page receives.** Before any page script runs, the Player defines a frozen `window.FIS_DEMO_CONTEXT`:

`{ dataSet, dataSetName, persona, clientName, reason, tracked, data, files, dataBytes, dataError }`

- `data` is the merged JSON (after `$extends`). The limit is 1 MB inline, and 256 KB is recommended.
- `files` maps `$files` names to package files that pages fetch on demand.
- Without a launch selection, the default (or first) data set is used.
- The Opportunity ID is never exposed to the page.

**Sample brand kit** (`samples/Demos/shared/deck.js`) helpers:
- `FISDeck.data('path.to.value', fallback)` reads from the selected data.
- In markup:
  - `data-fis-field` sets text.
  - `data-fis-count` sets an animated KPI target.
  - `data-fis-value` sets an input value.
  - `data-fis-when` hides an element unless the value is present.
  - The paths `clientName`, `persona` and `dataSetName` read from the context instead of the data file.
- With a client name, the top bar shows a *Prepared for* chip, and opening pages show *Prepared for / Persona* under the lede.
