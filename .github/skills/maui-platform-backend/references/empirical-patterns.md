# Empirical Patterns — Lessons from Building MAUI Backends

Patterns extracted from building 7 MAUI platform backends (macOS/AppKit, Linux/GTK4, Blazor, GDUI/game-engine, TUI/terminal, Container-based Linux, and core extensibility work). Every item was discovered in practice, not theorized.

> **Provenance:** These patterns were mined from ~95 Copilot session checkpoints across multiple backend implementations. The session history is cataloged in `platforms/platform-backend-sessions.md` in this repo for anyone who wants to dig deeper into a specific backend's development history.

---

## Lessons Learned

### Implementation Order That Works

The macOS backend tracked 68 iterations across months of development. The order that emerged:

1. **Audit first** — create a checklist of every handler and feature, audit what exists, identify gaps
2. **Core infrastructure** — base handler, dispatcher, context, host builder extension
3. **Basic rendering** — Label, Button, layouts → get "Hello World" on screen
4. **DevFlow integration** — add visual tree walking and screenshots early so you can debug everything that follows
5. **Interactive controls** — Entry, Editor, Switch, CheckBox, Slider
6. **Navigation** — Shell, NavigationPage, TabbedPage (lazily create pages — eager creation crashes)
7. **Gestures** — combine related native recognizers to avoid conflicts
8. **Advanced controls** — CollectionView (virtualization), WebView, Pickers
9. **Polish** — dark mode, toolbar, menu bar, FormattedText, remaining essentials
10. **Visual parity audit** — side-by-side screenshot comparison against a reference platform

### Key Lessons

#### 🏗️ Architecture

1. **Audit before you code** — The checklist is your source of truth for gaps and priorities. Don't start implementing randomly; audit what MAUI requires, then work through it systematically.

2. **Native toolkit quirks will surprise you** — AppKit crashes on missing images (`NSImage(fileName)` with a nonexistent path), GTK4's minimum-based allocation fights MAUI's frame-based layout, game engines (GDUI) need headless rendering pipelines. Every platform has its own "gotchas" that aren't documented.

3. **Handler registration order matters** — Some MAUI stubs must be overridden with concrete types in a specific order. If controls don't appear, check that your handler is registered and that it's not shadowed by a default stub.

4. **Some "bugs" are platform differences** — Mac Catalyst's 77% scaling, switch/slider tint limitations, and native style differences are expected. Document them rather than forcing impossible workarounds.

5. **Custom subclasses are often necessary** — Button padding, editor placeholders, and attributed labels all needed custom native subclasses on macOS. Plan for this — wrapping isn't always enough.

#### 🧭 Navigation & Modals

6. **Shell content must be lazy** — Eagerly creating all Shell page content triggers crashes (null references, premature layout). Only create content for the currently visible page.

7. **Modals need platform-native presentation** — On macOS, `PushModalAsync` should use `NSWindow.BeginSheet()/EndSheet()` (not overlay). Measure `ContentPage.Content` (not the `Page` itself) for natural sheet sizing. Keep overlay as an opt-in fallback via attached properties.

8. **Toolbar APIs need phased design** — Break toolbar work into native item types (buttons, toggles, groups, search, share, popup) and system items. Don't try to implement everything at once.

#### 🐛 Debugging & Testing

9. **Side-by-side screenshot comparison is the best audit technique** — Run your backend and a reference platform (Mac Catalyst, iOS Simulator) on separate DevFlow ports, then compare screenshots page-by-page.

10. **DevFlow accelerates debugging dramatically** — Sessions that used DevFlow for visual tree inspection completed handler implementations faster than those relying on console logging.

11. **Use SQL/state tracking for audit progress** — Large audits (60+ pages, 40+ handlers) need structured tracking. Use the SQL tool's `todos` table or a custom table to track per-page/per-handler completion.

