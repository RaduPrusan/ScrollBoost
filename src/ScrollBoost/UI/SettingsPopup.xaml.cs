#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ScrollBoost.Hook;
using ScrollBoost.Interop;
using ScrollBoost.Profiles;

namespace ScrollBoost.UI;

public partial class SettingsPopup : Window
{
    private readonly AppConfig _config;
    private readonly Action<AppConfig> _onConfigChanged;
    private readonly MouseHookManager _hookManager;
    private readonly DispatcherTimer _counterTimer;
    private bool _suppressEvents = true;
    private bool _advancedExpanded;

    // Theme brushes cached for rule row creation
    private SolidColorBrush _textBrush = Brushes.White;
    private SolidColorBrush _secondaryBrush = Brushes.Gray;
    private SolidColorBrush _borderBrush = Brushes.DarkGray;
    private SolidColorBrush _surfaceBrush = new(Color.FromRgb(0x2D, 0x2D, 0x2D));

    public SettingsPopup(AppConfig config, Action<AppConfig> onConfigChanged, MouseHookManager hookManager)
    {
        _config = config;
        _onConfigChanged = onConfigChanged;
        _hookManager = hookManager;
        InitializeComponent();
        ApplyTheme();
        LoadFromConfig();
        RebuildRuleRows();
        UpdateScrollCounter();

        // Live counter update every second while popup is visible
        _counterTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _counterTimer.Tick += (_, _) => UpdateScrollCounter();
        _counterTimer.Start();
    }

    private void UpdateScrollCounter()
    {
        ScrollCountText.Text = _hookManager.ScrollCount.ToString("N0");
    }

