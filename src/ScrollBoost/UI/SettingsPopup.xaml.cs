#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ScrollBoost.Profiles;

namespace ScrollBoost.UI;

public partial class SettingsPopup : Window
{
    private readonly AppConfig _config;
    private readonly Action<AppConfig> _onConfigChanged;
    private bool _suppressEvents = true;

    public SettingsPopup(AppConfig config, Action<AppConfig> onConfigChanged)
    {
        _config = config;
        _onConfigChanged = onConfigChanged;
        InitializeComponent();
        ApplyTheme();
        LoadFromConfig();
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

        var bgBrush        = new SolidColorBrush(bgColor);
        var textBrush      = new SolidColorBrush(textColor);
        var secondaryBrush = new SolidColorBrush(secondaryColor);
        var accentBrush    = new SolidColorBrush(accentColor);
        var borderBrush    = new SolidColorBrush(borderColor);

        // Update DynamicResource brushes for slider templates
        Resources["AccentBrush"] = accentBrush;
        Resources["TrackBrush"]  = new SolidColorBrush(trackColor);

        // Window chrome
        RootBorder.Background  = bgBrush;
        RootBorder.BorderBrush = borderBrush;
        TitleSeparator.Fill    = borderBrush;

        // Title bar
        TitleText.Foreground   = textBrush;
        CloseButton.Foreground = secondaryBrush;

        // Primary labels
        EnabledLabel.Foreground = textBrush;
        SpeedName.Foreground    = textBrush;
        AccelName.Foreground    = textBrush;
        MaxName.Foreground      = textBrush;
        CurveLabel.Foreground   = textBrush;

        // Value labels (accent)
        SpeedLabel.Foreground = accentBrush;
        AccelLabel.Foreground = accentBrush;
        MaxLabel.Foreground   = accentBrush;

        // Description text (secondary)
        EnabledDesc.Foreground          = secondaryBrush;
        SpeedDesc.Foreground            = secondaryBrush;
        AccelDesc.Foreground            = secondaryBrush;
        MaxDesc.Foreground              = secondaryBrush;
        CurveDesc.Foreground            = secondaryBrush;
        StartupHeader.Foreground        = secondaryBrush;
        StartupRegistryDesc.Foreground  = secondaryBrush;
        StartupSchedulerDesc.Foreground = secondaryBrush;

        // Startup divider line
        StartupDivider.Fill = borderBrush;

        // ComboBox
        CurveCombo.Foreground = textBrush;

        // Version label
        VersionLabel.Foreground = secondaryBrush;
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

    private void AdvancedLink_Click(object sender, MouseButtonEventArgs e)
    {
        string configPath = System.IO.Path.Combine(AppContext.BaseDirectory, "config.json");
        System.Diagnostics.Process.Start("notepad.exe", configPath);
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