12. **Visual snapshot testing works for headless backends** — GDUI used a `VisualMatrix`/SkiaSharp pipeline to render controls headlessly and compare against baselines. One catalog drives all controls/themes/states; cache `ScenarioCatalog.Build()` for performance.

13. **Visual audit is the largest single time investment** — In the macOS session, 22 of 68 checkpoints (32%) were visual comparison work. Plan for multiple passes: first pass finds obvious issues, second pass catches regressions from fixes, third pass verifies edge cases. This isn't a sign something is wrong — it's the expected shape of backend development.

14. **Kill stale app processes before retesting** — This was the #1 source of wasted debugging time. Stale binaries mask fixes and create false negatives. Always kill and restart the app after rebuilding.

#### 🔌 Platform Bindings & APIs

15. **Verify .NET binding existence before using native APIs** — APIs documented in Apple/GTK official docs may not have .NET bindings, or have different names (`NSColor.LabelColor` → `NSColor.ControlText`, `NSAccessibilityNotifications` → `"AXAnnouncementRequested"` string literal, `NSVisualEffectState` enum values differ). Always verify bindings compile before building complex logic.

16. **Per-window unique identifiers are required for native constructs** — `NSToolbar` identity collisions (`NSToolbar already contains item with identifier MauiTitle`) occur when opening multiple windows. Use per-window IDs for toolbars, tracking areas, and any native construct with identity semantics.

17. **Toolbar/MenuBar collections lack change notifications** — `Page.MenuBarItems` is a plain `List<MenuBarItem>` with no collection-changed events. Workarounds: use sentinel item mutations to force handler re-mapping, or diff-based toolbar refresh to detect changes without full rebuilds.

#### 🎨 Text & Visual Rendering

18. **Unified attributed text rendering prevents ordering bugs** — Mixing separate property setters (StringValue, Font, TextColor, attributed text) creates visual glitches. The winning pattern is a single `UpdateAttributedText()` method that owns the entire label rendering path (font, color, decorations, alignment, line-break mode, HTML spans).

19. **Button padding almost always requires a custom native subclass** — Most desktop platforms lack `ContentEdgeInsets` or equivalent. On macOS, `NSButton` with `Bordered=false` removes native bezel padding, requiring a `MauiNSButton` subclass overriding `IntrinsicContentSize` and `DrawRect`. Expect to write a similar subclass on any platform.

20. **Editor placeholder almost always requires a custom subclass** — `NSTextView` has no built-in placeholder support. GTK4's `GtkTextView` similarly lacks it. Plan for a `PlaceholderTextView` subclass that renders placeholder text when content is empty.

### Pitfalls & Workarounds

| Pitfall | Platform | Workaround |
|---------|----------|------------|
| `NSImage(fileName)` crashes on missing files | macOS | Use a fallback chain: bundle resource → file path → `ImageNamed` |
| `PlatformBehavior<View>` crashes | macOS | Replace with plain `Behavior<View>` |
| Shell content loaded too early → crashes | macOS | Only create content for the active page (lazy) |
| Toolbar title doesn't refresh | macOS | Update the title item text directly, don't rebuild toolbar |
| `NSTrackingArea` alone misses pointer events | macOS | Override mouse-enter/exit methods in the view subclass |
| Swipe recognizers fight each other | macOS | Combine into one native recognizer, route through swipe controller APIs |
| Button padding vanishes when bordered=false | macOS | Custom `MauiNSButton` subclass overriding `IntrinsicContentSize`/`DrawRect` |
| GTK4 minimum-based allocation fights MAUI frames | GTK4 | Custom `GtkLayoutPanel` with P/Invoke to override GTK's layout |
| AppKit layout infinite-retries on managed exceptions | macOS | Add try-catch guards around `Layout()` paths |
| EditorNSView.IntrinsicContentSize recursion | macOS | Identified and guarded — separate fix from the try-catch |
| NuGet feed policy blocks packages at import | GTK4 | Optionalize affected features (e.g., FontAwesome) rather than changing feed config |
| Graphics type/interface mismatch after import | GTK4 | `CairoPlatformImage`, `CairoGraphicsServices`, `CairoCanvas` needed adaptation |
| `NSToolbar` identity collision on multi-window | macOS | Use per-window unique toolbar item identifiers |
| `GoToAsync` on background thread → SIGSEGV | macOS | Always marshal Shell navigation to UI thread via `Dispatcher.Dispatch()` |
| `base.IntrinsicContentSize` returns NaN | macOS | Guard against NaN from base calls when padding/margins contain NaN |
| `NSColor.LabelColor` doesn't exist in bindings | macOS | Use `NSColor.ControlText` or verify binding name before use |
| `NSPopUpButton.AddItem(string)` doesn't work | macOS | Use `Menu.AddItem(new NSMenuItem(...))` instead |
| `contentViewController` assignment changes window size | macOS | Save/restore window frame around `contentViewController` changes |
| Stale app process masks code fixes | All | Always kill and restart app after rebuilding |
| `DrawString(text, x, y, ...)` silently kills later drawing | macOS | Avoid `DrawString` overloads that corrupt graphics state; use attributed strings |

