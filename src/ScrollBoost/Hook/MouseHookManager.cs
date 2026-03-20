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
    private bool _isInjecting;

    public bool Enabled { get; set; } = true;

    public MouseHookManager(AccelerationEngine engine, ProfileManager profileManager)
    {
        _engine = engine;
        // Store delegate as field to prevent GC collection
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

                // Lightweight message pump — this is ALL this thread does
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

        // Wait for hook to be installed
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
            // Post WM_QUIT to break the message loop
            NativeMethods.PostThreadMessageW(_hookThreadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            _hookThread.Join(2000);
            _hookThread = null;
            _hookThreadId = 0;
        }
    }

    private unsafe IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Fast path: only intercept vertical scroll
        if (nCode >= 0 && Enabled && wParam == (IntPtr)NativeMethods.WM_MOUSEWHEEL)
        {
            var hookStruct = (NativeMethods.MSLLHOOKSTRUCT*)lParam;

            // Skip self-injected events
            if (_isInjecting || (hookStruct->flags & NativeMethods.LLMHF_INJECTED) != 0)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            int delta = (short)(hookStruct->mouseData >> 16);
            int modifiedDelta = _engine.ProcessScroll(delta, (long)hookStruct->time);

            // Inject and suppress
            _isInjecting = true;
            NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, modifiedDelta, UIntPtr.Zero);
            _isInjecting = false;

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
