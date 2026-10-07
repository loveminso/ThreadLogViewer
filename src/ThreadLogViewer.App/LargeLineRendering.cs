using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ThreadLogViewer.App;

public readonly record struct LargeLineWindow(int StartOffset, int EndOffset);

/// <summary>
/// Bounds text shaping for unusually long physical lines. Only visual elements are replaced;
/// the document, original line numbers, searches, selections and copied text remain unchanged.
/// Every hidden range is labelled and can be opened by clicking its previous/next marker.
/// </summary>
public sealed class LargeLineElementGenerator : VisualLineElementGenerator
{
    public const int Threshold = 16384;
    public const int DisplayBudget = 4096;
    public bool Enabled { get; set; } = true;
    public int FocusOffset { get; set; }
    public event Action<int>? NavigateRequested;

    public static LargeLineWindow GetWindow(TextDocument document, DocumentLine line, int focusOffset)
    {
        int relative = Math.Clamp(focusOffset - line.Offset, 0, Math.Max(0, line.Length - 1));
        int start = line.Offset + relative / DisplayBudget * DisplayBudget;
        int end = Math.Min(line.EndOffset, start + DisplayBudget);
        // A surrogate pair belongs to one visible region; do not turn either half into a marker.
        if (start > line.Offset && char.IsLowSurrogate(document.GetCharAt(start)) && char.IsHighSurrogate(document.GetCharAt(start - 1))) start--;
        if (end < line.EndOffset && char.IsLowSurrogate(document.GetCharAt(end)) && char.IsHighSurrogate(document.GetCharAt(end - 1))) end++;
        return new(start, end);
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (!Enabled || line.Length <= Threshold || startOffset >= line.EndOffset) return -1;
        var range = GetWindow(CurrentContext.Document, line, FocusOffset);
        if (startOffset < range.StartOffset) return Math.Max(startOffset, line.Offset);
        return startOffset <= range.EndOffset && range.EndOffset < line.EndOffset ? range.EndOffset : -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (!Enabled || line.Length <= Threshold) return null;
        var range = GetWindow(CurrentContext.Document, line, FocusOffset);
        if (offset < range.StartOffset)
        {
            // StartOffset can move back by one UTF-16 unit to keep a surrogate pair together.
            // Step from the nominal block, rather than subtracting a block from that adjusted
            // start, which would skip a whole chunk when the pair straddles a boundary.
            int previousBlock = Math.Max(0, (range.StartOffset - line.Offset + DisplayBudget - 1) / DisplayBudget - 1);
            return new RangeMarker($"[긴 줄 · 앞 {range.StartOffset - offset:N0}자 · 클릭: 이전 부분] ", range.StartOffset - offset,
                () => NavigateRequested?.Invoke(line.Offset + previousBlock * DisplayBudget));
        }
        if (offset == range.EndOffset && offset < line.EndOffset)
            return new RangeMarker($" [긴 줄 · 뒤 {line.EndOffset - offset:N0}자 · 클릭: 다음 부분]", line.EndOffset - offset,
                () => NavigateRequested?.Invoke(range.EndOffset));
        return null;
    }

    private sealed class RangeMarker(string label, int documentLength, Action navigate) : FormattedTextElement(label, documentLength)
    {
        protected override void OnQueryCursor(QueryCursorEventArgs e) { e.Cursor = Cursors.Hand; e.Handled = true; }
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) { e.Handled = true; navigate(); }
        }
    }
}
