# ScrollBoost Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a portable Windows 11 scroll acceleration tool with configurable velocity-based curves, a system tray UI, and per-app profile support.

**Architecture:** A single-process C# WPF app with three layers: (1) a `WH_MOUSE_LL` hook that intercepts and reinjects scroll events, (2) a pure-math acceleration engine using ring-buffer velocity detection and sigmoid/power/linear curves, (3) a WPF system tray popup with sliders for configuration. Per-app profiles resolved via `WindowFromPoint` + cached PID-to-process-name mapping.

**Tech Stack:** C# / .NET 9, WPF, `H.NotifyIcon.Wpf`, `LibraryImport` P/Invoke, xUnit, `System.Text.Json`

**Spec:** `docs/superpowers/specs/2026-03-20-scrollboost-design.md`

---

## File Structure

```
ScrollBoost/
├── ScrollBoost.sln
├── src/
│   └── ScrollBoost/
│       ├── ScrollBoost.csproj
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── Interop/
│       │   └── NativeMethods.cs              # Win32 P/Invoke declarations
│       ├── Hook/
│       │   └── MouseHookManager.cs           # WH_MOUSE_LL install/callback/inject
│       ├── Acceleration/
│       │   ├── VelocityTracker.cs            # Ring buffer + EMA velocity detection
│       │   ├── IAccelerationCurve.cs         # Curve interface
│       │   ├── LinearCurve.cs                # factor = baseMultiplier
│       │   ├── PowerCurve.cs                 # factor = base + k * velocity^gamma
│       │   ├── SigmoidCurve.cs              # factor = base + (max-base) / (1 + e^(-k*(v-mid)))
│       │   └── AccelerationEngine.cs         # Orchestrates velocity + curve → modified delta
│       ├── Profiles/
│       │   ├── ScrollProfile.cs              # Profile data model
│       │   ├── ProfileManager.cs             # Per-app lookup with PID cache
│       │   └── AppConfig.cs                  # JSON config load/save
│       ├── UI/
│       │   ├── SettingsPopup.xaml             # WPF popup with sliders
│       │   └── SettingsPopup.xaml.cs          # Code-behind
│       └── Resources/
│           └── app.ico                        # Tray icon (16x16 + 32x32 + 48x48)
├── tests/
│   └── ScrollBoost.Tests/
│       ├── ScrollBoost.Tests.csproj
│       ├── VelocityTrackerTests.cs
│       ├── AccelerationCurveTests.cs
│       ├── AccelerationEngineTests.cs
│       ├── ProfileManagerTests.cs
│       └── AppConfigTests.cs
```

---

## Task 1: Project Scaffolding

**Files:**
- Create: `ScrollBoost.sln`
- Create: `src/ScrollBoost/ScrollBoost.csproj`
- Create: `tests/ScrollBoost.Tests/ScrollBoost.Tests.csproj`

- [ ] **Step 1: Create solution and projects**

```bash
cd "F:/- Projects -/ClaudeCode/scroll accellerator"
dotnet new sln -n ScrollBoost
dotnet new wpf -n ScrollBoost --framework net9.0-windows -o src/ScrollBoost
dotnet new xunit -n ScrollBoost.Tests --framework net9.0-windows -o tests/ScrollBoost.Tests
dotnet sln ScrollBoost.sln add src/ScrollBoost/ScrollBoost.csproj
dotnet sln ScrollBoost.sln add tests/ScrollBoost.Tests/ScrollBoost.Tests.csproj
dotnet add tests/ScrollBoost.Tests/ScrollBoost.Tests.csproj reference src/ScrollBoost/ScrollBoost.csproj
```

- [ ] **Step 2: Configure the app .csproj**

Edit `src/ScrollBoost/ScrollBoost.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <RootNamespace>ScrollBoost</RootNamespace>
    <AssemblyName>ScrollBoost</AssemblyName>
    <ApplicationIcon>Resources\app.ico</ApplicationIcon>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Configure the test .csproj**

Edit `tests/ScrollBoost.Tests/ScrollBoost.Tests.csproj` — ensure it targets `net9.0-windows`:
```xml
<PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
</PropertyGroup>
```

- [ ] **Step 4: Add NuGet package for tray icon**

```bash
dotnet add src/ScrollBoost/ScrollBoost.csproj package H.NotifyIcon.Wpf
```

- [ ] **Step 5: Verify build**

```bash
dotnet build ScrollBoost.sln
```
Expected: Build succeeded. 0 Errors.

- [ ] **Step 6: Verify tests run**

```bash
dotnet test ScrollBoost.sln
```
Expected: Passed! (default template test)

- [ ] **Step 7: Create .gitignore**

```bash
dotnet new gitignore
```

- [ ] **Step 8: Commit**

```bash
git init
git add -A
git commit -m "chore: scaffold ScrollBoost solution with WPF app and xUnit tests"
```

---

## Task 2: VelocityTracker (TDD)

**Files:**
- Create: `src/ScrollBoost/Acceleration/VelocityTracker.cs`
- Create: `tests/ScrollBoost.Tests/VelocityTrackerTests.cs`

The VelocityTracker maintains a ring buffer of event timestamps and computes smoothed scroll velocity in notches/second.

- [ ] **Step 1: Write failing tests**

Create `tests/ScrollBoost.Tests/VelocityTrackerTests.cs`:

```csharp
using ScrollBoost.Acceleration;

namespace ScrollBoost.Tests;

public class VelocityTrackerTests
{
    [Fact]
    public void FirstEvent_ReturnsZeroVelocity()
    {
        var tracker = new VelocityTracker(windowSize: 4, gestureTimeoutMs: 250, smoothingAlpha: 0.3);
        double velocity = tracker.RecordEventAndGetVelocity(1000);
        Assert.Equal(0.0, velocity);
    }

    [Fact]
    public void TwoEvents_50msApart_Returns20NotchesPerSec()
    {
        var tracker = new VelocityTracker(windowSize: 4, gestureTimeoutMs: 250, smoothingAlpha: 1.0);
        tracker.RecordEventAndGetVelocity(1000);
        double velocity = tracker.RecordEventAndGetVelocity(1050);
        // 1 notch / 0.05s = 20 notches/sec
        Assert.Equal(20.0, velocity, precision: 1);
    }

    [Fact]
    public void EventAfterTimeout_ResetsVelocity()
    {
        var tracker = new VelocityTracker(windowSize: 4, gestureTimeoutMs: 250, smoothingAlpha: 1.0);
        tracker.RecordEventAndGetVelocity(1000);
        tracker.RecordEventAndGetVelocity(1050); // fast scroll
        // 500ms gap — exceeds 250ms timeout
        double velocity = tracker.RecordEventAndGetVelocity(1550);
        Assert.Equal(0.0, velocity);
    }