### Import & Migration Lessons (from GTK4 import to maui-labs)

From importing the GTK4 backend into `dotnet/maui-labs`:

1. **Raw import → normalization**: Do the git import as a clean commit first, then normalize on a branch. Don't mix import and restructuring.
2. **`git merge -s ours` + `read-tree --prefix`**: Used instead of `git subtree` to preserve history/authorship.
3. **Nearest `Directory.Build.props` shadows parents**: The imported subtree's build props must explicitly import the repo-root props/targets.
4. **Disable central package management**: Imported backends should manage their own package versions (`ManagePackageVersionsCentrally=false`).
5. **Self-contained solution**: Each platform gets its own `.slnx` for independent builds.
6. **Feed policy matters**: Don't change repo-level NuGet feeds for one platform's dependencies. Optionalize features instead.

### Non-Standard Backend Approaches

Not all backends follow the traditional "native widget per control" pattern:

- **GDUI (Game engine)**: Uses headless SkiaSharp rendering (`SkiaCanvas` → `SKBitmap`). Built a `VisualMatrix` bulk renderer with browser-reviewable HTML output and image-diff CI artifacts. Shared contracts (`design-system-catalog`, `specimen-manifest`, `control-traceability`) enable spec-backed validation.

- **TUI (Terminal UI)**: Uses `XenoAtom.Terminal.UI`'s `LogControl` as the base primitive. Model-driven transcript with mutable reasoning entries keyed by `reasoningId`. Native mouse selection matters in terminal contexts.

- **Blazor Backend**: Pure web rendering — all controls are HTML/CSS/JS. Different handler architecture from native backends.

---

## Existing Backend Implementations

Reference these existing backends for implementation patterns:

| Backend | Platform | Repository / Location | Status |
|---------|----------|----------------------|--------|
| **Linux/GTK4** | GTK4 via GirCore | `platforms/Linux.Gtk4/` in this repo (branch: `platforms/linux-gtk4-import`) | 43 handlers, 20 essentials |
| **macOS/AppKit** | AppKit via Xamarin.Mac | [shinyorg/mauiplatforms](https://github.com/shinyorg/mauiplatforms) | 48+ handlers |
| **GDUI** | Headless SkiaSharp | [redth/Maui.GDUI](https://github.com/redth/Maui.GDUI) | Game-engine rendering |
| **Blazor** | HTML/CSS/JS | [Redth/Maui.Blazor](https://github.com/Redth/Maui.Blazor) | Web-based rendering |

> **Session history:** The full catalog of Copilot sessions used to build these backends (26 sessions across 7 categories) is maintained at `platforms/platform-backend-sessions.md` in this repo.
