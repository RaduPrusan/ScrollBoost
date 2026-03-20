# ScrollBoost Development Journal

## 2026-03-20 — Day 1: From idea to v1.1.0

### Research Phase

Started with a research question: how to build a mouse scroll acceleration tool for Windows 11.

Investigated three approaches:
1. **C# / .NET 9 / WPF** — fastest to develop, WPF for UI, Native AOT possible
2. **Rust + windows-rs** — smallest footprint but painful Win32 GUI
3. **C++ / Win32** — maximum control but extreme boilerplate

Chose C# for development speed and WPF for the settings UI.

Deep-dived into the Windows input architecture:
- `WH_MOUSE_LL` hooks intercept scroll events system-wide
- The hook callback receives a read-only `MSLLHOOKSTRUCT` — you cannot modify scroll delta in place
- The standard pattern is **suppress-and-reinject**: return non-zero to suppress, then inject a modified event via `SendInput` or `mouse_event`

### First Build — Everything Broke

Built the initial implementation with TDD (26 unit tests for the acceleration engine). The core math worked perfectly — velocity tracking with ring buffer + EMA smoothing, sigmoid/power/linear curves.

But the first live test was a disaster:
- **No tray icon** — `H.NotifyIcon.Wpf` NuGet package targets `net10.0-windows`, falls back to broken .NET Framework DLL on net9.0
- **Mouse cursor freezing every 30 seconds** — health check timer called `Install()` which created NEW timers without disposing old ones (exponential timer leak), and reinstalled the hook on a ThreadPool thread (no message pump)
- **Settings popup crash** — XAML `IsChecked="True"` fires events during `InitializeComponent()` before fields are initialized

### The Performance Breakthrough

Fixed the immediate bugs, but scroll was still laggy. Three iterations:

1. **Moved hook to dedicated thread** — the hook was on the WPF UI thread, so every mouse event had to wait for WPF rendering. Dedicated thread with bare Win32 message pump fixed cursor lag but scroll was still slow.

2. **Discovered the double hook traversal problem** — `SendInput`/`mouse_event` creates a new input event that traverses the ENTIRE LL hook chain again. With other hooks on the system (Logitech, ROCCAT, ESET), each traversal involves multiple cross-thread context switches. Two traversals per scroll = noticeable lag.

3. **The PostMessage solution** — instead of reinjecting into the input pipeline, `PostMessage(WM_MOUSEWHEEL)` sends the scroll directly to the target window, bypassing the hook chain entirely. Zero re-traversal. The user's reaction: *"holy shit Claude, you nailed it. it flies"*

### v1.0.0 — Feature Complete

- Acceleration engine with 3 curve types
- System tray with dark/light themed WPF settings popup
- Dual autostart (Registry Run key + Task Scheduler for elevated)
- Admin elevation via manifest
- Global hotkey (Ctrl+Shift+ScrollLock)
- Ctrl+Scroll zoom preserved (modifier key forwarding)
- Portable single-file EXE (163 MB self-contained)
- 26 unit tests, zero build warnings
- Published on GitHub with release

### v1.1.0 — Hybrid Injection

Discovered that `PostMessage` doesn't work for all window types:
- **UWP/WinUI** (Settings, Calculator) — uses `CoreWindow`, ignores posted `WM_MOUSEWHEEL`
- **Chromium** (Chrome, Edge, VS Code) — works on some systems, drops deltas on others
- **Windows Terminal** (XAML Islands) — unreliable routing

Researched 12 different window frameworks and built a **hybrid approach**:
- Detect window class via `GetClassNameW` (cached per-window)
- Route to `PostMessage` (fast) or `SendInput` (compatible) based on configurable rules
- Added `Passthrough` mode for games/apps that shouldn't be touched
- In-app UI with expandable rules panel
- **Window class picker** — drag a crosshair over any window to auto-detect its class name

### Key Technical Lessons

1. **Never use LL hooks on the UI thread** — every mouse event blocks until the dispatcher yields
2. **PostMessage > SendInput for scroll** — bypasses the hook chain, eliminates double traversal
3. **UWP apps need SendInput** — they don't process posted WM_MOUSEWHEEL
4. **Timer disposal matters** — `Timer.Dispose()` doesn't wait for in-flight callbacks; use the `WaitHandle` overload
5. **`MSLLHOOKSTRUCT` is read-only** — modifications are ignored by the system
6. **WPF + Native AOT = incompatible** — use `PublishSingleFile` + `SelfContained` instead
7. **`H.NotifyIcon.Wpf` is broken on net9.0** — use `System.Windows.Forms.NotifyIcon`
8. **`FrameworkElementFactory.AppendChild` doesn't work with `Track`** — use `DynamicResource` for slider theming
9. **`SM_CXSMICON` is wrong for tray icons** — Windows 11 tray uses `SM_CXICON`
10. **Window `SizeToContent="Height"` breaks positioning** — use `ActualHeight` after `Show()`

## 2026-03-21 — Day 2: Polish and UX

### UI Refinements
- **Themed context menu** — custom `ToolStripProfessionalRenderer` with dark/light colors, rounded hover highlights, matching the Windows system theme. Updates live on theme switch.
- **"Open" (bold)** added to top of right-click menu for quick access to settings.
- **Scroll counter in styled card** — bordered panel with emoji, large accent-colored number, and "accelerated scrolls" subtitle. Counts gestures (250ms+ gap = new operation), not individual ticks. Persists across restarts via `totalScrollCount` in config.json.
- **Window expands upward** when advanced panel opens — bottom edge stays anchored to tray area.
- **Tray icon sizing** — switched from `SM_CXSMICON` (too small) to `SM_CXICON` to match other Win11 tray icons. Body proportions at 90% height / 56% width.

### Config Defaults Updated
Based on real-world usage, changed defaults to match the user's preferred settings:
- Scroll Speed: 1.5x → 1.0x (no base multiplier, pure acceleration)
- Acceleration: 0.4 → 0.8 (more aggressive velocity response)
- Max Speed Cap: 12x → 30x (slider max raised to 50x)
- UWP window rules: sendinput → passthrough (user preferred native scroll in UWP)

### Double Freeze Research
Deep-dived into why `SendInput`/`mouse_event` causes a double freeze per scroll event. Root cause: the injected event re-enters the LL hook chain, and the RIT (Raw Input Thread) blocks synchronously during each traversal. With N hooks on the system, total latency = 2 × N × context_switch_time. Documented in journal as a fundamental architectural limitation of the LL hook framework.

### Stats

- 45+ commits across two sessions
- 26 unit tests
- ~3,000 lines of C# across 17 source files
- Research covered macOS/Linux acceleration algorithms, Win32 hook architecture, 12 window frameworks, LL hook double-traversal analysis
- v1.0.0 and v1.1.0 released on GitHub
