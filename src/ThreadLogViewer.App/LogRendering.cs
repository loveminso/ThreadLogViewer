using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public sealed class ThreadBackgroundRenderer : IBackgroundRenderer
{
    public LogProjection? Projection { get; set; }
    public int? ContextEntryIndex { get; set; }
    public int? ActionLineIndex { get; set; }
    private WorkbenchTheme theme = new(true);
    private Brush contextTint = CreateContextTint(new(true));
    private Pen contextOutline = CreateContextOutline(new(true));
    private Brush actionTint = CreateActionTint(new(true));
    private Pen actionOutline = CreateActionOutline(new(true));
    public WorkbenchTheme Theme
    {
        get => theme;
        set
        {
            theme = value; contextTint = CreateContextTint(value); contextOutline = CreateContextOutline(value);
            actionTint = CreateActionTint(value); actionOutline = CreateActionOutline(value);
        }
    }
    private static Brush CreateContextTint(WorkbenchTheme value)
    {
        var color = ((SolidColorBrush)value.Accent).Color;
        var brush = new SolidColorBrush(Color.FromArgb(value.IsDark ? (byte)24 : (byte)18, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
    private static Pen CreateContextOutline(WorkbenchTheme value)
    {
        var pen = new Pen(value.Accent, 1);
        pen.Freeze();
        return pen;
    }
    private static Brush CreateActionTint(WorkbenchTheme value)
    {
        var color = ((SolidColorBrush)value.Accent).Color;
        var brush = new SolidColorBrush(Color.FromArgb(value.IsDark ? (byte)48 : (byte)36, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
    private static Pen CreateActionOutline(WorkbenchTheme value)
    {
        var pen = new Pen(value.Accent, 1.5);
        pen.Freeze();
        return pen;
    }
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
            if (ContextEntryIndex == line.Value.EntryIndex)
            {
                var entry = Projection.Source.Entries[line.Value.EntryIndex];
                drawingContext.DrawRectangle(contextTint, null, new Rect(0, top, textView.ActualWidth, visual.Height));
                drawingContext.DrawRectangle(Theme.Accent, null, new Rect(0, top, 4, visual.Height));
                if (line.Value.OriginalLineNumber - 1 == entry.StartLineIndex)
                    drawingContext.DrawLine(contextOutline, new Point(0, top + 0.5), new Point(textView.ActualWidth, top + 0.5));
                var last = Projection.AtDisplayLine(visual.LastDocumentLine.LineNumber);
                if (last?.OriginalLineNumber - 1 == entry.StartLineIndex + entry.LineCount - 1)
                    drawingContext.DrawLine(contextOutline, new Point(0, top + visual.Height - 0.5), new Point(textView.ActualWidth, top + visual.Height - 0.5));
            }
            if (ActionLineIndex == line.Value.OriginalLineNumber - 1)
            {
                drawingContext.DrawRectangle(actionTint, null, new Rect(0, top, textView.ActualWidth, visual.Height));
                drawingContext.DrawRectangle(null, actionOutline, new Rect(0.75, top + 0.75,
                    Math.Max(0, textView.ActualWidth - 1.5), Math.Max(0, visual.Height - 1.5)));
                drawingContext.DrawRectangle(Theme.Accent, null, new Rect(0, top, 2, visual.Height));
            }
        }
    }
}

public sealed class OriginalLineMargin : AbstractMargin
{
    public OriginalLineMargin()
    {
        Cursor = Cursors.Hand;
        ToolTip = "줄 번호 드래그: 연속 줄 선택 · Ctrl+클릭: 줄 추가 / 해제";
        dragScrollTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(60) };
        dragScrollTimer.Tick += (_, _) =>
        {
            if (!IsMouseCaptured || !IsLineGestureCurrent()) { EndLineSelection(); return; }
            if (!dragMoved) return;
            double y = TranslatePoint(dragPoint, TextView).Y;
            double edge = Math.Min(16, TextView.ActualHeight / 4);
            if (y < edge) AutoScrollLineSelection(-1);
            else if (y >= TextView.ActualHeight - edge) AutoScrollLineSelection(1);
        };
    }
    public event Action<int>? LineClicked;
    public event Action<int, bool>? LineSelectionStarted;
    public event Action<int>? LineSelectionExtended;
    public event Action? LineSelectionFinished;
    private readonly DispatcherTimer dragScrollTimer;
    private LogProjection? dragProjection;
    private ICSharpCode.AvalonEdit.Document.TextDocument? dragDocument;
    private int dragLastLine;
    private Point dragPoint, dragStartPoint;
    private bool dragMoved;
    public IReadOnlySet<int> Bookmarks { get; set; } = new HashSet<int>();
    public IReadOnlySet<int> SelectedLineIndexes { get; set; } = new HashSet<int>();
    private int? timeALineIndex, timeBLineIndex;
    public int? TimeALineIndex
    {
        get => timeALineIndex;
        set { if (timeALineIndex == value) return; timeALineIndex = value; InvalidateMeasure(); InvalidateVisual(); }
    }
    public int? TimeBLineIndex
    {
        get => timeBLineIndex;
        set { if (timeBLineIndex == value) return; timeBLineIndex = value; InvalidateMeasure(); InvalidateVisual(); }
    }
    private int? contextLineIndex;
    public int? ContextLineIndex
    {
        get => contextLineIndex;
        set { if (contextLineIndex == value) return; contextLineIndex = value; InvalidateMeasure(); InvalidateVisual(); }
    }
    private int? actionLineIndex;
    public int? ActionLineIndex
    {
        get => actionLineIndex;
        set { if (actionLineIndex == value) return; actionLineIndex = value; InvalidateVisual(); }
    }
    public LogProjection? Projection { get; set; }
    public double LogFontSize { get; set; } = 14;
    public FontFamily LogFontFamily { get; set; } = LogTypography.Create(false);
    public WorkbenchTheme Theme { get; set; } = new(true);
    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Format(new string('9', Math.Max(3, (Projection?.Source.Lines.Count ?? 0).ToString().Length)));
        double contextWidth = ContextLineIndex is null && TimeALineIndex is null && TimeBLineIndex is null ? 0 : ContextLabel("A/B 기준").Width + 12;
        return new Size(text.Width + 20 + contextWidth, 0);
    }
    private FormattedText Format(string value, bool action = false, bool selected = false) => new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface(LogFontFamily, FontStyles.Normal, action || selected ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), LogFontSize,
        action ? Theme.AccentText : selected ? Theme.SelectionText : Theme.Muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private FormattedText ContextLabel(string value = "기준", bool action = false) => new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Malgun Gothic"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), Math.Max(10, LogFontSize * 0.75),
        action ? Theme.Accent : Theme.AccentText, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        EndLineSelection();
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= LinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += LinesChanged;
    }
    private void LinesChanged(object? sender, EventArgs e) => InvalidateVisual();
    internal int? DisplayLineAtPoint(Point textViewPoint)
    {
        if (TextView is null || Projection is null || textViewPoint.Y < 0 || textViewPoint.Y >= TextView.ActualHeight) return null;
        TextView.EnsureVisualLines();
        double documentY = textViewPoint.Y + TextView.VerticalOffset;
        var visual = TextView.VisualLines.FirstOrDefault(line => documentY >= line.VisualTop && documentY < line.VisualTop + line.Height);
        if (visual is null || Projection.AtDisplayLine(visual.FirstDocumentLine.LineNumber) is null) return null;
        return visual.FirstDocumentLine.LineNumber;
    }
    internal bool TryClickLineAtPoint(Point textViewPoint)
    {
        if (DisplayLineAtPoint(textViewPoint) is not { } displayLine) return false;
        LineClicked?.Invoke(displayLine);
        return true;
    }
    /// <summary>Selects the visible physical line at a point relative to this margin.</summary>
    public bool SelectLineAtPoint(Point marginPoint)
    {
        if (TextView is null || marginPoint.X < 0 || marginPoint.X >= ActualWidth || marginPoint.Y < 0 || marginPoint.Y >= ActualHeight) return false;
        return TryClickLineAtPoint(TranslatePoint(marginPoint, TextView));
    }
    public bool BeginLineSelectionAtPoint(Point marginPoint, bool control)
    {
        if (TextView is null || Projection is null || marginPoint.X < 0 || marginPoint.X >= ActualWidth || marginPoint.Y < 0 || marginPoint.Y >= ActualHeight
            || DisplayLineAtPoint(TranslatePoint(marginPoint, TextView)) is not { } displayLine) return false;
        EndLineSelection();
        var view = Projection; var document = TextView.Document;
        // Keep the existing one-line event compatible with callers that do not implement dragging.
        if (!control) LineClicked?.Invoke(displayLine);
        if (Projection != view || TextView.Document != document) return false;
        dragProjection = view; dragDocument = document; dragLastLine = displayLine;
        dragPoint = dragStartPoint = marginPoint; dragMoved = false;
        LineSelectionStarted?.Invoke(displayLine, control);
        return IsLineGestureCurrent();
    }
    private bool IsLineGestureCurrent() => dragProjection is not null && dragProjection == Projection && TextView is not null && dragDocument == TextView.Document;
    private bool ExtendLineGesture(int displayLine)
    {
        if (!IsLineGestureCurrent() || Projection?.AtDisplayLine(displayLine) is null) { EndLineSelection(); return false; }
        if (displayLine != dragLastLine) { dragLastLine = displayLine; LineSelectionExtended?.Invoke(displayLine); }
        return IsLineGestureCurrent();
    }
    public bool ContinueLineSelectionAtPoint(Point marginPoint)
    {
        if (!IsLineGestureCurrent()) { EndLineSelection(); return false; }
        dragPoint = marginPoint;
        dragMoved |= Math.Abs(marginPoint.X - dragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(marginPoint.Y - dragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
        var point = TranslatePoint(marginPoint, TextView);
        double y = Math.Clamp(point.Y, 0, Math.Max(0, TextView.ActualHeight - 0.01));
        int? line = DisplayLineAtPoint(new Point(0, y));
        if (line is null)
            line = TextView.VisualLines.LastOrDefault(visual => Projection!.AtDisplayLine(visual.FirstDocumentLine.LineNumber) is not null)?.FirstDocumentLine.LineNumber;
        if (line is null || !ExtendLineGesture(line.Value)) return false;
        if (dragMoved && point.Y < 0) AutoScrollLineSelection(-1);
        else if (dragMoved && point.Y >= TextView.ActualHeight) AutoScrollLineSelection(1);
        return IsLineGestureCurrent();
    }
    public bool AutoScrollLineSelection(int direction)
    {
        if (!IsLineGestureCurrent() || direction == 0) return false;
        int target = Math.Clamp(dragLastLine + Math.Sign(direction), 1, Projection!.Count);
        if (target == dragLastLine || !ExtendLineGesture(target)) return false;
        var visual = TextView.GetOrConstructVisualLine(TextView.Document.GetLineByNumber(target));
        double top = TextView.GetVisualTopByDocumentLine(target);
        double offset = direction < 0 ? Math.Min(TextView.VerticalOffset, top)
            : Math.Max(TextView.VerticalOffset, top + visual.Height - TextView.ActualHeight);
        ((IScrollInfo)TextView).SetVerticalOffset(Math.Max(0, offset));
        TextView.EnsureVisualLines();
        return true;
    }
    public void EndLineSelection()
    {
        bool active = dragProjection is not null;
        dragProjection = null; dragDocument = null; dragLastLine = 0; dragMoved = false;
        dragScrollTimer?.Stop();
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (active) LineSelectionFinished?.Invoke();
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (BeginLineSelectionAtPoint(e.GetPosition(this), (Keyboard.Modifiers & ModifierKeys.Control) != 0))
        {
            e.Handled = true;
            if (CaptureMouse()) dragScrollTimer.Start(); else EndLineSelection();
            return;
        }
        base.OnMouseLeftButtonDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured && IsLineGestureCurrent()) { ContinueLineSelectionAtPoint(e.GetPosition(this)); e.Handled = true; return; }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsLineGestureCurrent()) { EndLineSelection(); e.Handled = true; return; }
        base.OnMouseLeftButtonUp(e);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    { EndLineSelection(); base.OnLostMouseCapture(e); }
    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(Theme.Panel, null, new Rect(RenderSize));
        if (TextView is null || !TextView.VisualLinesValid || Projection is null) return;
        foreach (var visual in TextView.VisualLines)
        {
            var line = Projection.AtDisplayLine(visual.FirstDocumentLine.LineNumber);
            if (line is null) continue;
            int sourceLine = line.Value.OriginalLineNumber - 1;
            bool action = ActionLineIndex == sourceLine;
            bool selected = SelectedLineIndexes.Contains(sourceLine);
            var label = Format(line.Value.OriginalLineNumber.ToString(CultureInfo.InvariantCulture), action, selected);
            double top = visual.VisualTop - TextView.VerticalOffset;
            if (selected) drawingContext.DrawRectangle(Theme.Selection, null, new Rect(0, top, ActualWidth, visual.Height));
            if (action) drawingContext.DrawRectangle(Theme.Accent, null, new Rect(0, top, ActualWidth, visual.Height));
            if (Bookmarks.Contains(sourceLine))
                drawingContext.DrawEllipse(action ? Theme.AccentText : selected ? Theme.SelectionText : Theme.Accent, null, new Point(5, top + 8), 3, 3);
            string badgeText = TimeALineIndex == sourceLine && TimeBLineIndex == sourceLine ? "A/B" : TimeALineIndex == sourceLine ? "A" : TimeBLineIndex == sourceLine ? "B" : "";
            if (ContextLineIndex == sourceLine) badgeText += (badgeText.Length > 0 ? " " : "") + "기준";
            if (badgeText.Length > 0)
            {
                var badge = ContextLabel(badgeText, action);
                double badgeTop = top + Math.Max(0, (visual.Height - badge.Height - 2) / 2);
                drawingContext.DrawRoundedRectangle(action ? Theme.Panel : Theme.Accent, null, new Rect(10, badgeTop, badge.Width + 6, badge.Height + 2), 3, 3);
                drawingContext.DrawText(badge, new Point(13, badgeTop + 1));
            }
            drawingContext.DrawText(label, new Point(ActualWidth - label.Width - 10, visual.VisualTop - TextView.VerticalOffset));
        }
    }
}

public sealed class SearchHighlightRenderer : IBackgroundRenderer
{
    public HighlightIndex Index { get; set; } = HighlightIndex.Empty;
    public KnownLayer Layer => KnownLayer.Background;
    private WorkbenchTheme theme = new(true);
    private Pen outline = new(new WorkbenchTheme(true).SearchOutline, 1);
    public WorkbenchTheme Theme { get => theme; set { theme = value; outline = new(value.SearchOutline, 1); outline.Freeze(); } }
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || textView.VisualLines.Count == 0 || Index.Count == 0) return;
        int start = textView.VisualLines[0].FirstDocumentLine.Offset;
        int end = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        foreach (var hit in Index.Query(start, end))
        {
            var segment = new ICSharpCode.AvalonEdit.Document.TextSegment { StartOffset = hit.Offset, Length = hit.Length };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                drawingContext.DrawRectangle(theme.Search, outline, rect);
        }
    }
}

