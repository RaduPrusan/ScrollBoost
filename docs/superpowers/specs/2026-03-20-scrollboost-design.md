# ScrollBoost — Windows 11 Scroll Acceleration Tool

**Date:** 2026-03-20
**Status:** Approved

## Problem

Windows 11's default scroll behavior is linear and not configurable beyond a fixed "lines per notch" setting. Users want:
- An acceleration curve: scroll faster the faster you flick the wheel
- A configurable base multiplier: each notch scrolls more than the default
- Per-application profiles (v2): different scroll behavior per app

## Requirements

- **Acceleration + multiplier**: Both a velocity-based acceleration curve and a configurable base scroll multiplier
- **System-wide**: Works across all Windows 11 apps including UWP/WinUI
- **Configurable**: System tray with quick-access popup (sliders) + JSON config file for advanced settings
- **Portable**: No installer, single EXE + config.json next to it, zero registry writes (except optional auto-start)
- **Auto-start**: Optional, moderate footprint acceptable (~15-30 MB RAM)
- **Per-app profiles**: Wired internally from v1, UI exposed in v2

## Technical Approach

### Hook Mechanism: WH_MOUSE_LL Suppress-and-Reinject

Windows does not allow modifying `MSLLHOOKSTRUCT` in-place — the system uses its own copy. The proven pattern used by all successful open-source scroll tools:

1. Install `WH_MOUSE_LL` via `SetWindowsHookEx`
2. In the callback, intercept `WM_MOUSEWHEEL` / `WM_MOUSEHWHEEL`
3. Check re-entrancy guard: a thread-local `_isInjecting` boolean flag set around `SendInput` calls, combined with `LLMHF_INJECTED` flag check — if either indicates a self-injected event, pass through
4. Suppress the original event (return non-zero, do NOT call `CallNextHookEx`)
5. Compute the accelerated delta via the acceleration engine
6. Inject a new scroll event via `SendInput` with `MOUSEEVENTF_WHEEL`

**Constraints:**
- Hook callback must return within 300ms (default `LowLevelHooksTimeout`), max 1000ms on Win10+
- Hook runs on the installing thread — that thread must pump a message loop
- Windows silently removes the hook on timeout with no notification
- Standard-integrity hook cannot intercept elevated (admin) windows — running as admin extends coverage

### Technology Stack

| Component | Choice |
|---|---|
| Language | C# / .NET 9 |
| Compilation | Self-contained single-file publish (WPF is not AOT-compatible; use `PublishSingleFile` + `PublishTrimmed` instead) |
| Win32 Interop | P/Invoke for `SetWindowsHookEx`, `SendInput`, `GetWindowThreadProcessId`, etc. |
| GUI | WPF for tray icon + popup settings panel (requires .NET Desktop runtime, bundled via self-contained publish) |
| Config | `System.Text.Json` for config.json |
| Distribution | `.zip` — unzip and run |

## Architecture

Three logical layers in a single process:

### 1. Mouse Hook Layer

- Installs `WH_MOUSE_LL` on a dedicated message-pumping thread
- Intercepts `WM_MOUSEWHEEL` (vertical). `WM_MOUSEHWHEEL` (horizontal) is passed through unmodified in v1 via `CallNextHookEx`.
- Resolves the target app via `WindowFromPoint(cursor)` → `GetWindowThreadProcessId` → process name
- Looks up the matching profile (app-specific or default)
- Passes the original delta + profile to the Acceleration Engine
- Injects the modified delta via `SendInput`
- Re-installs the hook if it detects removal (periodic self-check via timer)

### 2. Acceleration Engine

Pure math, no Win32 dependencies. Testable in isolation.

**Velocity Detection:**
- Ring buffer of last 4 scroll event timestamps
- Events older than the gesture timeout (default 250ms, configurable) are discarded — treated as a new gesture
- Velocity = `event_count / time_span` (notches per second)
- Smoothed via exponential moving average (EMA, alpha = 0.3)

**Acceleration Curves:**

Three options, user-selectable:

| Curve | Formula | Behavior |
|---|---|---|
| **Linear** | `factor = baseMultiplier` | Constant multiplier, no acceleration |
| **Power** | `factor = base + k * velocity^gamma` | Progressive, needs max cap |
| **Sigmoid** | `factor = base + (max - base) / (1 + e^(-k * (velocity - midpoint)))` | Self-capping S-curve, recommended default |

**Output:** `modifiedDelta = originalDelta * clamp(factor, 1.0, maxMultiplier)`

**Typical velocity ranges (standard 24-notch mouse):**
- Slow: 1-5 notches/sec
- Medium: 5-15 notches/sec
- Fast: 15-40 notches/sec
- Very fast: 40+ notches/sec (free-spinning wheels)

