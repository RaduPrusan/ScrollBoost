using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
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
    private Timer? _healthCheckTimer;
    private System.Windows.Threading.Dispatcher? _uiDispatcher;
    private IAccelerationCurve? _cachedCurve;
    private ScrollProfile? _cachedProfile;

    public bool Enabled { get; set; } = true;

    public MouseHookManager(AccelerationEngine engine, ProfileManager profileManager)
    {
        _engine = engine;
        _profileManager = profileManager;
        _hookProc = HookCallback;
    }

    public void Install()
    {
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

        // NOTE: Health check must run on the UI thread (the hook thread) because
        // SetWindowsHookExW requires the installing thread to pump a message loop.
        // We store a reference to the dispatcher and use it for reinstallation.
        _uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _healthCheckTimer = new Timer(_ => HealthCheck(), null, 30000, 30000);
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

            if (_isInjecting || (hookStruct.flags & NativeMethods.LLMHF_INJECTED) != 0)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            int delta = (short)(hookStruct.mouseData >> 16);

            string processName = ResolveProcessName(hookStruct.pt);
            var profile = _profileManager.GetProfile(processName);

            if (_cachedProfile != profile)
            {
                _cachedCurve = BuildCurve(profile);
                _cachedProfile = profile;
                _engine.SetCurve(_cachedCurve);
            }

            int modifiedDelta = _engine.ProcessScroll(delta, (long)hookStruct.time);

            InjectScroll(modifiedDelta);

            return (IntPtr)1;
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void InjectScroll(int delta)
    {
        _isInjecting = true;
        try
        {
            // Use mouse_event (legacy but reliable) — avoids INPUT struct layout issues on x64
            NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, delta, UIntPtr.Zero);
        }
        finally
        {
            _isInjecting = false;
        }
    }

    private string ResolveProcessName(NativeMethods.POINT pt)
    {
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
        if (Enabled && _uiDispatcher != null)
        {
            // Must reinstall on the UI thread — WH_MOUSE_LL requires a message pump
            _uiDispatcher.BeginInvoke(() =>
            {
                try { Install(); } catch { /* will retry next tick */ }
            });
        }
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