    [Fact]
    public void MultipleEvents_ComputesWindowedVelocity()
    {
        var tracker = new VelocityTracker(windowSize: 4, gestureTimeoutMs: 250, smoothingAlpha: 1.0);
        tracker.RecordEventAndGetVelocity(1000);
        tracker.RecordEventAndGetVelocity(1040);
        tracker.RecordEventAndGetVelocity(1080);
        double velocity = tracker.RecordEventAndGetVelocity(1120);
        // 3 intervals over 120ms = 3/0.12 = 25 notches/sec
        Assert.Equal(25.0, velocity, precision: 1);
    }

    [Fact]
    public void EmaSmoothing_DampensSpikes()
    {
        var tracker = new VelocityTracker(windowSize: 4, gestureTimeoutMs: 250, smoothingAlpha: 0.3);
        tracker.RecordEventAndGetVelocity(1000);
        double v1 = tracker.RecordEventAndGetVelocity(1100); // 10 n/s — raw
        double v2 = tracker.RecordEventAndGetVelocity(1120); // raw velocity jumps (shorter interval)
        // With alpha=0.3, EMA dampens: v2 = 0.3*raw + 0.7*v1, so v2 < raw
        Assert.True(v2 < 50.0, $"EMA should dampen spike, got {v2}");
        Assert.True(v2 > v1, $"Velocity should increase, got {v2} vs {v1}");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ScrollBoost.Tests --filter "VelocityTrackerTests" -v n
```
Expected: Build error — `VelocityTracker` does not exist.

- [ ] **Step 3: Implement VelocityTracker**

Create `src/ScrollBoost/Acceleration/VelocityTracker.cs`:

```csharp
namespace ScrollBoost.Acceleration;

public class VelocityTracker
{
    private readonly int _windowSize;
    private readonly double _gestureTimeoutMs;
    private readonly double _smoothingAlpha;
    private readonly long[] _timestamps;
    private int _count;
    private int _head;
    private double _smoothedVelocity;

    public VelocityTracker(int windowSize = 4, double gestureTimeoutMs = 250, double smoothingAlpha = 0.3)
    {
        _windowSize = windowSize;
        _gestureTimeoutMs = gestureTimeoutMs;
        _smoothingAlpha = smoothingAlpha;
        _timestamps = new long[windowSize];
        _count = 0;
        _head = 0;
        _smoothedVelocity = 0;
    }

    public double RecordEventAndGetVelocity(long timestampMs)
    {
        // Check gesture timeout — if gap too large, reset
        if (_count > 0)
        {
            int lastIndex = (_head - 1 + _windowSize) % _windowSize;
            long lastTimestamp = _timestamps[lastIndex];
            if (timestampMs - lastTimestamp > _gestureTimeoutMs)
            {
                _count = 0;
                _head = 0;
                _smoothedVelocity = 0;
            }
        }

        // Add timestamp to ring buffer
        _timestamps[_head] = timestampMs;
        _head = (_head + 1) % _windowSize;
        if (_count < _windowSize) _count++;

        // Need at least 2 events to compute velocity
        if (_count < 2)
        {
            _smoothedVelocity = 0;
            return 0;
        }

        // Compute windowed velocity: (count-1) intervals over time span
        int oldest = _count < _windowSize
            ? 0
            : _head; // oldest is at _head when buffer is full
        int newest = (_head - 1 + _windowSize) % _windowSize;

        int oldestIndex = _count < _windowSize
            ? (_head - _count + _windowSize) % _windowSize
            : _head;

        long oldestTimestamp = _timestamps[oldestIndex];
        long newestTimestamp = _timestamps[newest];
        double timeSpanMs = newestTimestamp - oldestTimestamp;

        if (timeSpanMs <= 0) return _smoothedVelocity;

        double rawVelocity = (_count - 1) / (timeSpanMs / 1000.0);

        // Apply EMA smoothing
        _smoothedVelocity = _smoothingAlpha * rawVelocity + (1 - _smoothingAlpha) * _smoothedVelocity;

        return _smoothedVelocity;
    }

    public void Reset()
    {
        _count = 0;
        _head = 0;
        _smoothedVelocity = 0;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/ScrollBoost.Tests --filter "VelocityTrackerTests" -v n
```
Expected: 5 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/ScrollBoost/Acceleration/VelocityTracker.cs tests/ScrollBoost.Tests/VelocityTrackerTests.cs
git commit -m "feat: add VelocityTracker with ring buffer and EMA smoothing"
```

---

## Task 3: Acceleration Curves (TDD)

**Files:**
- Create: `src/ScrollBoost/Acceleration/IAccelerationCurve.cs`
- Create: `src/ScrollBoost/Acceleration/LinearCurve.cs`
- Create: `src/ScrollBoost/Acceleration/PowerCurve.cs`
- Create: `src/ScrollBoost/Acceleration/SigmoidCurve.cs`
- Create: `tests/ScrollBoost.Tests/AccelerationCurveTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/ScrollBoost.Tests/AccelerationCurveTests.cs`:

```csharp
using ScrollBoost.Acceleration;

namespace ScrollBoost.Tests;

public class AccelerationCurveTests
{
    // --- Linear ---

    [Fact]
    public void Linear_ReturnsBaseMultiplier_Regardless()
    {
        var curve = new LinearCurve(baseMultiplier: 2.0);
        Assert.Equal(2.0, curve.Evaluate(0));
        Assert.Equal(2.0, curve.Evaluate(10));
        Assert.Equal(2.0, curve.Evaluate(100));
    }

    // --- Power ---

    [Fact]
    public void Power_AtZeroVelocity_ReturnsBase()
    {
        var curve = new PowerCurve(baseMultiplier: 1.5, gamma: 2.0, maxMultiplier: 20.0);
        double factor = curve.Evaluate(0);
        Assert.Equal(1.5, factor, precision: 2);
    }

    [Fact]
    public void Power_IncreasesWithVelocity()
    {
        var curve = new PowerCurve(baseMultiplier: 1.0, gamma: 2.0, maxMultiplier: 50.0);
        double slow = curve.Evaluate(5);
        double fast = curve.Evaluate(20);
        Assert.True(fast > slow, $"fast={fast} should be > slow={slow}");
    }

    [Fact]
    public void Power_ClampsAtMax()
    {
        var curve = new PowerCurve(baseMultiplier: 1.0, gamma: 2.0, maxMultiplier: 10.0);
        double extreme = curve.Evaluate(1000);
        Assert.Equal(10.0, extreme, precision: 2);
    }

    // --- Sigmoid ---

    [Fact]
    public void Sigmoid_AtZeroVelocity_ReturnsNearBase()
    {
        var curve = new SigmoidCurve(baseMultiplier: 1.0, maxMultiplier: 15.0, midpoint: 15.0, steepness: 0.3);
        double factor = curve.Evaluate(0);
        Assert.True(factor < 2.0, $"At v=0, factor should be near base, got {factor}");
    }

    [Fact]
    public void Sigmoid_AtMidpoint_ReturnsHalfway()
    {
        var curve = new SigmoidCurve(baseMultiplier: 1.0, maxMultiplier: 15.0, midpoint: 15.0, steepness: 0.3);
        double factor = curve.Evaluate(15);
        double expected = (1.0 + 15.0) / 2.0; // 8.0
        Assert.Equal(expected, factor, precision: 0);
    }

    [Fact]
    public void Sigmoid_AtHighVelocity_ApproachesMax()
    {
        var curve = new SigmoidCurve(baseMultiplier: 1.0, maxMultiplier: 15.0, midpoint: 15.0, steepness: 0.3);
        double factor = curve.Evaluate(100);
        Assert.True(factor > 14.0, $"At v=100, factor should approach max, got {factor}");
    }

    [Fact]
    public void Sigmoid_IsMonotonicallyIncreasing()
    {
        var curve = new SigmoidCurve(baseMultiplier: 1.0, maxMultiplier: 15.0, midpoint: 15.0, steepness: 0.3);
        double prev = curve.Evaluate(0);
        for (int v = 1; v <= 50; v++)
        {
            double current = curve.Evaluate(v);
            Assert.True(current >= prev, $"Curve should be monotonic: v={v}, current={current}, prev={prev}");
            prev = current;
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ScrollBoost.Tests --filter "AccelerationCurveTests" -v n
```
Expected: Build error — types don't exist.

- [ ] **Step 3: Implement the interface and curves**

Create `src/ScrollBoost/Acceleration/IAccelerationCurve.cs`:
```csharp
namespace ScrollBoost.Acceleration;

public interface IAccelerationCurve
{
    double Evaluate(double velocity);
}
```

Create `src/ScrollBoost/Acceleration/LinearCurve.cs`:
```csharp
namespace ScrollBoost.Acceleration;

public class LinearCurve : IAccelerationCurve
{
    private readonly double _baseMultiplier;

    public LinearCurve(double baseMultiplier)
    {
        _baseMultiplier = baseMultiplier;
    }

    public double Evaluate(double velocity) => _baseMultiplier;
}
```

Create `src/ScrollBoost/Acceleration/PowerCurve.cs`:
```csharp
namespace ScrollBoost.Acceleration;

public class PowerCurve : IAccelerationCurve
{
    private readonly double _baseMultiplier;
    private readonly double _gamma;
    private readonly double _maxMultiplier;
    private readonly double _scale;

    public PowerCurve(double baseMultiplier, double gamma, double maxMultiplier)
    {
        _baseMultiplier = baseMultiplier;
        _gamma = gamma;
        _maxMultiplier = maxMultiplier;
        // Scale factor so that at velocity=30 (fast scroll), we reach ~maxMultiplier
        _scale = (maxMultiplier - baseMultiplier) / Math.Pow(30.0, gamma);
    }

    public double Evaluate(double velocity)
    {
        double factor = _baseMultiplier + _scale * Math.Pow(velocity, _gamma);
        return Math.Min(factor, _maxMultiplier);
    }
}
```

Create `src/ScrollBoost/Acceleration/SigmoidCurve.cs`:
```csharp
namespace ScrollBoost.Acceleration;

public class SigmoidCurve : IAccelerationCurve
{
    private readonly double _baseMultiplier;
    private readonly double _maxMultiplier;
    private readonly double _midpoint;
    private readonly double _steepness;

    public SigmoidCurve(double baseMultiplier, double maxMultiplier, double midpoint, double steepness)
    {
        _baseMultiplier = baseMultiplier;
        _maxMultiplier = maxMultiplier;
        _midpoint = midpoint;
        _steepness = steepness;
    }

    public double Evaluate(double velocity)
    {
        double sigmoid = 1.0 / (1.0 + Math.Exp(-_steepness * (velocity - _midpoint)));
        return _baseMultiplier + (_maxMultiplier - _baseMultiplier) * sigmoid;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/ScrollBoost.Tests --filter "AccelerationCurveTests" -v n
```
Expected: 8 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/ScrollBoost/Acceleration/ tests/ScrollBoost.Tests/AccelerationCurveTests.cs
git commit -m "feat: add acceleration curves (linear, power, sigmoid)"
```

---

## Task 4: AccelerationEngine (TDD)

**Files:**
- Create: `src/ScrollBoost/Acceleration/AccelerationEngine.cs`
- Create: `tests/ScrollBoost.Tests/AccelerationEngineTests.cs`

The engine composes VelocityTracker + curve to produce a modified scroll delta.

- [ ] **Step 1: Write failing tests**

Create `tests/ScrollBoost.Tests/AccelerationEngineTests.cs`:

```csharp
using ScrollBoost.Acceleration;

namespace ScrollBoost.Tests;

public class AccelerationEngineTests
{
    [Fact]
    public void SingleEvent_ReturnsOriginalDelta()
    {
        var engine = new AccelerationEngine(
            new SigmoidCurve(1.0, 15.0, 15.0, 0.3),
            gestureTimeoutMs: 250);
        int result = engine.ProcessScroll(120, timestampMs: 1000);
        // First event, velocity=0, sigmoid at 0 ≈ base (1.0), so delta stays ~120
        Assert.InRange(result, 100, 140);
    }

    [Fact]
    public void FastScrolling_AmplifiesDelta()
    {
        var engine = new AccelerationEngine(
            new SigmoidCurve(1.0, 15.0, 15.0, 0.3),
            gestureTimeoutMs: 250);

        engine.ProcessScroll(120, 1000);
        engine.ProcessScroll(120, 1030);
        engine.ProcessScroll(120, 1060);
        int result = engine.ProcessScroll(120, 1090);

        // Fast scrolling (~33 notches/sec), factor should be well above 1
        Assert.True(result > 120, $"Fast scroll should amplify delta, got {result}");
    }

    [Fact]
    public void NegativeDelta_PreservesDirection()
    {
        var engine = new AccelerationEngine(
            new SigmoidCurve(1.5, 15.0, 15.0, 0.3),
            gestureTimeoutMs: 250);

        int result = engine.ProcessScroll(-120, 1000);
        Assert.True(result < 0, "Negative delta should remain negative");
    }

    [Fact]
    public void Disabled_ReturnsOriginalDelta()
    {
        var engine = new AccelerationEngine(
            new SigmoidCurve(2.0, 15.0, 15.0, 0.3),
            gestureTimeoutMs: 250);
        engine.Enabled = false;

        int result = engine.ProcessScroll(120, 1000);
        Assert.Equal(120, result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ScrollBoost.Tests --filter "AccelerationEngineTests" -v n
```
Expected: Build error — `AccelerationEngine` does not exist.

- [ ] **Step 3: Implement AccelerationEngine**

Create `src/ScrollBoost/Acceleration/AccelerationEngine.cs`:

```csharp
namespace ScrollBoost.Acceleration;

public class AccelerationEngine
{
    private readonly VelocityTracker _velocityTracker;
    private IAccelerationCurve _curve;

    public bool Enabled { get; set; } = true;

    public AccelerationEngine(IAccelerationCurve curve, double gestureTimeoutMs = 250,
        int windowSize = 4, double smoothingAlpha = 0.3)
    {
        _curve = curve;
        _velocityTracker = new VelocityTracker(windowSize, gestureTimeoutMs, smoothingAlpha);
    }

    public void SetCurve(IAccelerationCurve curve)
    {
        _curve = curve;
    }

    public int ProcessScroll(int originalDelta, long timestampMs)
    {
        if (!Enabled) return originalDelta;

        double velocity = _velocityTracker.RecordEventAndGetVelocity(timestampMs);
        double factor = _curve.Evaluate(velocity);

        // Preserve direction, apply factor to magnitude
        double result = originalDelta * factor;

        // Round away from zero to ensure at least the original notch count
        if (result > 0)
            return Math.Max((int)Math.Ceiling(result), originalDelta);
        else if (result < 0)
            return Math.Min((int)Math.Floor(result), originalDelta);
        else
            return 0;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/ScrollBoost.Tests --filter "AccelerationEngineTests" -v n
```
Expected: 4 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/ScrollBoost/Acceleration/AccelerationEngine.cs tests/ScrollBoost.Tests/AccelerationEngineTests.cs
git commit -m "feat: add AccelerationEngine composing velocity tracker and curves"
```

---

## Task 5: Config and Profile System (TDD)

**Files:**
- Create: `src/ScrollBoost/Profiles/ScrollProfile.cs`
- Create: `src/ScrollBoost/Profiles/AppConfig.cs`
- Create: `src/ScrollBoost/Profiles/ProfileManager.cs`
- Create: `tests/ScrollBoost.Tests/AppConfigTests.cs`
- Create: `tests/ScrollBoost.Tests/ProfileManagerTests.cs`

- [ ] **Step 1: Write AppConfig tests**

Create `tests/ScrollBoost.Tests/AppConfigTests.cs`:

```csharp
using ScrollBoost.Profiles;

namespace ScrollBoost.Tests;

public class AppConfigTests
{
    [Fact]
    public void DefaultConfig_HasSensibleDefaults()
    {
        var config = AppConfig.CreateDefault();
        Assert.Equal(1.5, config.DefaultProfile.BaseMultiplier);
        Assert.Equal("sigmoid", config.DefaultProfile.CurveType);
        Assert.Equal(0.4, config.DefaultProfile.Acceleration);
        Assert.Equal(12.0, config.DefaultProfile.MaxMultiplier);
        Assert.True(config.Enabled);
        Assert.False(config.StartWithWindows);
    }

    [Fact]
    public void RoundTrip_SerializeDeserialize()
    {
        var config = AppConfig.CreateDefault();
        config.AppProfiles["firefox"] = new ScrollProfile
        {
            BaseMultiplier = 2.0,
            CurveType = "power",
            Acceleration = 0.6,
            MaxMultiplier = 15.0
        };

        string json = config.ToJson();
        var loaded = AppConfig.FromJson(json);

        Assert.Equal(config.DefaultProfile.BaseMultiplier, loaded.DefaultProfile.BaseMultiplier);
        Assert.True(loaded.AppProfiles.ContainsKey("firefox"));
        Assert.Equal(2.0, loaded.AppProfiles["firefox"].BaseMultiplier);
    }

    [Fact]
    public void FromJson_MissingFields_GetDefaults()
    {
        string json = """{"configVersion": 1, "defaultProfile": {"baseMultiplier": 3.0}}""";
        var config = AppConfig.FromJson(json);
        Assert.Equal(3.0, config.DefaultProfile.BaseMultiplier);
        // Other fields should have defaults
        Assert.Equal("sigmoid", config.DefaultProfile.CurveType);
        Assert.True(config.Enabled);
    }

    [Fact]
    public void FromJson_InvalidJson_ReturnsDefault()
    {
        var config = AppConfig.FromJson("not valid json {{{");
        Assert.Equal(1.5, config.DefaultProfile.BaseMultiplier);
    }
}
```

- [ ] **Step 2: Write ProfileManager tests**

Create `tests/ScrollBoost.Tests/ProfileManagerTests.cs`:

```csharp
using ScrollBoost.Profiles;

namespace ScrollBoost.Tests;

public class ProfileManagerTests
{
    [Fact]
    public void GetProfile_UnknownApp_ReturnsDefault()
    {
        var config = AppConfig.CreateDefault();
        var manager = new ProfileManager(config);
        var profile = manager.GetProfile("someunknownapp");
        Assert.Equal(config.DefaultProfile.BaseMultiplier, profile.BaseMultiplier);
    }

    [Fact]
    public void GetProfile_KnownApp_ReturnsAppProfile()
    {
        var config = AppConfig.CreateDefault();
        config.AppProfiles["firefox"] = new ScrollProfile
        {
            BaseMultiplier = 2.5,
            CurveType = "linear",
            Acceleration = 0.0,
            MaxMultiplier = 5.0
        };
        var manager = new ProfileManager(config);
        var profile = manager.GetProfile("firefox");
        Assert.Equal(2.5, profile.BaseMultiplier);
    }

    [Fact]
    public void GetProfile_CaseInsensitive()
    {
        var config = AppConfig.CreateDefault();
        config.AppProfiles["firefox"] = new ScrollProfile { BaseMultiplier = 2.5 };
        var manager = new ProfileManager(config);
        Assert.Equal(2.5, manager.GetProfile("Firefox").BaseMultiplier);
        Assert.Equal(2.5, manager.GetProfile("FIREFOX").BaseMultiplier);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/ScrollBoost.Tests --filter "AppConfigTests|ProfileManagerTests" -v n
```
Expected: Build errors — types don't exist.

- [ ] **Step 4: Implement ScrollProfile**

Create `src/ScrollBoost/Profiles/ScrollProfile.cs`:

```csharp
using System.Text.Json.Serialization;

namespace ScrollBoost.Profiles;

public class ScrollProfile
{
    [JsonPropertyName("baseMultiplier")]
    public double BaseMultiplier { get; set; } = 1.5;

    [JsonPropertyName("curveType")]
    public string CurveType { get; set; } = "sigmoid";

    [JsonPropertyName("acceleration")]
    public double Acceleration { get; set; } = 0.4;

    [JsonPropertyName("maxMultiplier")]
    public double MaxMultiplier { get; set; } = 12.0;
}
```

- [ ] **Step 5: Implement AppConfig**

Create `src/ScrollBoost/Profiles/AppConfig.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrollBoost.Profiles;

public class AppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonPropertyName("configVersion")]
    public int ConfigVersion { get; set; } = 1;

    [JsonPropertyName("defaultProfile")]
    public ScrollProfile DefaultProfile { get; set; } = new();

    [JsonPropertyName("appProfiles")]
    public Dictionary<string, ScrollProfile> AppProfiles { get; set; } = new();

    [JsonPropertyName("gestureTimeoutMs")]
    public int GestureTimeoutMs { get; set; } = 250;

    [JsonPropertyName("smoothingAlpha")]
    public double SmoothingAlpha { get; set; } = 0.3;

    [JsonPropertyName("velocityWindowSize")]
    public int VelocityWindowSize { get; set; } = 4;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = false;

    public static AppConfig CreateDefault() => new();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static AppConfig FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? CreateDefault();
        }
        catch (JsonException)
        {
            return CreateDefault();
        }
    }

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path)) return CreateDefault();
        string json = File.ReadAllText(path);
        return FromJson(json);
    }

    public void Save(string path)
    {
        File.WriteAllText(path, ToJson());
    }
}
```

- [ ] **Step 6: Implement ProfileManager**

Create `src/ScrollBoost/Profiles/ProfileManager.cs`:

```csharp
namespace ScrollBoost.Profiles;

public class ProfileManager
{
    private AppConfig _config;
    private readonly Dictionary<string, ScrollProfile> _normalizedProfiles = new(StringComparer.OrdinalIgnoreCase);

    public AppConfig Config => _config;

    public ProfileManager(AppConfig config)
    {
        _config = config;
        RebuildIndex();
    }

    public void UpdateConfig(AppConfig config)
    {
        _config = config;
        RebuildIndex();
    }

    public ScrollProfile GetProfile(string processName)
    {
        if (string.IsNullOrEmpty(processName))
            return _config.DefaultProfile;

        return _normalizedProfiles.TryGetValue(processName, out var profile)
            ? profile
            : _config.DefaultProfile;
    }

    private void RebuildIndex()
    {
        _normalizedProfiles.Clear();
        foreach (var (key, value) in _config.AppProfiles)
        {
            _normalizedProfiles[key] = value;
        }
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
dotnet test tests/ScrollBoost.Tests --filter "AppConfigTests|ProfileManagerTests" -v n
```
Expected: 7 passed, 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/ScrollBoost/Profiles/ tests/ScrollBoost.Tests/AppConfigTests.cs tests/ScrollBoost.Tests/ProfileManagerTests.cs
git commit -m "feat: add config system with JSON persistence and per-app profile manager"
```

---

## Task 6: Win32 P/Invoke Declarations

**Files:**
- Create: `src/ScrollBoost/Interop/NativeMethods.cs`

No tests for this task — these are pure extern declarations verified by integration in Task 7.

- [ ] **Step 1: Create NativeMethods**

Create `src/ScrollBoost/Interop/NativeMethods.cs`:

```csharp
using System.Runtime.InteropServices;

namespace ScrollBoost.Interop;

internal static partial class NativeMethods
{
    internal const int WH_MOUSE_LL = 14;
    internal const int WM_MOUSEWHEEL = 0x020A;
    internal const int WM_MOUSEHWHEEL = 0x020E;
    internal const uint MOUSEEVENTF_WHEEL = 0x0800;
    internal const uint LLMHF_INJECTED = 0x01;
    internal const uint INPUT_MOUSE = 0;
    internal const int WHEEL_DELTA = 120;

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        internal POINT pt;
        internal uint mouseData;
        internal uint flags;
        internal uint time;
        internal UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        internal int x;
        internal int y;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUT
    {
        [FieldOffset(0)] internal uint type;
        [FieldOffset(8)] internal MOUSEINPUT mi; // offset 8 on x64 due to alignment
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        internal int dx;
        internal int dy;
        internal int mouseData;
        internal uint dwFlags;
        internal uint time;
        internal UIntPtr dwExtraInfo;
    }

    // Verify: Marshal.SizeOf<INPUT>() should be 40 on x64

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr SetWindowsHookExW(
        int idHook,
        LowLevelMouseProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(
        uint nInputs,
        INPUT[] pInputs,
        int cbSize);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial IntPtr GetModuleHandleW(string? lpModuleName);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr WindowFromPoint(POINT point);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/ScrollBoost/ScrollBoost.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ScrollBoost/Interop/NativeMethods.cs
git commit -m "feat: add Win32 P/Invoke declarations for mouse hook and SendInput"
```

---

## Task 7: MouseHookManager

**Files:**
- Create: `src/ScrollBoost/Hook/MouseHookManager.cs`

This is the core hook logic — suppress original scroll, compute acceleration, inject modified scroll. Not unit-testable (requires Win32 message loop), verified via manual integration testing in Task 10.

- [ ] **Step 1: Implement MouseHookManager**

Create `src/ScrollBoost/Hook/MouseHookManager.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using ScrollBoost.Acceleration;
using ScrollBoost.Interop;
using ScrollBoost.Profiles;

namespace ScrollBoost.Hook;

public class MouseHookManager : IDisposable
{
    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly NativeMethods.LowLevelMouseProc _hookProc;
    private readonly AccelerationEngine _engine;
    private readonly ProfileManager _profileManager;
    private readonly Dictionary<int, string> _pidCache = new();
    private readonly object _pidCacheLock = new();
    private readonly Stopwatch _cacheTimer = Stopwatch.StartNew();
    private bool _isInjecting;
    private System.Threading.Timer? _healthCheckTimer;
    private IAccelerationCurve? _cachedCurve;
    private ScrollProfile? _cachedProfile;

    public bool Enabled { get; set; } = true;

    public MouseHookManager(AccelerationEngine engine, ProfileManager profileManager)
    {
        _engine = engine;
        _profileManager = profileManager;
        // Store delegate to prevent GC collection
        _hookProc = HookCallback;
    }

    public void Install()
    {
        // Uninstall first if re-installing (for health check)
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        IntPtr hModule = NativeMethods.GetModuleHandleW(null);
        _hookHandle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_MOUSE_LL,
            _hookProc,
            hModule,
            0);

        if (_hookHandle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Failed to install mouse hook. Error: {Marshal.GetLastWin32Error()}");

        // Health check timer — unconditionally reinstall every 30s to handle silent removal
        _healthCheckTimer = new System.Threading.Timer(_ => HealthCheck(), null, 30000, 30000);
    }

    public void Uninstall()
    {
        _healthCheckTimer?.Dispose();
        _healthCheckTimer = null;

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Enabled && wParam == (IntPtr)NativeMethods.WM_MOUSEWHEEL)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            // Re-entrancy guard: skip self-injected events
            if (_isInjecting || (hookStruct.flags & NativeMethods.LLMHF_INJECTED) != 0)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            // Extract scroll delta (signed, high word of mouseData)
            int delta = (short)(hookStruct.mouseData >> 16);

            // Resolve target app profile (process lookup deferred to background on cache miss)
            string processName = ResolveProcessName(hookStruct.pt);
            var profile = _profileManager.GetProfile(processName);

            // Cache curve — only rebuild when profile changes
            if (_cachedProfile != profile)
            {
                _cachedCurve = BuildCurve(profile);
                _cachedProfile = profile;
                _engine.SetCurve(_cachedCurve);
            }

            int modifiedDelta = _engine.ProcessScroll(delta, (long)hookStruct.time);

            // Inject modified scroll event
            InjectScroll(modifiedDelta);

            // Suppress original
            return (IntPtr)1;
        }

        // Pass through horizontal scroll and all non-scroll events
        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void InjectScroll(int delta)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            mi = new NativeMethods.MOUSEINPUT
            {
                mouseData = delta,
                dwFlags = NativeMethods.MOUSEEVENTF_WHEEL,
            }
        };

        _isInjecting = true;
        try
        {
            NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
        }
        finally
        {
            _isInjecting = false;
        }
    }

    private string ResolveProcessName(NativeMethods.POINT pt)
    {
        // Clear cache every 2 seconds
        if (_cacheTimer.ElapsedMilliseconds > 2000)
        {
            lock (_pidCacheLock) { _pidCache.Clear(); }
            _cacheTimer.Restart();
        }

        try
        {
            IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return string.Empty;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            int pidInt = (int)pid;

            lock (_pidCacheLock)
            {
                if (_pidCache.TryGetValue(pidInt, out string? cached))
                    return cached;
            }

            // On cache miss: return empty (uses default profile) and resolve in background
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string name = Process.GetProcessById(pidInt).ProcessName;
                    lock (_pidCacheLock)
                    {
                        _pidCache[pidInt] = name;
                    }
                }
                catch { /* process may have exited */ }
            });

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IAccelerationCurve BuildCurve(ScrollProfile profile)
    {
        return profile.CurveType?.ToLowerInvariant() switch
        {
            "linear" => new LinearCurve(profile.BaseMultiplier),
            "power" => new PowerCurve(profile.BaseMultiplier, 2.0, profile.MaxMultiplier),
            "sigmoid" => new SigmoidCurve(
                profile.BaseMultiplier,
                profile.MaxMultiplier,
                midpoint: 15.0,
                steepness: 0.1 + profile.Acceleration * 0.4),
            _ => new SigmoidCurve(profile.BaseMultiplier, profile.MaxMultiplier, 15.0, 0.3)
        };
    }

    private void HealthCheck()
    {
        // Windows silently removes hooks on timeout without zeroing the handle.
        // Unconditionally reinstall to recover from silent removal.
        if (Enabled)
        {
            try { Install(); } catch { /* will retry next tick */ }
        }
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/ScrollBoost/ScrollBoost.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ScrollBoost/Hook/MouseHookManager.cs
git commit -m "feat: add MouseHookManager with suppress-and-reinject pattern"
```

---

## Task 8: Tray UI

**Files:**
- Create: `src/ScrollBoost/UI/SettingsPopup.xaml`
- Create: `src/ScrollBoost/UI/SettingsPopup.xaml.cs`
- Modify: `src/ScrollBoost/App.xaml`
- Create: `src/ScrollBoost/Resources/app.ico`

- [ ] **Step 1: Generate a placeholder icon**

Create a simple `.ico` file. For now, use a placeholder — replace with a real icon later.

```bash
# Create Resources directory
mkdir -p "F:/- Projects -/ClaudeCode/scroll accellerator/src/ScrollBoost/Resources"
```

Create a minimal 16x16 ICO file programmatically, or download one. For the plan, we will create a simple one during implementation. Mark the `.ico` in `.csproj` as a resource:

Add to `src/ScrollBoost/ScrollBoost.csproj` inside `<Project>`:
```xml
<ItemGroup>
    <Resource Include="Resources\app.ico" />
</ItemGroup>
```

- [ ] **Step 2: Create SettingsPopup XAML**

Create `src/ScrollBoost/UI/SettingsPopup.xaml`:

```xml
<Window x:Class="ScrollBoost.UI.SettingsPopup"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ScrollBoost Settings"
        Width="320" Height="380"
        WindowStyle="ToolWindow"
        ShowInTaskbar="False"
        ResizeMode="NoResize"
        Topmost="True"
        WindowStartupLocation="Manual">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Enable/Disable -->
        <CheckBox Grid.Row="0" x:Name="EnabledCheck" Content="Enabled"
                  Margin="0,0,0,12" IsChecked="True"
                  Checked="EnabledCheck_Changed" Unchecked="EnabledCheck_Changed"/>

        <!-- Scroll Speed -->
        <StackPanel Grid.Row="1" Margin="0,0,0,8">
            <TextBlock>
                <Run Text="Scroll Speed: "/><Run x:Name="SpeedLabel" Text="1.5x"/>
            </TextBlock>
            <Slider x:Name="SpeedSlider" Minimum="1" Maximum="5"
                    Value="1.5" TickFrequency="0.1" IsSnapToTickEnabled="True"
                    ValueChanged="Slider_ValueChanged"/>
        </StackPanel>

        <!-- Acceleration -->
        <StackPanel Grid.Row="2" Margin="0,0,0,8">
            <TextBlock>
                <Run Text="Acceleration: "/><Run x:Name="AccelLabel" Text="0.4"/>
            </TextBlock>
            <Slider x:Name="AccelSlider" Minimum="0" Maximum="1"
                    Value="0.4" TickFrequency="0.05" IsSnapToTickEnabled="True"
                    ValueChanged="Slider_ValueChanged"/>
        </StackPanel>

        <!-- Max Speed Cap -->
        <StackPanel Grid.Row="3" Margin="0,0,0,8">
            <TextBlock>
                <Run Text="Max Speed Cap: "/><Run x:Name="MaxLabel" Text="12x"/>
            </TextBlock>
            <Slider x:Name="MaxSlider" Minimum="2" Maximum="30"
                    Value="12" TickFrequency="1" IsSnapToTickEnabled="True"
                    ValueChanged="Slider_ValueChanged"/>
        </StackPanel>

        <!-- Curve Type -->
        <StackPanel Grid.Row="4" Margin="0,0,0,12">
            <TextBlock Text="Curve Type" Margin="0,0,0,4"/>
            <ComboBox x:Name="CurveCombo" SelectedIndex="2"
                      SelectionChanged="CurveCombo_Changed">
                <ComboBoxItem Content="Linear"/>
                <ComboBoxItem Content="Power"/>
                <ComboBoxItem Content="Sigmoid"/>
            </ComboBox>
        </StackPanel>

        <!-- Start with Windows -->
        <CheckBox Grid.Row="5" x:Name="AutoStartCheck" Content="Start with Windows"
                  Checked="AutoStartCheck_Changed" Unchecked="AutoStartCheck_Changed"/>
    </Grid>
</Window>
```

- [ ] **Step 3: Create SettingsPopup code-behind**

Create `src/ScrollBoost/UI/SettingsPopup.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using ScrollBoost.Profiles;

namespace ScrollBoost.UI;

public partial class SettingsPopup : Window
{
    private readonly AppConfig _config;
    private readonly Action<AppConfig> _onConfigChanged;
    private bool _suppressEvents;

    public SettingsPopup(AppConfig config, Action<AppConfig> onConfigChanged)
    {
        InitializeComponent();
        _config = config;
        _onConfigChanged = onConfigChanged;
        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        _suppressEvents = true;
        EnabledCheck.IsChecked = _config.Enabled;
        SpeedSlider.Value = _config.DefaultProfile.BaseMultiplier;
        AccelSlider.Value = _config.DefaultProfile.Acceleration;
        MaxSlider.Value = _config.DefaultProfile.MaxMultiplier;
        CurveCombo.SelectedIndex = _config.DefaultProfile.CurveType?.ToLowerInvariant() switch
        {
            "linear" => 0,
            "power" => 1,
            "sigmoid" => 2,
            _ => 2
        };
        AutoStartCheck.IsChecked = _config.StartWithWindows;
        UpdateLabels();
        _suppressEvents = false;
    }

    private void UpdateLabels()
    {
        SpeedLabel.Text = $"{SpeedSlider.Value:F1}x";
        AccelLabel.Text = $"{AccelSlider.Value:F2}";
        MaxLabel.Text = $"{MaxSlider.Value:F0}x";
    }

    private void SaveToConfig()
    {
        _config.Enabled = EnabledCheck.IsChecked == true;
        _config.DefaultProfile.BaseMultiplier = SpeedSlider.Value;
        _config.DefaultProfile.Acceleration = AccelSlider.Value;
        _config.DefaultProfile.MaxMultiplier = MaxSlider.Value;
        _config.DefaultProfile.CurveType = CurveCombo.SelectedIndex switch
        {
            0 => "linear",
            1 => "power",
            2 => "sigmoid",
            _ => "sigmoid"
        };
        _config.StartWithWindows = AutoStartCheck.IsChecked == true;
        _onConfigChanged(_config);
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents) return;
        UpdateLabels();
        SaveToConfig();
    }

    private void EnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SaveToConfig();
    }

    private void CurveCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        SaveToConfig();
    }

    private void AutoStartCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SaveToConfig();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        Hide();
    }
}
```

- [ ] **Step 4: Verify build**

```bash
dotnet build src/ScrollBoost/ScrollBoost.csproj
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ScrollBoost/UI/ src/ScrollBoost/Resources/
git commit -m "feat: add WPF settings popup with sliders for scroll configuration"
```

---

## Task 9: App Entry Point and Wiring

**Files:**
- Modify: `src/ScrollBoost/App.xaml`
- Rewrite: `src/ScrollBoost/App.xaml.cs`

This task wires everything together: single-instance mutex, config loading, hook installation, tray icon, auto-start registry management.

- [ ] **Step 1: Update App.xaml**

Replace `src/ScrollBoost/App.xaml` — remove the default `StartupUri` so we manage the window lifecycle ourselves:

```xml
<Application x:Class="ScrollBoost.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources/>
</Application>
```

- [ ] **Step 2: Rewrite App.xaml.cs**

Replace `src/ScrollBoost/App.xaml.cs`:

```csharp
using System.IO;
using System.Threading;
using System.Windows;
using H.NotifyIcon;
using Microsoft.Win32;
using ScrollBoost.Acceleration;
using ScrollBoost.Hook;
using ScrollBoost.Profiles;
using ScrollBoost.UI;

namespace ScrollBoost;

public partial class App : Application
{
    private const string MutexName = "Global\\ScrollBoost";
    private const string AutoStartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValue = "ScrollBoost";

    private Mutex? _mutex;
    private TaskbarIcon? _trayIcon;
    private MouseHookManager? _hookManager;
    private AppConfig _config = null!;
    private ProfileManager _profileManager = null!;
    private AccelerationEngine _engine = null!;
    private SettingsPopup? _settingsPopup;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check — silently exit if already running
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        // Load config
        string configPath = GetConfigPath();
        _config = AppConfig.Load(configPath);

        // Save defaults if config didn't exist
        if (!File.Exists(configPath))
            _config.Save(configPath);

        // Initialize components
        _profileManager = new ProfileManager(_config);
        var curve = BuildCurveFromProfile(_config.DefaultProfile);
        _engine = new AccelerationEngine(curve,
            gestureTimeoutMs: _config.GestureTimeoutMs,
            windowSize: _config.VelocityWindowSize,
            smoothingAlpha: _config.SmoothingAlpha);
        _engine.Enabled = _config.Enabled;

        _hookManager = new MouseHookManager(_engine, _profileManager);
        _hookManager.Enabled = _config.Enabled;

        // Install hook
        try
        {
            _hookManager.Install();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to install mouse hook:\n{ex.Message}\n\nTry running as administrator.",
                "ScrollBoost Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Setup tray icon
        SetupTrayIcon();

        // Self-heal auto-start registry (update path if exe moved)
        if (_config.StartWithWindows)
            UpdateAutoStart(true);
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "ScrollBoost",
            ContextMenu = CreateContextMenu()
        };

        // Try to load icon from resources
        try
        {
            var iconUri = new Uri("pack://application:,,,/Resources/app.ico");
            _trayIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(iconUri);
        }
        catch
        {
            // Fallback: no custom icon, will use default
        }

        _trayIcon.TrayLeftMouseUp += (_, _) => ShowSettings();
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var enableItem = new System.Windows.Controls.MenuItem
        {
            Header = _config.Enabled ? "Disable" : "Enable"
        };
        enableItem.Click += (_, _) =>
        {
            _config.Enabled = !_config.Enabled;
            _engine!.Enabled = _config.Enabled;
            _hookManager!.Enabled = _config.Enabled;
            enableItem.Header = _config.Enabled ? "Disable" : "Enable";
            _trayIcon!.ToolTipText = _config.Enabled ? "ScrollBoost" : "ScrollBoost (Disabled)";
            SaveConfig();
        };
        menu.Items.Add(enableItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var configItem = new System.Windows.Controls.MenuItem { Header = "Open Config File" };
        configItem.Click += (_, _) =>
        {
            System.Diagnostics.Process.Start("explorer.exe", GetConfigPath());
        };
        menu.Items.Add(configItem);

        var aboutItem = new System.Windows.Controls.MenuItem { Header = "About" };
        aboutItem.Click += (_, _) =>
        {
            MessageBox.Show("ScrollBoost v0.1.0\nConfigurable scroll acceleration for Windows 11.",
                "About ScrollBoost", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        menu.Items.Add(aboutItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ShowSettings()
    {
        if (_settingsPopup == null || !_settingsPopup.IsLoaded)
        {
            _settingsPopup = new SettingsPopup(_config, OnConfigChanged);
        }

        // Position near tray icon (bottom-right of screen)
        var workArea = SystemParameters.WorkArea;
        _settingsPopup.Left = workArea.Right - _settingsPopup.Width - 10;
        _settingsPopup.Top = workArea.Bottom - _settingsPopup.Height - 10;

        _settingsPopup.Show();
        _settingsPopup.Activate();
    }

    private void OnConfigChanged(AppConfig config)
    {
        _config = config;
        _profileManager.UpdateConfig(config);
        _engine.Enabled = config.Enabled;
        _hookManager!.Enabled = config.Enabled;
        var curve = BuildCurveFromProfile(config.DefaultProfile);
        _engine.SetCurve(curve);
        UpdateAutoStart(config.StartWithWindows);
        SaveConfig();
    }

    private void SaveConfig()
    {
        try { _config.Save(GetConfigPath()); } catch { /* best effort */ }
    }

    private static string GetConfigPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    private static IAccelerationCurve BuildCurveFromProfile(ScrollProfile profile)
    {
        return profile.CurveType?.ToLowerInvariant() switch
        {
            "linear" => new LinearCurve(profile.BaseMultiplier),
            "power" => new PowerCurve(profile.BaseMultiplier, 2.0, profile.MaxMultiplier),
            "sigmoid" => new SigmoidCurve(
                profile.BaseMultiplier,
                profile.MaxMultiplier,
                midpoint: 15.0,
                steepness: 0.1 + profile.Acceleration * 0.4),
            _ => new SigmoidCurve(profile.BaseMultiplier, profile.MaxMultiplier, 15.0, 0.3)
        };
    }

    private void UpdateAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKey, writable: true);
            if (key == null) return;

            if (enabled)
            {
                string exePath = Environment.ProcessPath ?? "";
                key.SetValue(AutoStartValue, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AutoStartValue, throwOnMissingValue: false);
            }
        }
        catch { /* best effort */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hookManager?.Dispose();
        _trayIcon?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 3: Delete the default MainWindow files**

The WPF template creates `MainWindow.xaml` and `MainWindow.xaml.cs` — delete them since we don't use a main window:

```bash
rm "F:/- Projects -/ClaudeCode/scroll accellerator/src/ScrollBoost/MainWindow.xaml"
rm "F:/- Projects -/ClaudeCode/scroll accellerator/src/ScrollBoost/MainWindow.xaml.cs"
```

- [ ] **Step 4: Verify build**

```bash
dotnet build src/ScrollBoost/ScrollBoost.csproj
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: wire app entry point with tray icon, config, hook, and auto-start"
```

---

## Task 10: Build, Run, and Integration Test

- [ ] **Step 1: Run all unit tests**

```bash
dotnet test ScrollBoost.sln -v n
```
Expected: All tests pass.

- [ ] **Step 2: Run the app in debug mode**

```bash
dotnet run --project src/ScrollBoost
```
Expected: Tray icon appears in system tray. Left-click opens settings popup.

- [ ] **Step 3: Manual integration test checklist**

Verify each of these manually:

1. Tray icon visible in system tray
2. Left-click opens settings popup near tray
3. Settings popup has all 4 sliders + 2 checkboxes
4. Moving sliders updates labels in real-time
5. Scroll wheel in a browser — noticeably faster/different than default
6. Scroll in a text editor — same
7. Disable toggle — scroll returns to normal
8. Re-enable — acceleration resumes
9. Close popup by clicking outside — popup hides
10. Right-click tray → Exit — app closes
11. Check `config.json` was created next to the EXE with saved settings

- [ ] **Step 4: Test published build**

```bash
dotnet publish src/ScrollBoost -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Run the published EXE from `src/ScrollBoost/bin/Release/net9.0-windows/win-x64/publish/ScrollBoost.exe` and repeat the manual checklist above.

- [ ] **Step 5: Commit and tag**

```bash
git add -A
git commit -m "chore: verify build and integration testing"
git tag v0.1.0
```

---

## Summary

| Task | Description | Tests |
|------|-------------|-------|
| 1 | Project scaffolding | Build verification |
| 2 | VelocityTracker | 5 unit tests |
| 3 | Acceleration Curves | 8 unit tests |
| 4 | AccelerationEngine | 4 unit tests |
| 5 | Config & Profiles | 7 unit tests |
| 6 | P/Invoke declarations | Build verification |
| 7 | MouseHookManager | Integration (Task 10) |
| 8 | Tray UI (WPF) | Integration (Task 10) |
| 9 | App wiring | Integration (Task 10) |
| 10 | Build & integration test | Manual checklist |