public sealed class KeywordHighlightRenderer : IBackgroundRenderer
{
    public HighlightIndex Index { get; set; } = HighlightIndex.Empty;
    public bool IsDark { get; set; } = true;
    public int[] RuleColors { get; set; } = [];
    public KnownLayer Layer => KnownLayer.Background;
    private static readonly Color[] Colors = [ColorsFrom("#F7A844"), ColorsFrom("#B397FF"), ColorsFrom("#4BD6CC"), ColorsFrom("#75AEFF"), ColorsFrom("#FF92BD"), ColorsFrom("#97D778")];
    private static Color ColorsFrom(string value) => (Color)ColorConverter.ConvertFromString(value);
    private readonly Brush[] darkBrushes = MakeBrushes(100);
    private readonly Brush[] lightBrushes = MakeBrushes(90);
    private static Brush[] MakeBrushes(byte alpha) => Colors.Select(c => { var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)); b.Freeze(); return (Brush)b; }).ToArray();
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || textView.VisualLines.Count == 0) return;
        int start = textView.VisualLines[0].FirstDocumentLine.Offset;
        int end = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        foreach (var hit in Index.Query(start, end).OrderByDescending(h => h.RuleIndex))
        {
            var segment = new ICSharpCode.AvalonEdit.Document.TextSegment { StartOffset = hit.Offset, Length = hit.Length };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                drawingContext.DrawRectangle((IsDark ? darkBrushes : lightBrushes)[(hit.RuleIndex < RuleColors.Length ? RuleColors[hit.RuleIndex] : 0) % Colors.Length], null, rect);
        }
    }
}
