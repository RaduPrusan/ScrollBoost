#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using ScrollBoost.Acceleration;
using ScrollBoost.Hook;
using ScrollBoost.Profiles;
using ScrollBoost.UI;
using WinForms = System.Windows.Forms;

namespace ScrollBoost;

public partial class App : Application
{
    private const string MutexName = "Global\\ScrollBoost";
    private const string AutoStartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValue = "ScrollBoost";

    private Mutex? _mutex;
    private WinForms.NotifyIcon? _trayIcon;
    private MouseHookManager? _hookManager;
    private AppConfig _config = null!;
    private ProfileManager _profileManager = null!;
    private AccelerationEngine _engine = null!;
    private SettingsPopup? _settingsPopup;
    private HotkeyForm? _hotkeyForm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        string configPath = GetConfigPath();
        _config = AppConfig.Load(configPath);

        if (!File.Exists(configPath))
            _config.Save(configPath);

        _profileManager = new ProfileManager(_config);
        var curve = BuildCurveFromProfile(_config.DefaultProfile);
        _engine = new AccelerationEngine(curve,
            gestureTimeoutMs: _config.GestureTimeoutMs,
            windowSize: _config.VelocityWindowSize,
            smoothingAlpha: _config.SmoothingAlpha);
        _engine.Enabled = _config.Enabled;

        _hookManager = new MouseHookManager(_engine, _profileManager);
        _hookManager.Enabled = _config.Enabled;

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

        SetupTrayIcon();
        SetupGlobalHotkey();

        if (_config.StartWithWindows)
            UpdateAutoStart(true);
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "ScrollBoost",
            Icon = TrayIconHelper.CreateIcon(),
            Visible = true
        };

        // Build context menu
        var menu = new WinForms.ContextMenuStrip();

        var enableItem = new WinForms.ToolStripMenuItem(_config.Enabled ? "Disable" : "Enable");
        enableItem.Click += (_, _) =>
        {
            _config.Enabled = !_config.Enabled;
            _engine!.Enabled = _config.Enabled;
            _hookManager!.Enabled = _config.Enabled;
            enableItem.Text = _config.Enabled ? "Disable" : "Enable";
            _trayIcon!.Text = _config.Enabled ? "ScrollBoost" : "ScrollBoost (Disabled)";
            SaveConfig();
        };
        menu.Items.Add(enableItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var configItem = new WinForms.ToolStripMenuItem("Open Config File");
        configItem.Click += (_, _) =>
        {
            System.Diagnostics.Process.Start("explorer.exe", GetConfigPath());
        };
        menu.Items.Add(configItem);

        var aboutItem = new WinForms.ToolStripMenuItem("About");
        aboutItem.Click += (_, _) =>
        {
            MessageBox.Show("ScrollBoost v0.1.0\nConfigurable scroll acceleration for Windows 11.",
                "About ScrollBoost", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        menu.Items.Add(aboutItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left)
                ShowSettings();
        };
    }

    private void SetupGlobalHotkey()
    {
        _hotkeyForm = new HotkeyForm();
        _hotkeyForm.HotkeyPressed += () =>
        {
            _config.Enabled = !_config.Enabled;
            _engine!.Enabled = _config.Enabled;
            _hookManager!.Enabled = _config.Enabled;
            _trayIcon!.Text = _config.Enabled ? "ScrollBoost" : "ScrollBoost (Disabled)";
            SaveConfig();
        };
        _hotkeyForm.RegisterToggleHotkey(); // Best effort — may fail if key combo is taken
    }

    private void ShowSettings()
    {
        if (_settingsPopup == null || !_settingsPopup.IsLoaded)
        {
            _settingsPopup = new SettingsPopup(_config, OnConfigChanged);
        }

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
        _hotkeyForm?.UnregisterToggleHotkey();
        _hotkeyForm?.Dispose();
        _hookManager?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
