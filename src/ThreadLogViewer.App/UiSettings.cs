namespace ThreadLogViewer.App;

/// <summary>Display preferences only. Log content, file paths and analysis state are never persisted.</summary>
public sealed record UiSettings
{
    public bool Dark { get; init; } = true;
    public bool Compact { get; init; }
    public bool WordWrap { get; init; }
    public double FontSize { get; init; } = 14;
    public double PanelWidth { get; init; } = 295;
    public double ResultsHeight { get; init; } = 180;
    public bool ResultsCollapsed { get; init; }
    public bool ThreadBackgrounds { get; init; } = true;
    public static UiSettings Default { get; } = new();

    public UiSettings Normalize() => this with
    {
        FontSize = FontSize is 11 or 12 or 13 or 14 or 16 or 18 or 20 or 24 ? FontSize : 14,
        PanelWidth = double.IsFinite(PanelWidth) && PanelWidth is >= 275 and <= 600 ? PanelWidth : 295,
        ResultsHeight = double.IsFinite(ResultsHeight) && ResultsHeight is >= 80 and <= 1200 ? ResultsHeight : 180
    };
}
