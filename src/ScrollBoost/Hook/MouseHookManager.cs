#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Threading;
using ScrollBoost.Acceleration;
using ScrollBoost.Interop;

namespace ScrollBoost.Hook;

public class MouseHookManager : IDisposable
{
    private const uint WM_APP_REHOOK = 0x8001; // WM_APP + 1

    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly NativeMethods.LowLevelMouseProc _hookProc;
    private readonly AccelerationEngine _engine;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private Timer? _healthTimer;
    private volatile bool _enabled = true;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public MouseHookManager(AccelerationEngine engine)
    {
        _engine = engine;
        _hookProc = HookCallback;
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

                // Lightweight message pump
                while (NativeMethods.GetMessageW(out var msg, IntPtr.Zero, 0, 0))
                {
                    if (msg.message == WM_APP_REHOOK)
                    {
                        // Reinstall hook on this thread (safe — we're on the hook thread)
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

                // Message loop exited (WM_QUIT received) — clean up hook on this thread
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

        // Health check: periodically ask the hook thread to reinstall
        _healthTimer = new Timer(_ =>
        {
            if (_hookThreadId != 0)
                NativeMethods.PostThreadMessageW(_hookThreadId, WM_APP_REHOOK, UIntPtr.Zero, IntPtr.Zero);
        }, null, 60000, 60000);
    }

    public void Uninstall()
    {
        // Stop health timer first and wait for any in-flight callback
        if (_healthTimer != null)
        {
            var timerDone = new ManualResetEventSlim(false);
            _healthTimer.Dispose(timerDone.WaitHandle);
            timerDone.Wait(2000);
            timerDone.Dispose();
            _healthTimer = null;
        }

        // Post WM_QUIT — the hook thread will unhook and exit cleanly
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

            // Skip injected events from other tools
            if ((hookStruct->flags & NativeMethods.LLMHF_INJECTED) != 0)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            int delta = (short)(hookStruct->mouseData >> 16);
            int modifiedDelta = _engine.ProcessScroll(delta, (long)hookStruct->time);

            IntPtr targetHwnd = NativeMethods.WindowFromPoint(hookStruct->pt);
            if (targetHwnd != IntPtr.Zero)
            {
                uint modifiers = 0;
                if ((NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0) modifiers |= (uint)NativeMethods.MK_CONTROL;
                if ((NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0) modifiers |= (uint)NativeMethods.MK_SHIFT;
                if ((NativeMethods.GetKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0) modifiers |= (uint)NativeMethods.MK_LBUTTON;
                if ((NativeMethods.GetKeyState(NativeMethods.VK_MBUTTON) & 0x8000) != 0) modifiers |= (uint)NativeMethods.MK_MBUTTON;
                if ((NativeMethods.GetKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0) modifiers |= (uint)NativeMethods.MK_RBUTTON;
                uint wp = (uint)(((ushort)(short)modifiedDelta) << 16) | modifiers;

                // MAKELPARAM: pack two signed 16-bit values (safe for negative multi-monitor coords)
                IntPtr lp = (IntPtr)((int)((ushort)(short)hookStruct->pt.y << 16) | (ushort)(short)hookStruct->pt.x);

                NativeMethods.PostMessageW(targetHwnd, NativeMethods.WM_MOUSEWHEEL,
                    (UIntPtr)wp, lp);
            }

            return (IntPtr)1;
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
