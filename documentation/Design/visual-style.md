# Screenshot-driven FIS desktop styling

Current workspace decision: solid dark navy #101925, with no main-content green glow. Banner/sidebar image overlays and green controls remain. This supersedes the gradient notes below.

Latest adjustment: workspace green glow fades horizontally from right to left at 60% brush opacity, retaining its short fade distance. Sidebar bottom artwork renders at 50% image opacity; logo, navigation text and selection highlights remain opaque. This supersedes the earlier diagonal glow direction.

Current background palette: dark navy #101925 throughout Studio, Player, tabs, inputs, lists and app-owned dialogs. Translucent tile tints use navy rather than grey. Green artwork, selection highlights, primary buttons and the short 60%-opacity bottom-right glow remain. This supersedes earlier charcoal/solid-grey panel choices; presentation HTML retains its own content styling.

The user's Demo Library and Configuration Studio reference images are the visual target. This supersedes the earlier indigo/cyan prototype styling.

- Charcoal shell and layered dark gray panels, pale readable text, green selected navigation and tabs.
- Fixed 200-DIP left rail, official FIS wordmark, green active stripe and lower green brand treatment.
- Dark fields, lists, scrollbars and buttons with explicit WPF control templates. No selected tab may inherit a Windows white background.
- Green rounded primary actions with dark text and no border (including focus/hover); subdued green-gradient selected navigation/tabs with white text. Keyboard focus on primary actions uses a brighter fill.
- Tab headers use a natural-width horizontal StackPanel in a scrolling header strip, not TabPanel's squeezing/multi-row behavior. A dark-green shell connects the title area, banner and workspace instead of separate gray/green/gray bands. Framed tools keep charcoal interiors.
- Configuration General layout uses side-by-side presentation and runtime/branding cards, with save/validation actions always available below the tabs.
- Shared theme: Themes/FisTheme.xaml. Both Studio and Player use it; presentation HTML styling remains independent.
- Page hierarchy is banner first, current-workspace tag inside the banner, tabs, then content. No standalone location/title strip sits above the banner. The main workspace fades from vivid green at bottom-right diagonally toward charcoal at top-left; source-folder tools appear below the tabs for authoring workspaces.
- Content tiles use translucent tinted gradients, subtle highlight borders and soft shadows over the workspace glow. This is a glass-like WPF treatment, not native backdrop blur; text remains fully opaque for contrast.

The official logo and user-supplied artwork are embedded locally, so opening Studio does not require a network connection. The banner uses green-building-banner.png across its full width with UniformToFill, preserving aspect ratio and cropping vertically as necessary. The sidebar uses green-dots-and-swish.png with UniformToFill aligned left/bottom to retain the dot artwork. These images replace the generated grid/arches/swish. The full-width dark-green banner base remains behind the image.

Demo Library and Downloads navigation are live (see [repository](../DemoLibrary/repository.md)); Recent remains visibly disabled and labelled as planned. Personas, data profiles, scenarios, remote libraries and demo cards are not simulated as working features by this styling change.

VisualThemeTests runs on an STA thread, loads the actual resource dictionary/window, checks selected-tab colors, dark fields, scrollbar styling and sidebar workflow switching, and renders a sample-filled preview PNG into test output. This is a layout/template regression test, not full mouse/keyboard/DPI/accessibility acceptance on all Windows devices.

## Screen fit

Studio, Player and the news reader size themselves to the work area of the monitor they open on (the owner's monitor, or the one under the cursor), in DPI-independent units. When the preferred size (Studio 1440×920, Player 1400×850) does not fit, the window starts maximized. On large screens Studio opens at 80% of the work area. See `Controls/WindowFitter.cs`.

**Display scale.** Studio's whole UI is scaled against a 1360×820 design size (`Controls/DisplayScaler.cs`). Auto shrinks it on smaller screens (to no less than 70%; about 85% on a 1366×768 laptop). It stays at 100% on typical desktops and grows by up to 25% on large monitors. Users can override it in Settings › Startup & Display › Display size (Auto, 80–125%) or with Ctrl + / Ctrl − / Ctrl 0. The choice applies immediately and is saved as `UiScale` (0 = Auto).

**Compact layout.** On short or narrow screens (client under 800 DIP high or 1280 wide), or when a large manual zoom leaves too little room, Studio uses a compact layout. The header drops from 174 to 100 px with a smaller title (the banner artwork keeps its full size, anchored to the top, so only its bottom is cropped), the sidebar narrows from 200 to 172 px with tighter buttons and a smaller logo, and margins are reduced. In the authoring views (Configuration Studio, Build Package), compact mode goes further: a 60 px header without the description, the source folder controls moved up next to the tab strip, intro text hidden, and slimmer text inputs. The Configuration Studio General tab places short fields side by side (ID, Name and Version on one row; entry point and accent color with a live swatch on one row; L3–L8 in a single row), and Menu & Pages shows sections and pages in two columns. The Player is not scaled, because WebView2 content does not support WPF layout transforms; demo pages handle their own responsiveness.

## Shell chrome

The header has no status or location pills.

**Sidebar**
- The sidebar logo row is always the same height as the hero, so the FIS logo (84×44, or 72×38 in compact mode) and the hero title share a centre line.
- The sidebar has no tagline. A 32 px **?** circle at its bottom right opens **About**, which shows:
  - versions (Studio, .NET, WebView2, package format)
  - local stats (installed demos, favorites, tracked sessions, available updates)
  - user, machine, repository, library, usage queue and log locations
  - support contacts, with Copy details and View logs actions

  The support contacts in `Dialogs/AboutDialog.cs` are placeholders until the support model is confirmed.

**Workspace**
- There is no page footer.
- Scrollable tab content has no bottom padding, so lists run to the bottom edge of their panel.
- Workspace-level buttons (Library actions, Save Settings) sit on the right of the tab strip.
- Startup check tiles are 50 px high and show the check title only; details are in the tooltip.
