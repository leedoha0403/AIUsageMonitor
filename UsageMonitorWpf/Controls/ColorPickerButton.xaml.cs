using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UsageMonitorWpf.Controls;

// A palette-grid color picker in the style of Sheets/Docs "text color" pickers: a swatch button that
// opens a popup with a hue/shade grid plus a shared "custom colors" row and a hex fallback field.
public partial class ColorPickerButton : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(string), typeof(ColorPickerButton),
        new FrameworkPropertyMetadata("#000000", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorChanged));

    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    // The Application resource key (e.g. "InkBrush", "HeaderStartColor") this field feeds into ThemeService.
    // Set so hovering a swatch can preview it live app-wide without touching the bound Color/state until clicked.
    public static readonly DependencyProperty PreviewResourceKeyProperty = DependencyProperty.Register(
        nameof(PreviewResourceKey), typeof(string), typeof(ColorPickerButton), new PropertyMetadata(null));

    public string? PreviewResourceKey
    {
        get => (string?)GetValue(PreviewResourceKeyProperty);
        set => SetValue(PreviewResourceKeyProperty, value);
    }

    // Shared across every picker instance so a color added in one field is available in the others, like Sheets.
    public static ObservableCollection<string> RecentColors { get; } = new();

    public IReadOnlyList<string> Palette { get; } = BuildPalette();

    public ColorPickerButton()
    {
        InitializeComponent();
        UpdateSwatch();
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ColorPickerButton)d;
        control.UpdateSwatch();
        if (control.HexBox.Text != control.Color) control.HexBox.Text = control.Color;
    }

    private void UpdateSwatch() => SwatchFill.Background = ToBrush(Color);

    private static System.Windows.Media.Brush ToBrush(string hex)
    {
        try
        {
            return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return System.Windows.Media.Brushes.Transparent;
        }
    }

    private void OnPopupOpened(object? sender, EventArgs e) => HexBox.Text = Color;

    // Closing without a click (Escape, click-away) must not leave a hovered swatch's preview applied.
    private void OnPopupClosed(object? sender, EventArgs e) => RevertPreview();

    private void OnSwatchPick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex })
        {
            Color = hex;
            Pop.IsOpen = false;
        }
    }

    private void OnSwatchPreviewEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex }) ApplyPreview(hex);
    }

    private void OnSwatchPreviewLeave(object sender, System.Windows.Input.MouseEventArgs e) => RevertPreview();

    private void ApplyPreview(string hex)
    {
        if (string.IsNullOrEmpty(PreviewResourceKey)) return;
        System.Windows.Media.Color color;
        try
        {
            color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return;
        }

        var resources = System.Windows.Application.Current.Resources;
        if (PreviewResourceKey.EndsWith("Color", StringComparison.Ordinal))
        {
            resources[PreviewResourceKey] = color;
        }
        else
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[PreviewResourceKey] = brush;
        }
    }

    // No separate "original value" to track: Color only changes on an actual click, so reapplying it
    // undoes whatever the hover preview temporarily overrode.
    private void RevertPreview() => ApplyPreview(Color);

    private void OnAddRecent(object sender, RoutedEventArgs e)
    {
        var hex = NormalizeHex(HexBox.Text);
        if (hex is null) return;
        RecentColors.Remove(hex);
        RecentColors.Insert(0, hex);
        while (RecentColors.Count > 12) RecentColors.RemoveAt(RecentColors.Count - 1);
        Color = hex;
    }

    private void OnHexBoxLostFocus(object sender, RoutedEventArgs e)
    {
        var hex = NormalizeHex(HexBox.Text);
        HexBox.Text = hex ?? Color;
        if (hex is not null) Color = hex;
    }

    private void OnHexBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var hex = NormalizeHex(HexBox.Text);
        if (hex is null) return;
        Color = hex;
        Pop.IsOpen = false;
    }

    private static string? NormalizeHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        try
        {
            System.Windows.Media.ColorConverter.ConvertFromString(value);
            return value;
        }
        catch
        {
            return null;
        }
    }

    private static List<string> BuildPalette()
    {
        // One grayscale column plus a row of hue columns, five lightness steps each (lightest to darkest).
        string[] grays = ["#FFFFFF", "#D9D9D9", "#B7B7B7", "#666666", "#000000"];
        double[] hues = [0, 25, 40, 55, 90, 150, 190, 215, 245, 275, 320];
        double[] lightness = [0.85, 0.68, 0.5, 0.34, 0.2];

        var result = new List<string>();
        for (var row = 0; row < 5; row++)
        {
            result.Add(grays[row]);
            foreach (var hue in hues) result.Add(HslToHex(hue, 0.55, lightness[row]));
        }
        return result;
    }

    private static string HslToHex(double h, double s, double l)
    {
        double r, g, b;
        if (s <= 0)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            var hk = h / 360.0;
            r = HueToRgb(p, q, hk + 1.0 / 3);
            g = HueToRgb(p, q, hk);
            b = HueToRgb(p, q, hk - 1.0 / 3);
        }
        return $"#{ToByte(r):X2}{ToByte(g):X2}{ToByte(b):X2}";
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
}
