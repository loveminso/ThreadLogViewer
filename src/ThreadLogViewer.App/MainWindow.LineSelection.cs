using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Editing;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private LogProjection? lineRangeView;
    private int lineRangeAnchor;
    private int[] lineRangeBase = [];
    private bool lineRangeControl, lineRangeRemove, publishingLineSelection;

    private void InitializeLineSelection()
    {
        margin.LineSelectionStarted += BeginWholeLineRange;
        margin.LineSelectionExtended += ExtendWholeLineRange;
        margin.LineSelectionFinished += EndWholeLineRange;
        Editor.TextArea.SelectionChanged += (_, _) =>
        {
            if (!publishingLineSelection && Editor.TextArea.Selection is not WholeLineSelection) ResetLineSelectionGesture();
            RefreshLineSelectionIndicator();
        };
    }

    private bool IsSelectableDisplayLine(int displayLine) => viewReady && !restoringPosition && data is not null && projection is not null
        && projection.Source == data && projection.AtDisplayLine(displayLine) is { } row && displayLine <= Editor.Document.LineCount
        && (ActiveScope is null || ActiveScope.Value.Contains(row.OriginalLineNumber - 1));
    private WholeLineSelection? CurrentWholeLineSelection() => projection is not null && Editor.TextArea.Selection is WholeLineSelection selection
        && selection.IsFor(projection, Editor.Document) ? selection : null;
    private void PrepareWholeLineGesture()
    {
        lineActionVersion++;
        if (Editor.ContextMenu?.IsOpen == true) Editor.ContextMenu.IsOpen = false;
        ResetLineActionCapture();
    }

    private void SelectGutterDisplayLine(int displayLine)
    {
        if (!IsSelectableDisplayLine(displayLine)) return;
        ResetLineSelectionGesture();
        PrepareWholeLineGesture();
        PublishWholeLineSelection([displayLine], displayLine, true);
    }
    private bool ToggleWholeDisplayLine(int displayLine)
    {
        if (!IsSelectableDisplayLine(displayLine)) return false;
        ResetLineSelectionGesture();
        PrepareWholeLineGesture();
        var selected = CurrentWholeLineSelection()?.DisplayLines.ToHashSet() ?? [];
        if (!selected.Add(displayLine)) selected.Remove(displayLine);
        PublishWholeLineSelection(selected, displayLine, true);
        return true;
    }
    private bool ToggleWholeLineAtPoint(Point textViewPoint)
    {
        var view = Editor.TextArea.TextView;
        return textViewPoint.X >= 0 && textViewPoint.X < view.ActualWidth
            && margin.DisplayLineAtPoint(textViewPoint) is { } line && ToggleWholeDisplayLine(line);
    }
    private bool HandleLineSelectionMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return false;
        if (!ToggleWholeLineAtPoint(e.GetPosition(Editor.TextArea.TextView))) return false;
        e.Handled = true;
        return true;
    }
    private void BeginWholeLineRange(int displayLine, bool control)
    {
        if (!IsSelectableDisplayLine(displayLine)) { ResetLineSelectionGesture(); return; }
        PrepareWholeLineGesture();
        lineRangeView = projection; lineRangeAnchor = displayLine; lineRangeControl = control;
        lineRangeBase = control ? CurrentWholeLineSelection()?.DisplayLines.ToArray() ?? [] : [];
        lineRangeRemove = control && lineRangeBase.Contains(displayLine);
        ExtendWholeLineRange(displayLine);
        Editor.Focus();
    }
    private void ExtendWholeLineRange(int displayLine)
    {
        if (projection != lineRangeView || !IsSelectableDisplayLine(displayLine)) { ResetLineSelectionGesture(); return; }
        var selected = lineRangeBase.ToHashSet();
        int first = Math.Min(lineRangeAnchor, displayLine), last = Math.Max(lineRangeAnchor, displayLine);
        for (int line = first; line <= last; line++)
        {
            if (lineRangeControl && lineRangeRemove) selected.Remove(line); else selected.Add(line);
        }
        PublishWholeLineSelection(selected, displayLine, false);
    }
    private void EndWholeLineRange()
    { lineRangeView = null; lineRangeBase = []; lineRangeAnchor = 0; lineRangeControl = lineRangeRemove = false; }
    private void ResetLineSelectionGesture()
    {
        margin.EndLineSelection(); EndWholeLineRange();
        margin.SelectedLineIndexes = new HashSet<int>(); margin.InvalidateVisual();
    }
    private int[]? CaptureWholeLineSelection() => CurrentWholeLineSelection()?.SourceLineIndexes.ToArray();
    private void RestoreWholeLineSelection(IEnumerable<int> sourceLineIndexes)
    {
        if (projection is null) return;
        int[] lines = sourceLineIndexes.Select(index => projection.FindDisplayLine(index + 1)).Where(line => line.HasValue)
            .Select(line => line!.Value).Where(line => projection.AtDisplayLine(line) is { } row
                && (ActiveScope is null || ActiveScope.Value.Contains(row.OriginalLineNumber - 1))).Distinct().Order().ToArray();
        PublishWholeLineSelection(lines, Editor.TextArea.Caret.Line, false, true);
    }
    private void PublishWholeLineSelection(IEnumerable<int> displayLines, int caretLine, bool focus, bool preserveCaret = false)
    {
        if (projection is null) return;
        int[] selected = displayLines.Distinct().Order().ToArray();
        publishingLineSelection = true;
        try
        {
            if (!preserveCaret)
            {
                int target = selected.Length == 0 || selected.Contains(caretLine) ? caretLine : selected[0];
                Editor.TextArea.Caret.Offset = Editor.Document.GetLineByNumber(target).Offset;
            }
            Editor.TextArea.Selection = selected.Length == 0 ? Selection.Create(Editor.TextArea, Editor.TextArea.Caret.Offset, Editor.TextArea.Caret.Offset)
                : new WholeLineSelection(Editor.TextArea, projection, selected);
        }
        finally { publishingLineSelection = false; }
        RefreshLineSelectionIndicator();
        if (focus) Editor.Focus();
    }
    private void RefreshLineSelectionIndicator()
    {
        if (Editor.TextArea.Selection is WholeLineSelection stale && (projection is null || !stale.IsFor(projection, Editor.Document)))
        {
            Editor.TextArea.ClearSelection();
            return;
        }
        margin.SelectedLineIndexes = CurrentWholeLineSelection()?.SourceLineIndexes.ToHashSet() ?? new HashSet<int>();
        margin.InvalidateVisual();
    }
}