    private void ApplyTheme()
    {
        bool isLight = TrayIconHelper.IsLightTheme();

        Color bgColor        = isLight ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20);
        Color textColor      = isLight ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Color.FromRgb(0xFF, 0xFF, 0xFF);
        Color secondaryColor = isLight ? Color.FromRgb(0x66, 0x66, 0x66) : Color.FromRgb(0x99, 0x99, 0x99);
        Color accentColor    = isLight ? Color.FromRgb(0x00, 0x5F, 0xB8) : Color.FromRgb(0x60, 0xCD, 0xFF);
        Color trackColor     = isLight ? Color.FromRgb(0xD0, 0xD0, 0xD0) : Color.FromRgb(0x40, 0x40, 0x40);
        Color borderColor    = isLight ? Color.FromRgb(0xE0, 0xE0, 0xE0) : Color.FromRgb(0x38, 0x38, 0x38);
        Color surfaceColor   = isLight ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x2D, 0x2D, 0x2D);

        _textBrush      = new SolidColorBrush(textColor);
        _secondaryBrush = new SolidColorBrush(secondaryColor);
        _borderBrush    = new SolidColorBrush(borderColor);
        _surfaceBrush   = new SolidColorBrush(surfaceColor);
        var bgBrush     = new SolidColorBrush(bgColor);
        var accentBrush = new SolidColorBrush(accentColor);

        Resources["AccentBrush"] = accentBrush;
        Resources["TrackBrush"]  = new SolidColorBrush(trackColor);

        RootBorder.Background  = bgBrush;
        RootBorder.BorderBrush = _borderBrush;
        TitleSeparator.Fill    = _borderBrush;
        TitleText.Foreground   = _textBrush;
        CloseButton.Foreground = _secondaryBrush;

        EnabledLabel.Foreground = _textBrush;
        SpeedName.Foreground    = _textBrush;
        AccelName.Foreground    = _textBrush;
        MaxName.Foreground      = _textBrush;
        CurveLabel.Foreground   = _textBrush;
        SpeedLabel.Foreground   = accentBrush;
        AccelLabel.Foreground   = accentBrush;
        MaxLabel.Foreground     = accentBrush;

        EnabledDesc.Foreground          = _secondaryBrush;
        SpeedDesc.Foreground            = _secondaryBrush;
        AccelDesc.Foreground            = _secondaryBrush;
        MaxDesc.Foreground              = _secondaryBrush;
        CurveDesc.Foreground            = _secondaryBrush;
        StartupHeader.Foreground        = _secondaryBrush;
        StartupRegistryDesc.Foreground  = _secondaryBrush;
        StartupSchedulerDesc.Foreground = _secondaryBrush;
        StartupDivider.Fill             = _borderBrush;

        CurveCombo.Foreground  = _textBrush;
        VersionLabel.Foreground = _secondaryBrush;
        AdvancedDesc.Foreground = _secondaryBrush;

        // Advanced panel inputs
        NewClassName.Background = _surfaceBrush;
        NewClassName.Foreground = _textBrush;
        NewClassName.BorderBrush = _borderBrush;
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
            "linear"  => 0,
            "power"   => 1,
            "sigmoid" => 2,
            _         => 2
        };
        switch (_config.StartupMode?.ToLowerInvariant())
        {
            case "registry":  StartupRegistry.IsChecked  = true; break;
            case "scheduler": StartupScheduler.IsChecked = true; break;
            default:          StartupNone.IsChecked       = true; break;
        }
        UpdateLabels();
        _suppressEvents = false;
    }

    private void RebuildRuleRows()
    {
        RulesPanel.Children.Clear();
        foreach (var (className, method) in _config.WindowClassRules)
        {
            AddRuleRow(className, method);
        }
    }

    private void AddRuleRow(string className, string method)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var label = new TextBlock
        {
            Text = className,
            Foreground = _textBrush,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = className
        };
        Grid.SetColumn(label, 0);

        var combo = new ComboBox
        {
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 11,
            Height = 24,
            Margin = new Thickness(4, 0, 4, 0),
            Tag = className
        };
        combo.Items.Add(new ComboBoxItem { Content = "PostMessage" });
        combo.Items.Add(new ComboBoxItem { Content = "SendInput" });
        combo.Items.Add(new ComboBoxItem { Content = "Passthrough" });
        combo.SelectedIndex = method?.ToLowerInvariant() switch
        {
            "sendinput" => 1,
            "passthrough" => 2,
            _ => 0
        };
        combo.SelectionChanged += RuleCombo_Changed;
        Grid.SetColumn(combo, 1);

        var deleteBtn = new Button
        {
            Content = "\u2715",
            Width = 24, Height = 24,
            FontSize = 10,
            Cursor = Cursors.Hand,
            Foreground = _secondaryBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Tag = className
        };
        deleteBtn.Click += DeleteRule_Click;
        Grid.SetColumn(deleteBtn, 2);

        grid.Children.Add(label);
        grid.Children.Add(combo);
        grid.Children.Add(deleteBtn);
        RulesPanel.Children.Add(grid);
    }

    private void RuleCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (sender is ComboBox combo && combo.Tag is string className)
        {
            string method = combo.SelectedIndex switch
            {
                1 => "sendinput",
                2 => "passthrough",
                _ => "postmessage"
            };
            _config.WindowClassRules[className] = method;
            _onConfigChanged(_config);
        }
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string className)
        {
            _config.WindowClassRules.Remove(className);
            RebuildRuleRows();
            _onConfigChanged(_config);
        }
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        string className = NewClassName.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(className)) return;
        if (_config.WindowClassRules.ContainsKey(className)) return;

        string method = NewClassMethod.SelectedIndex switch
        {
            1 => "sendinput",
            2 => "passthrough",
            _ => "postmessage"
        };

        _config.WindowClassRules[className] = method;
        AddRuleRow(className, method);
        NewClassName.Text = "";
        _onConfigChanged(_config);
    }

    // --- Window class picker (drag crosshair over any window) ---

    private bool _picking;

    private void Picker_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _picking = true;
        Mouse.Capture(PickerTarget);
        PickerTarget.BorderBrush = (SolidColorBrush)Resources["AccentBrush"];
    }

    private void Picker_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_picking) return;

        NativeMethods.GetCursorPos(out var pt);
        IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return;

        // Skip our own window
        var thisHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == thisHandle) return;

        var buf = new char[256];
        int len = NativeMethods.GetClassNameW(hwnd, buf, buf.Length);
        if (len > 0)
        {
            NewClassName.Text = new string(buf, 0, len);
        }
    }

    private void Picker_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _picking = false;
        Mouse.Capture(null);
        PickerTarget.BorderBrush = _borderBrush;
    }

    private void GitHubLink_Click(object sender, MouseButtonEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://github.com/RaduPrusan/ScrollBoost",
            UseShellExecute = true
        });
    }

    private void AdvancedToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _advancedExpanded = !_advancedExpanded;
        double bottomEdge = Top + ActualHeight;

        AdvancedPanel.Visibility = _advancedExpanded ? Visibility.Visible : Visibility.Collapsed;
        AdvancedArrow.Text = _advancedExpanded ? "\u25BC " : "\u25B6 ";

        // After layout updates, keep the bottom edge pinned
        Dispatcher.InvokeAsync(() =>
        {
            Top = bottomEdge - ActualHeight;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateLabels()
    {
        SpeedLabel.Text = $"{SpeedSlider.Value:F1}x";
        AccelLabel.Text = $"{AccelSlider.Value:F2}";
        MaxLabel.Text   = $"{MaxSlider.Value:F0}x";
    }

    private void SaveToConfig()
    {
        _config.Enabled = EnabledCheck.IsChecked == true;
        _config.DefaultProfile.BaseMultiplier = SpeedSlider.Value;
        _config.DefaultProfile.Acceleration   = AccelSlider.Value;
        _config.DefaultProfile.MaxMultiplier  = MaxSlider.Value;
        _config.DefaultProfile.CurveType = CurveCombo.SelectedIndex switch
        {
            0 => "linear",
            1 => "power",
            2 => "sigmoid",
            _ => "sigmoid"
        };
        if (StartupScheduler.IsChecked == true)
            _config.StartupMode = "scheduler";
        else if (StartupRegistry.IsChecked == true)
            _config.StartupMode = "registry";
        else
            _config.StartupMode = "none";

        _onConfigChanged(_config);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

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

    private void Startup_Changed(object sender, RoutedEventArgs e)
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
