using System;
using WinForms = System.Windows.Forms;
using ScrollBoost.Interop;

namespace ScrollBoost.UI;

public class HotkeyForm : WinForms.Form
{
    private const int HOTKEY_ID = 1;
    public event Action? HotkeyPressed;

    public HotkeyForm()
    {
        ShowInTaskbar = false;
        WindowState = WinForms.FormWindowState.Minimized;
        FormBorderStyle = WinForms.FormBorderStyle.None;
        Opacity = 0;
        Show();
        Hide();
    }

    public bool RegisterToggleHotkey()
    {
        return NativeMethods.RegisterHotKey(
            Handle,
            HOTKEY_ID,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_SCROLL);
    }

    public void UnregisterToggleHotkey()
    {
        NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID);
    }

    protected override void WndProc(ref WinForms.Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
        }
        base.WndProc(ref m);
    }
}