### 3. Tray UI (WPF)

**Left-click tray icon** → popup panel with:

| Setting | Control | Range | Default |
|---|---|---|---|
| Scroll Speed | Slider | 1x – 5x | 1.5x |
| Acceleration | Slider | Off – High (0.0 – 1.0) | 0.4 |
| Max Speed Cap | Slider | 2x – 30x | 12x |
| Curve Type | Dropdown | Linear / Power / Sigmoid | Sigmoid |
| Enable/Disable | Toggle | on/off | on |
| Start with Windows | Toggle | on/off | off |

**Right-click tray icon** → context menu: Enable/Disable, Open Config File, About, Exit

**Global hotkey:** Ctrl+Shift+ScrollLock to toggle enable/disable

## Per-App Profile System

### Resolution

On every scroll event:
1. `WindowFromPoint(cursorPosition)` → target window handle
2. `GetWindowThreadProcessId(hwnd)` → PID (fast, no process handle needed)
3. Cached `Dictionary<int, string>` maps PID → process name. On cache hit: immediate lookup. On cache miss: use default profile immediately, queue process name resolution to a background thread, cache result for subsequent events. Cache entries expire after ~2 seconds.
4. Look up profile by process name, fall back to default

**Performance note:** `WindowFromPoint` and `GetWindowThreadProcessId` are fast Win32 calls safe for the hook callback. The expensive `Process.GetProcessById(pid).ProcessName` call must never run inside the hook — it is always deferred to a background thread on cache miss.

Using `WindowFromPoint` instead of `GetForegroundWindow` because scroll targets the window under the cursor, not the focused window.

### Config Structure

```json
{
  "configVersion": 1,
  "defaultProfile": {
    "baseMultiplier": 1.5,
    "curveType": "sigmoid",
    "acceleration": 0.4,
    "maxMultiplier": 12.0
  },
  "appProfiles": {
    "firefox": {
      "baseMultiplier": 2.0,
      "acceleration": 0.6,
      "maxMultiplier": 15.0
    },
    "excel": {
      "baseMultiplier": 1.0,
      "acceleration": 0.2,
      "maxMultiplier": 5.0
    }
  },
  "gestureTimeoutMs": 250,
  "smoothingAlpha": 0.3,
  "velocityWindowSize": 4,
  "enabled": true,
  "startWithWindows": false
}
```

### Rollout

- **v1**: Profile engine wired internally, only default profile exposed in UI
- **v2**: "Per-App" tab in tray popup — add/edit/delete profiles, pick process from running apps

## Portable App Design

### Principles

- Zero installation, zero registry writes (except optional auto-start)
- Config lives next to the EXE (`AppContext.BaseDirectory / config.json`)
- First run creates `config.json` with defaults if missing
- Distributed as a `.zip`

### Auto-Start

- Disabled by default
- When enabled: writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `"ScrollBoost" = "<exe path>"`
- When disabled: removes the key
- On launch: if auto-start is enabled and registry key points to a different path, self-heals to current path

### Single Instance

- Named mutex `Global\ScrollBoost` prevents duplicate instances
- Second launch brings existing tray icon to front (via `PostMessage` to the first instance's hidden window)

### Admin Elevation

- Not required for normal operation
- Running as admin extends hook coverage to elevated windows
- UI note explains this; no auto-elevation

## Publish Command

```bash
dotnet publish -r win-x64 -c Release -p:PublishSingleFile=true -p:PublishTrimmed=true --self-contained true
```

Produces a single self-contained EXE (~30-50 MB) with the .NET runtime bundled. WPF is not compatible with Native AOT due to its reliance on runtime reflection and dynamic XAML loading, so we use single-file + trimming instead. The binary is fully portable — no .NET install needed on the target machine.

## Risk Mitigation

| Risk | Mitigation |
|---|---|
| Hook silently removed on timeout | Periodic self-check timer (every 5s) re-installs if needed |
| Infinite loop from self-injected events | Dual guard: thread-local `_isInjecting` flag + `LLMHF_INJECTED` check |
| High-resolution mice (sub-120 delta) | Normalize delta to notches before velocity calc |
| Free-spinning wheels (very high velocity) | Max cap clamps the multiplier |
| Config file corruption | Catch `JsonException`, fall back to defaults, warn user |
| Config version mismatch | `configVersion` field; missing fields get defaults, unknown fields are preserved |
| UWP/WinUI compatibility | `WH_MOUSE_LL` works across all desktop frameworks |

## Out of Scope (v1)

- Smooth scrolling animation (pixel interpolation over time)
- Horizontal scroll acceleration (pass-through only)
- Per-app profile UI (engine ready, UI deferred)
- Multi-monitor DPI awareness for the tray popup
- Installer / MSIX packaging
