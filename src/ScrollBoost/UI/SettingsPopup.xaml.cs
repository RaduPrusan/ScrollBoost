#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using ScrollBoost.Profiles;

namespace ScrollBoost.UI;

public partial class SettingsPopup : Window
{
    private readonly AppConfig _config;
    private readonly Action<AppConfig> _onConfigChanged;
    private bool _suppressEvents;

    public SettingsPopup(AppConfig config, Action<AppConfig> onConfigChanged)
    {
        InitializeComponent();
        _config = config;
        _onConfigChanged = onConfigChanged;
        LoadFromConfig();
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
            "linear" => 0,
            "power" => 1,
            "sigmoid" => 2,
            _ => 2
        };
        AutoStartCheck.IsChecked = _config.StartWithWindows;
        UpdateLabels();
        _suppressEvents = false;
    }

    private void UpdateLabels()
    {
        SpeedLabel.Text = $"{SpeedSlider.Value:F1}x";
        AccelLabel.Text = $"{AccelSlider.Value:F2}";
        MaxLabel.Text = $"{MaxSlider.Value:F0}x";
    }

    private void SaveToConfig()
    {
        _config.Enabled = EnabledCheck.IsChecked == true;
        _config.DefaultProfile.BaseMultiplier = SpeedSlider.Value;
        _config.DefaultProfile.Acceleration = AccelSlider.Value;
        _config.DefaultProfile.MaxMultiplier = MaxSlider.Value;
        _config.DefaultProfile.CurveType = CurveCombo.SelectedIndex switch
        {
            0 => "linear",
            1 => "power",
            2 => "sigmoid",
            _ => "sigmoid"
        };
        _config.StartWithWindows = AutoStartCheck.IsChecked == true;
        _onConfigChanged(_config);
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

    private void AutoStartCheck_Changed(object sender, RoutedEventArgs e)
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
