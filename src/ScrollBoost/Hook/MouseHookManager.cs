using System;
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
    private bool _isInjecting;

    public bool Enabled { get; set; } = true;

    public MouseHookManager(AccelerationEngine engine, ProfileManager profileManager)
    {
        _engine = engine;
        // Store delegate to prevent GC collection
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
    }

    public void Uninstall()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private unsafe IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Fast path: only process vertical scroll events
        if (nCode >= 0 && Enabled && wParam == (IntPtr)NativeMethods.WM_MOUSEWHEEL)
        {
            // Read struct directly via pointer — faster than Marshal.PtrToStructure
            var hookStruct = (NativeMethods.MSLLHOOKSTRUCT*)lParam;

            // Skip self-injected events to prevent infinite loop
            if (_isInjecting || (hookStruct->flags & NativeMethods.LLMHF_INJECTED) != 0)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            // Extract signed scroll delta from high word
            int delta = (short)(hookStruct->mouseData >> 16);

            // Apply acceleration
            int modifiedDelta = _engine.ProcessScroll(delta, (long)hookStruct->time);

            // Inject modified scroll and suppress original
            _isInjecting = true;
            NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, modifiedDelta, UIntPtr.Zero);
            _isInjecting = false;

            return (IntPtr)1; // Suppress original event
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
