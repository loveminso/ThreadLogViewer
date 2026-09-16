using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public sealed class ThreadBackgroundRenderer : IBackgroundRenderer
{
    public LogProjection? Projection { get; set; }
    public WorkbenchTheme Theme { get; set; } = new(true);
    public KnownLayer Layer => KnownLayer.Background;
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Projection is null || !textView.VisualLinesValid) return;
        foreach (var visual in textView.VisualLines)
        {
            var line = Projection.AtDisplayLine(visual.FirstDocumentLine.LineNumber);
            if (line is null) continue;
            Brush brush = Theme.ThreadBackground(line.Value.ThreadId);
            double top = visual.VisualTop - textView.VerticalOffset;
            drawingContext.DrawRectangle(brush, null, new Rect(0, top, textView.ActualWidth, visual.Height));
            drawingContext.DrawRectangle(Theme.ThreadMarker(line.Value.ThreadId), null, new Rect(0, top, 2, visual.Height));
        }
    }
}

public sealed class OriginalLineMargin : AbstractMargin
{
    public LogProjection? Projection { get; set; }
    public double LogFontSize { get; set; } = 14;
    public FontFamily LogFontFamily { get; set; } = LogTypography.Create(false);
    public WorkbenchTheme Theme { get; set; } = new(true);
    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Format(new string('9', Math.Max(3, (Projection?.Source.Lines.Count ?? 0).ToString().Length)));
        return new Size(text.Width + 20, 0);
    }
    private FormattedText Format(string value) => new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface(LogFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), LogFontSize, Theme.Muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= LinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += LinesChanged;
    }
    private void LinesChanged(object? sender, EventArgs e) => InvalidateVisual();
    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(Theme.Panel, null, new Rect(RenderSize));
        if (TextView is null || !TextView.VisualLinesValid || Projection is null) return;
        foreach (var visual in TextView.VisualLines)
        {
            var line = Projection.AtDisplayLine(visual.FirstDocumentLine.LineNumber);
            if (line is null) continue;
            var label = Format(line.Value.OriginalLineNumber.ToString(CultureInfo.InvariantCulture));
            drawingContext.DrawText(label, new Point(ActualWidth - label.Width - 10, visual.VisualTop - TextView.VerticalOffset));
        }
    }
}

public sealed class SearchHighlightRenderer : IBackgroundRenderer
{
    public SearchHit[] Hits { get; set; } = [];
    public KnownLayer Layer => KnownLayer.Background;
    private WorkbenchTheme theme = new(true);
    private Pen outline = new(new WorkbenchTheme(true).SearchOutline, 1);
    public WorkbenchTheme Theme { get => theme; set { theme = value; outline = new(value.SearchOutline, 1); outline.Freeze(); } }
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || textView.VisualLines.Count == 0 || Hits.Length == 0) return;
        int start = textView.VisualLines[0].FirstDocumentLine.Offset;
        int end = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        int low = 0, high = Hits.Length;
        while (low < high) { int mid = (low + high) / 2; if (Hits[mid].Offset + Hits[mid].Length < start) low = mid + 1; else high = mid; }
        for (int i = low; i < Hits.Length && Hits[i].Offset <= end; i++)
        {
            var hit = Hits[i];
            var segment = new ICSharpCode.AvalonEdit.Document.TextSegment { StartOffset = hit.Offset, Length = hit.Length };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                drawingContext.DrawRectangle(theme.Search, outline, rect);
        }
    }
}
