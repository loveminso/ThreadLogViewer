using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ThreadLogViewer.App;

// Presentation-only palettes. Thread identity is stable across filters, files and themes.
public sealed class WorkbenchTheme(bool dark)
{
    public bool IsDark { get; } = dark;
    public Brush Background { get; } = Solid(dark ? "#181C22" : "#FFFFFF");
    public Brush Panel { get; } = Solid(dark ? "#1D222A" : "#F6F7F9");
    public Brush Chrome { get; } = Solid(dark ? "#242A33" : "#EEF0F3");
    public Brush Border { get; } = Solid(dark ? "#303843" : "#DEE3EA");
    public Brush Text { get; } = Solid(dark ? "#E0E5EC" : "#25303D");
    public Brush Muted { get; } = Solid(dark ? "#A7B2C2" : "#5B687B");
    public Brush Accent { get; } = Solid(dark ? "#A8B8FF" : "#405BC0");
    public Brush AccentText { get; } = Solid(dark ? "#152047" : "#FFFFFF");
    public Brush Hover { get; } = Solid(dark ? "#303A4A" : "#E8EDFA");
    public Brush Selection { get; } = Solid(dark ? "#4363AE" : "#2353A2");
    public Brush SelectionText { get; } = Solid("#FFFFFF");
    public Brush Search { get; } = Solid(dark ? "#725817" : "#F7DA83");
    public Brush SearchOutline { get; } = Solid(dark ? "#D5AF4D" : "#A97810");
    public Brush ScrollThumb { get; } = Solid(dark ? "#566170" : "#AFB9C6");
    private static readonly string[] DarkThreads = ["#92B6E2", "#A1C798", "#D3B18F", "#B89DDB", "#8EC9BD", "#D7A1B3", "#CABD7D", "#98B7D3", "#B4C88B", "#9DBECD", "#C9A0C2", "#C9B2A0"];
    private static readonly string[] LightThreads = ["#4C719F", "#5C8053", "#9B714D", "#7955A3", "#427D72", "#A35C76", "#8F7B32", "#527994", "#74873F", "#4B7A8B", "#925D87", "#8F715B"];
    private readonly Dictionary<int, Brush> markers = [];
    private readonly Dictionary<int, Brush> rows = [];

    public Brush ThreadMarker(int? id)
    {
        int key = id is null ? -1 : (int)((uint)id.Value % 12);
        if (!markers.TryGetValue(key, out var brush))
            markers[key] = brush = Solid(key == -1 ? (IsDark ? "#A0A8B5" : "#6C7582") : (IsDark ? DarkThreads : LightThreads)[key]);
        return brush;
    }
    public Brush ThreadBackground(int? id)
    {
        int key = id is null ? -1 : (int)((uint)id.Value % 12);
        if (!rows.TryGetValue(key, out var brush))
        {
            Color marker = ((SolidColorBrush)ThreadMarker(id)).Color;
            Color background = ((SolidColorBrush)Background).Color;
            double opacity = IsDark ? 0.18 : 0.16;
            byte Blend(byte a, byte b) => (byte)Math.Round(a * opacity + b * (1 - opacity));
            var mixed = new SolidColorBrush(Color.FromRgb(Blend(marker.R, background.R), Blend(marker.G, background.G), Blend(marker.B, background.B)));
            mixed.Freeze(); rows[key] = brush = mixed;
        }
        return brush;
    }
    public void Apply(ResourceDictionary resources)
    {
        resources["BackgroundBrush"] = Background; resources["PanelBrush"] = Panel;
        resources["ChromeBrush"] = Chrome; resources["BorderBrush"] = Border;
        resources["TextBrush"] = Text; resources["MutedBrush"] = Muted;
        resources["AccentBrush"] = Accent; resources["AccentTextBrush"] = AccentText;
        resources["HoverBrush"] = Hover; resources["ScrollThumbBrush"] = ScrollThumb;
    }
    public static SolidColorBrush Solid(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze(); return brush;
    }
    public void ApplyTitleBar(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int darkMode = IsDark ? 1 : 0;
        // Per-window appearance only. Older Windows versions may ignore this attribute.
        _ = DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

public static class LogTypography
{
    // Real line metrics: no extra characters or rows are inserted into the document.
    public static FontFamily Create(bool compact)
    {
        var family = new FontFamily { LineSpacing = compact ? 1.25 : 1.65, Baseline = compact ? 0.98 : 1.18 };
        family.FamilyMaps.Add(new FontFamilyMap { Target = "Consolas, D2Coding, Malgun Gothic", Unicode = "0000-10FFFF" });
        return family;
    }
}
