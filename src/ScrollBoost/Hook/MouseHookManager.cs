#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using ScrollBoost.Acceleration;
using ScrollBoost.Interop;

namespace ScrollBoost.Hook;

public enum ScrollMethod { PostMessage, SendInput, Passthrough }

public class MouseHookManager : IDisposable
{
    private const uint WM_APP_REHOOK = 0x8001;

    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly NativeMethods.LowLevelMouseProc _hookProc;
    private readonly AccelerationEngine _engine;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private Timer? _healthTimer;
    private volatile bool _enabled = true;
    private volatile bool _isInjecting;

    // Window class → scroll method rules (case-insensitive)
    private volatile Dictionary<string, ScrollMethod> _classRules = new(StringComparer.OrdinalIgnoreCase);

    // Cache: avoid GetClassNameW on every scroll for the same window
    private IntPtr _cachedHwnd;
    private ScrollMethod _cachedMethod;

    // Counter: total accelerated scroll operations (gestures, not ticks)
    private long _scrollCount;
    private long _lastScrollTime;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public long ScrollCount => Interlocked.Read(ref _scrollCount);
    public void SetScrollCount(long value) => Interlocked.Exchange(ref _scrollCount, value);

    public MouseHookManager(AccelerationEngine engine)
    {
        _engine = engine;
        _hookProc = HookCallback;
    }

    public void UpdateClassRules(Dictionary<string, string> rules)
    {
        var parsed = new Dictionary<string, ScrollMethod>(StringComparer.OrdinalIgnoreCase);
        foreach (var (className, mode) in rules)
        {
            parsed[className] = mode?.ToLowerInvariant() switch
            {
                "sendinput" => ScrollMethod.SendInput,
                "passthrough" => ScrollMethod.Passthrough,
                _ => ScrollMethod.PostMessage
            };
        }
        _classRules = parsed;
        // Invalidate cache
        _cachedHwnd = IntPtr.Zero;
    }

    public void Install()
    {
        if (_hookThread != null) return;

        var readyEvent = new ManualResetEventSlim(false);
        Exception? threadError = null;

        _hookThread = new Thread(() =>
        {
            try
            {
                _hookThreadId = NativeMethods.GetCurrentThreadId();

                IntPtr hModule = NativeMethods.GetModuleHandleW(null);
                _hookHandle = NativeMethods.SetWindowsHookExW(
                    NativeMethods.WH_MOUSE_LL,
                    _hookProc,
                    hModule,
                    0);

                if (_hookHandle == IntPtr.Zero)
                {
                    threadError = new InvalidOperationException(
                        $"Failed to install mouse hook. Error: {Marshal.GetLastWin32Error()}");
                    readyEvent.Set();
                    return;
                }

                readyEvent.Set();

                while (NativeMethods.GetMessageW(out var msg, IntPtr.Zero, 0, 0))
                {
                    if (msg.message == WM_APP_REHOOK)
                    {
                        if (_hookHandle != IntPtr.Zero)
                            NativeMethods.UnhookWindowsHookEx(_hookHandle);

                        IntPtr hMod = NativeMethods.GetModuleHandleW(null);
                        _hookHandle = NativeMethods.SetWindowsHookExW(
                            NativeMethods.WH_MOUSE_LL, _hookProc, hMod, 0);
                        continue;
                    }
                    NativeMethods.TranslateMessage(in msg);
                    NativeMethods.DispatchMessageW(in msg);
                }

                if (_hookHandle != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(_hookHandle);
                    _hookHandle = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                threadError = ex;
                readyEvent.Set();
            }
        });

        _hookThread.IsBackground = true;
        _hookThread.Name = "ScrollBoost Hook";
        _hookThread.Start();

        readyEvent.Wait(5000);
        readyEvent.Dispose();

        if (threadError != null)
            throw threadError;

        _healthTimer = new Timer(_ =>
        {
            if (_hookThreadId != 0)
                NativeMethods.PostThreadMessageW(_hookThreadId, WM_APP_REHOOK, UIntPtr.Zero, IntPtr.Zero);
        }, null, 60000, 60000);
    }

    public void Uninstall()
    {
        if (_healthTimer != null)
        {
            var timerDone = new ManualResetEventSlim(false);
            _healthTimer.Dispose(timerDone.WaitHandle);
            timerDone.Wait(2000);
            timerDone.Dispose();
            _healthTimer = null;
        }

        if (_hookThread != null && _hookThreadId != 0)
        {
            NativeMethods.PostThreadMessageW(_hookThreadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            _hookThread.Join(2000);
            _hookThread = null;
            _hookThreadId = 0;
        }
    }

    private unsafe IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _enabled && wParam == (IntPtr)NativeMethods.WM_MOUSEWHEEL)
        {
            var hookStruct = (NativeMethods.MSLLHOOKSTRUCT*)lParam;

            // Skip our own injected events (SendInput path)
            if (_isInjecting || (hookStruct->flags & NativeMethods.LLMHF_INJECTED) != 0)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            IntPtr targetHwnd = NativeMethods.WindowFromPoint(hookStruct->pt);
            if (targetHwnd == IntPtr.Zero)
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            ScrollMethod method = ResolveMethod(targetHwnd);

            // Passthrough: don't touch this window's scroll at all
            if (method == ScrollMethod.Passthrough)
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            int delta = (short)(hookStruct->mouseData >> 16);
            int modifiedDelta = _engine.ProcessScroll(delta, (long)hookStruct->time);

            if (method == ScrollMethod.SendInput)
            {
                _isInjecting = true;
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL,
                    0, 0, modifiedDelta, UIntPtr.Zero);
                _isInjecting = false;
            }
            else // PostMessage
            {
                uint modifiers = GetModifierKeys();
                uint wp = (uint)(((ushort)(short)modifiedDelta) << 16) | modifiers;
                IntPtr lp = (IntPtr)((int)((ushort)(short)hookStruct->pt.y << 16)
                    | (ushort)(short)hookStruct->pt.x);

                NativeMethods.PostMessageW(targetHwnd, NativeMethods.WM_MOUSEWHEEL,
                    (UIntPtr)wp, lp);
            }

            // Count scroll operations (new gesture if 250ms+ gap)
            long now = (long)hookStruct->time;
            long prev = Interlocked.Exchange(ref _lastScrollTime, now);
            if (now - prev > 250)
                Interlocked.Increment(ref _scrollCount);

            return (IntPtr)1; // Suppress original
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private ScrollMethod ResolveMethod(IntPtr hwnd)
    {
        if (hwnd == _cachedHwnd)
            return _cachedMethod;

        _cachedHwnd = hwnd;
        _cachedMethod = ScrollMethod.PostMessage; // default

        var buf = new char[256];
        int len = NativeMethods.GetClassNameW(hwnd, buf, buf.Length);
        if (len > 0)
        {
            var className = new string(buf, 0, len);
            var rules = _classRules;
            if (rules.TryGetValue(className, out var method))
                _cachedMethod = method;
        }

        return _cachedMethod;
    }

    private static uint GetModifierKeys()
    {
        uint m = 0;
        if ((NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0) m |= (uint)NativeMethods.MK_CONTROL;
        if ((NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0) m |= (uint)NativeMethods.MK_SHIFT;
        if ((NativeMethods.GetKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0) m |= (uint)NativeMethods.MK_LBUTTON;
        if ((NativeMethods.GetKeyState(NativeMethods.VK_MBUTTON) & 0x8000) != 0) m |= (uint)NativeMethods.MK_MBUTTON;
        if ((NativeMethods.GetKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0) m |= (uint)NativeMethods.MK_RBUTTON;
        return m;
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
