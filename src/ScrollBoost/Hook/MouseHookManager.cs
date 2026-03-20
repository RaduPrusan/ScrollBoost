using System;
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
    private Thread? _hookThread;
    private uint _hookThreadId;

    public bool Enabled { get; set; } = true;

    public MouseHookManager(AccelerationEngine engine, ProfileManager profileManager)
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
                    NativeMethods.TranslateMessage(in msg);
                    NativeMethods.DispatchMessageW(in msg);
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
    }

    public void Uninstall()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
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
        if (nCode >= 0 && Enabled && wParam == (IntPtr)NativeMethods.WM_MOUSEWHEEL)
        {
            var hookStruct = (NativeMethods.MSLLHOOKSTRUCT*)lParam;

            // Skip injected events from other tools
            if ((hookStruct->flags & NativeMethods.LLMHF_INJECTED) != 0)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            int delta = (short)(hookStruct->mouseData >> 16);
            int modifiedDelta = _engine.ProcessScroll(delta, (long)hookStruct->time);

            // Send WM_MOUSEWHEEL directly to the target window — bypasses hook chain entirely
            IntPtr targetHwnd = NativeMethods.WindowFromPoint(hookStruct->pt);
            if (targetHwnd != IntPtr.Zero)
            {
                // Walk to the root/top-level window — WM_MOUSEWHEEL is sent to the focus window
                // but we use the window under the cursor (Windows 10+ behavior)
                // Build modifier key flags for low word of wParam
                uint modifiers = 0;
                if ((NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0) modifiers |= (uint)NativeMethods.MK_CONTROL;
                if ((NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0) modifiers |= (uint)NativeMethods.MK_SHIFT;
                if ((NativeMethods.GetKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0) modifiers |= (uint)NativeMethods.MK_LBUTTON;
                if ((NativeMethods.GetKeyState(NativeMethods.VK_MBUTTON) & 0x8000) != 0) modifiers |= (uint)NativeMethods.MK_MBUTTON;
                if ((NativeMethods.GetKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0) modifiers |= (uint)NativeMethods.MK_RBUTTON;
                uint wp = (uint)(((ushort)(short)modifiedDelta) << 16) | modifiers;
                // lParam = cursor position in screen coords (MAKELPARAM(x, y))
                IntPtr lp = (IntPtr)((hookStruct->pt.y << 16) | (hookStruct->pt.x & 0xFFFF));

                NativeMethods.PostMessageW(targetHwnd, NativeMethods.WM_MOUSEWHEEL,
                    (UIntPtr)wp, lp);
            }

            return (IntPtr)1; // Suppress original
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
