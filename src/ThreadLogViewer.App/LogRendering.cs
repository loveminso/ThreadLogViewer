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
    private readonly Dictionary<string, Brush> brushes = [];
    public KnownLayer Layer => KnownLayer.Background;
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Projection is null || !textView.VisualLinesValid) return;
        foreach (var visual in textView.VisualLines)
        {
            var line = Projection.AtDisplayLine(visual.FirstDocumentLine.LineNumber);
            if (line is null) continue;
            string color = ThreadColors.ForThread(line.Value.ThreadId);
            if (!brushes.TryGetValue(color, out var brush))
            {
                brush = (Brush)new BrushConverter().ConvertFromString(color)!;
                brush.Freeze(); brushes[color] = brush;
            }
            double top = visual.VisualTop - textView.VerticalOffset;
            drawingContext.DrawRectangle(brush, null, new Rect(0, top, textView.ActualWidth, visual.Height));
        }
    }
}

public sealed class OriginalLineMargin : AbstractMargin
{
    public LogProjection? Projection { get; set; }
    public double LogFontSize { get; set; } = 14;
    private readonly Typeface typeface = new("Consolas");
    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Format(new string('9', Math.Max(3, (Projection?.Source.Lines.Count ?? 0).ToString().Length)));
        return new Size(text.Width + 20, 0);
    }
    private FormattedText Format(string value) => new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        typeface, LogFontSize, Brushes.SlateGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= LinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += LinesChanged;
    }
    private void LinesChanged(object? sender, EventArgs e) => InvalidateVisual();
    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(247, 248, 250)), null, new Rect(RenderSize));
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
    private readonly Brush fill = new SolidColorBrush(Color.FromRgb(255, 220, 88));
    private readonly Pen outline = new(Brushes.DarkGoldenrod, 1);
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
                drawingContext.DrawRectangle(fill, outline, rect);
        }
    }
}
