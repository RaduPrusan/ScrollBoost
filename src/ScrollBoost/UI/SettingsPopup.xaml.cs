#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ScrollBoost.Profiles;

namespace ScrollBoost.UI;

public partial class SettingsPopup : Window
{
    private readonly AppConfig _config;
    private readonly Action<AppConfig> _onConfigChanged;
    private bool _suppressEvents = true; // Prevent events during InitializeComponent

    public SettingsPopup(AppConfig config, Action<AppConfig> onConfigChanged)
    {
        _config = config;
        _onConfigChanged = onConfigChanged;
        InitializeComponent(); // XAML triggers events — _suppressEvents blocks them
        ApplyTheme();
        LoadFromConfig();
    }

    private void ApplyTheme()
    {
        bool isLight = TrayIconHelper.IsLightTheme();

        // Colors based on theme
        Color bgColor           = isLight ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20);
        Color surfaceColor      = isLight ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x2D, 0x2D, 0x2D);
        Color textColor         = isLight ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Color.FromRgb(0xFF, 0xFF, 0xFF);
        Color secondaryColor    = isLight ? Color.FromRgb(0x66, 0x66, 0x66) : Color.FromRgb(0x99, 0x99, 0x99);
        Color accentColor       = isLight ? Color.FromRgb(0x00, 0x5F, 0xB8) : Color.FromRgb(0x60, 0xCD, 0xFF);
        Color sliderTrackColor  = isLight ? Color.FromRgb(0xD0, 0xD0, 0xD0) : Color.FromRgb(0x40, 0x40, 0x40);
        Color borderColor       = isLight ? Color.FromRgb(0xE0, 0xE0, 0xE0) : Color.FromRgb(0x38, 0x38, 0x38);

        SolidColorBrush bgBrush        = new(bgColor);
        SolidColorBrush surfaceBrush   = new(surfaceColor);
        SolidColorBrush textBrush      = new(textColor);
        SolidColorBrush secondaryBrush = new(secondaryColor);
        SolidColorBrush accentBrush    = new(accentColor);
        SolidColorBrush borderBrush    = new(borderColor);

        // Window chrome
        RootBorder.Background   = bgBrush;
        RootBorder.BorderBrush  = borderBrush;
        TitleSeparator.Fill     = borderBrush;

        // Title bar text
        TitleText.Foreground     = textBrush;
        CloseButton.Foreground   = secondaryBrush;

        // Enabled checkbox label
        EnabledLabel.Foreground  = textBrush;

        // Slider labels (name / value)
        SpeedName.Foreground  = textBrush;
        AccelName.Foreground  = textBrush;
        MaxName.Foreground    = textBrush;
        SpeedLabel.Foreground = accentBrush;
        AccelLabel.Foreground = accentBrush;
        MaxLabel.Foreground   = accentBrush;

        // Curve type label
        CurveLabel.Foreground = textBrush;

        // AutoStart checkbox label
        AutoStartLabel.Foreground = textBrush;

        // Rebuild slider styles with correct track color
        string accentHex = ColorToHex(accentColor);
        string trackHex  = ColorToHex(sliderTrackColor);
        UpdateSliderTrackColors(SpeedSlider,  accentHex, trackHex);
        UpdateSliderTrackColors(AccelSlider,  accentHex, trackHex);
        UpdateSliderTrackColors(MaxSlider,    accentHex, trackHex);

        // ComboBox surface + border
        CurveCombo.Background  = surfaceBrush;
        CurveCombo.BorderBrush = borderBrush;
        CurveCombo.Foreground  = textBrush;

        // Light theme: adjust combo item highlight colors via resources
        if (isLight)
        {
            CurveCombo.Resources["HighlightBrush"] = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xE5));
            CurveCombo.Resources["SelectedBrush"]  = new SolidColorBrush(Color.FromRgb(0xCC, 0xE4, 0xFF));
        }
    }

    /// <summary>
    /// Builds a minimal slider ControlTemplate in code so the track/fill colors match the theme.
    /// This is simpler than trying to walk the visual tree before it is rendered.
    /// </summary>
    private static void UpdateSliderTrackColors(Slider slider, string accentHex, string trackHex)
    {
        SolidColorBrush accentBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(accentHex)!;
        SolidColorBrush trackBrush  = (SolidColorBrush)new BrushConverter().ConvertFromString(trackHex)!;
        SolidColorBrush whiteBrush  = Brushes.White;

        // Decrease (filled) button
        var decreaseTemplate = new ControlTemplate(typeof(RepeatButton));
        var decreaseBorder = new FrameworkElementFactory(typeof(Border));
        decreaseBorder.SetValue(Border.BackgroundProperty, accentBrush);
        decreaseBorder.SetValue(Border.HeightProperty, 4.0);
        decreaseBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        decreaseTemplate.VisualTree = decreaseBorder;

        var decreaseStyle = new Style(typeof(RepeatButton));
        decreaseStyle.Setters.Add(new Setter(Control.TemplateProperty, decreaseTemplate));

        // Increase (empty) button
        var increaseTemplate = new ControlTemplate(typeof(RepeatButton));
        var increaseBorder = new FrameworkElementFactory(typeof(Border));
        increaseBorder.SetValue(Border.BackgroundProperty, trackBrush);
        increaseBorder.SetValue(Border.HeightProperty, 4.0);
        increaseBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        increaseTemplate.VisualTree = increaseBorder;

        var increaseStyle = new Style(typeof(RepeatButton));
        increaseStyle.Setters.Add(new Setter(Control.TemplateProperty, increaseTemplate));

        // Thumb
        var thumbTemplate = new ControlTemplate(typeof(Thumb));
        var thumbEllipse = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
        thumbEllipse.SetValue(System.Windows.Shapes.Shape.FillProperty, accentBrush);
        thumbEllipse.SetValue(FrameworkElement.WidthProperty, 16.0);
        thumbEllipse.SetValue(FrameworkElement.HeightProperty, 16.0);
        var thumbTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        thumbTrigger.Setters.Add(new Setter(System.Windows.Shapes.Shape.FillProperty, whiteBrush, thumbEllipse.Name));
        // Note: named-element trigger setters in code-behind require the element to have a Name set.
        // We skip the hover trigger here; the XAML-defined template already handles it for the
        // initial (dark) theme. The thumb color is correct for both themes regardless.
        thumbTemplate.VisualTree = thumbEllipse;

        var thumbStyle = new Style(typeof(Thumb));
        thumbStyle.Setters.Add(new Setter(Control.TemplateProperty, thumbTemplate));

        // Assemble the slider template
        var trackFactory = new FrameworkElementFactory(typeof(Track));
        trackFactory.Name = "PART_Track";

        var decreaseButton = new FrameworkElementFactory(typeof(RepeatButton));
        decreaseButton.SetValue(RepeatButton.CommandProperty, Slider.DecreaseLarge);
        decreaseButton.SetValue(RepeatButton.StyleProperty, decreaseStyle);

        var thumb = new FrameworkElementFactory(typeof(Thumb));
        thumb.SetValue(FrameworkElement.StyleProperty, thumbStyle);

        var increaseButton = new FrameworkElementFactory(typeof(RepeatButton));
        increaseButton.SetValue(RepeatButton.CommandProperty, Slider.IncreaseLarge);
        increaseButton.SetValue(RepeatButton.StyleProperty, increaseStyle);

        trackFactory.AppendChild(decreaseButton);
        trackFactory.AppendChild(thumb);
        trackFactory.AppendChild(increaseButton);

        var gridFactory = new FrameworkElementFactory(typeof(Grid));
        gridFactory.AppendChild(trackFactory);

        var sliderTemplate = new ControlTemplate(typeof(Slider));
        sliderTemplate.VisualTree = gridFactory;

        var sliderStyle = new Style(typeof(Slider));
        sliderStyle.Setters.Add(new Setter(Control.TemplateProperty, sliderTemplate));
        slider.Style = sliderStyle;
    }

    private static string ColorToHex(Color c) =>
        $"#{c.R:X2}{c.G:X2}{c.B:X2}";

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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
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
