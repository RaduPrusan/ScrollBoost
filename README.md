# ScrollBoost

Configurable scroll acceleration for Windows 11. Makes your mouse wheel velocity-aware — slow scrolling stays precise, fast flicks cover more ground.

## Features

- **Velocity-based acceleration** — scroll speed scales with how fast you flick the wheel
- **Three curve types** — Sigmoid (smooth, self-capping), Power (progressive), Linear (constant multiplier)
- **System-wide** — works across all Windows apps including UWP, WinUI, and elevated windows
- **Hybrid zero-lag architecture** — uses `PostMessage` for Win32/WPF/WinForms apps (zero hook re-traversal) and `SendInput` for UWP/Chromium/XAML apps. Auto-detects per window, fully configurable.
- **Per-window-class rules** — choose PostMessage (fast), SendInput (compatible), or Passthrough (disable) per window type. Drag the crosshair picker over any window to capture its class name.
- **Windows 11 themed UI** — dark/light mode settings popup with ochre adaptive tray icon
- **Dual autostart** — Registry Run key (standard) or Task Scheduler (elevated, no UAC prompt)
- **Portable** — single EXE, config file next to it, no installer needed
- **Global hotkey** — Ctrl+Shift+ScrollLock to toggle on/off
- **Ctrl+Scroll zoom** — modifier keys are preserved, so Ctrl+Scroll zoom works in browsers/editors
- **Hook health check** — automatically recovers if Windows silently removes the hook
- **Runtime theme detection** — tray icon and settings adapt when you switch dark/light mode

## Screenshot

<img src="docs/snapshot.png?v=2" alt="ScrollBoost settings" width="280"/>

| Setting | Range | Default | Description |
|---------|-------|---------|-------------|
| Scroll Speed | 1x – 5x | 1.0x | Base multiplier applied to every scroll event |
| Acceleration | 0.0 – 1.0 | 0.80 | How aggressively speed ramps up with velocity |
| Max Speed Cap | 2x – 50x | 30x | Upper bound on the scroll multiplier |
| Curve Type | Linear / Power / Sigmoid | Sigmoid | Acceleration curve shape |

## Installation

1. Download `ScrollBoost.exe` from [Releases](https://github.com/RaduPrusan/ScrollBoost/releases)
2. Place it anywhere (Desktop, a tools folder, etc.)
3. Run it — UAC will prompt for admin rights (needed for scroll in elevated windows)
4. An ochre mouse icon appears in the system tray
5. Left-click the tray icon to open settings, right-click for quick menu

No .NET runtime needed — the EXE is fully self-contained.

## Building from Source

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
# Build
dotnet build src/ScrollBoost -c Release

# Run tests (26 tests)
dotnet test

# Publish single-file EXE
dotnet publish src/ScrollBoost -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The published EXE is at `src/ScrollBoost/bin/Release/net9.0-windows/win-x64/publish/ScrollBoost.exe`.

## Configuration

Settings are stored in `config.json` next to the EXE. Editable through the tray popup or by hand.

```json
{
  "configVersion": 1,
  "defaultProfile": {
    "baseMultiplier": 1.0,
    "curveType": "sigmoid",
    "acceleration": 0.8,
    "maxMultiplier": 30.0
  },
  "appProfiles": {},
  "gestureTimeoutMs": 250,
  "smoothingAlpha": 0.3,
  "velocityWindowSize": 4,
  "enabled": true,
  "startupMode": "none",
  "windowClassRules": {
    "ApplicationFrameWindow": "passthrough",
    "Windows.UI.Core.CoreWindow": "passthrough"
  }
}
```

### Window Class Rules

Control how scroll is delivered per window type. Expand **"Window class rules"** in the settings popup to manage rules in-app.

| Method | Speed | Use for |
|--------|-------|---------|
| `postmessage` | Zero lag | Default — Win32, WPF, WinForms, Firefox, Office, Qt |
| `sendinput` | Some lag | UWP/WinUI apps that ignore PostMessage |
| `passthrough` | Native | Games or apps where ScrollBoost should not intervene |

Drag the **crosshair picker** (◎) over any window to auto-detect its class name, then choose a method and click +.

### Advanced Settings (config.json only)

- `gestureTimeoutMs` — time between scroll events before velocity resets (default 250ms)
- `smoothingAlpha` — EMA smoothing factor for velocity detection (0.0–1.0, default 0.3)
- `velocityWindowSize` — number of events in the velocity ring buffer (default 4)

### Startup Modes

| Mode | Description |
|------|-------------|
| Off | Manual launch only |
| Start with Windows | Registry Run key — no UAC, but scroll may not work in elevated apps |
| Start elevated | Task Scheduler — runs as admin with full app coverage, no UAC prompt |

## How It Works

1. A `WH_MOUSE_LL` hook on a **dedicated thread** intercepts `WM_MOUSEWHEEL` events
2. Velocity is computed from inter-event timing using a ring buffer + exponential moving average
3. The selected acceleration curve maps velocity to a scroll multiplier
4. The target window class is detected (`GetClassNameW`) and cached:
   - **Unlisted classes** → `PostMessage` (bypasses hook chain, zero lag)
   - **Configured as `sendinput`** → `mouse_event` (input pipeline, for UWP)
   - **Configured as `passthrough`** → original event passes through unmodified
5. The original event is suppressed (except passthrough)

The hybrid approach gives zero-lag performance for most apps while correctly handling UWP and other frameworks that don't process posted `WM_MOUSEWHEEL`.

## Architecture

```
ScrollBoost.exe
├── Hook/MouseHookManager      WH_MOUSE_LL on dedicated thread, hybrid injection
├── Acceleration/
│   ├── VelocityTracker         Ring buffer + EMA velocity detection
│   ├── AccelerationEngine      Composes velocity + curve → modified delta
│   └── Curves                  Sigmoid, Power, Linear (IAccelerationCurve)
├── Profiles/
│   ├── AppConfig               JSON config load/save with migration
│   ├── ProfileManager          Per-app profile lookup
│   └── AutoStartManager        Registry Run key + Task Scheduler
├── Interop/NativeMethods       Win32 P/Invoke (LibraryImport)
└── UI/
    ├── SettingsPopup            WPF dark/light themed popup with window class rules
    ├── TrayIconHelper           DPI-aware ochre icon generation
    └── HotkeyForm              Global hotkey (Ctrl+Shift+ScrollLock)
```

## License

MIT
