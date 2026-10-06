using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private sealed record LineActionTarget(LogData Source, LogProjection View, int SourceLine, int Column);
    private LineActionTarget? lineActionTarget;
    private bool lineActionMenuOpen, lineActionPointerPending, lineActionInvalidated, lineActionRightButtonHeld, lineActionMenuClosing;
    private long lineActionVersion;
    private string? lineActionHighlightSeed;
    private Selection? lineActionSelection;
    private Func<IReadOnlyList<HighlightRuleDraft>, string, IReadOnlyList<HighlightRuleDraft>?>? highlightPromptOverride = null;

    private bool IsLineActionTargetCurrent() => lineActionTarget is { } target && target.Source == data
        && target.View == projection && target.Source == projection?.Source && target.SourceLine >= 0
        && target.SourceLine < target.Source.Lines.Count && projection?.FindDisplayLine(target.SourceLine + 1) is not null
        && (ActiveScope is null || ActiveScope.Value.Contains(target.SourceLine));

    private int? LineActionSourceLine()
    {
        UpdateLineActionIndicator();
        if (lineActionMenuOpen || lineActionPointerPending || lineActionInvalidated)
            return !lineActionInvalidated && IsLineActionTargetCurrent() ? lineActionTarget!.SourceLine : null;
        if (projection is null || projection.Source != data || CurrentSourceLine() is not { } line) return null;
        return ActiveScope is null || ActiveScope.Value.Contains(line) ? line : null;
    }

    private bool CaptureLineActionTarget(int displayLine, int column = 1)
    {
        lineActionVersion++;
        lineActionPointerPending = true; lineActionInvalidated = lineActionMenuClosing = false;
        lineActionTarget = null; lineActionHighlightSeed = null; lineActionSelection = null;
        if (data is null || projection is null || projection.Source != data || projection.AtDisplayLine(displayLine) is not { } row)
        { lineActionInvalidated = true; UpdateLineActionIndicator(); return false; }
        int sourceLine = row.OriginalLineNumber - 1;
        if (ActiveScope is { } scope && !scope.Contains(sourceLine)) { lineActionInvalidated = true; UpdateLineActionIndicator(); return false; }
        var documentLine = Editor.Document.GetLineByNumber(displayLine);
        int offset = documentLine.Offset + Math.Clamp(column - 1, 0, documentLine.Length);
        bool insideSelection = Editor.TextArea.Selection.Segments.Any(segment => segment.Length > 0 && offset >= segment.StartOffset && offset < segment.EndOffset);
        string selected = SelectedHighlightSeed(offset);
        lineActionTarget = new(data, projection, sourceLine, offset - documentLine.Offset + 1);
        lineActionSelection = insideSelection ? Editor.TextArea.Selection : null;
        if (!insideSelection) Editor.Select(offset, 0);
        Editor.TextArea.Caret.Offset = offset;
        lineActionHighlightSeed = selected.Length > 0 ? selected : WordHighlightSeed(offset);
        UpdateLineActionIndicator();
        return true;
    }

    private void SelectWholeDisplayLine(int displayLine)
    {
        SelectGutterDisplayLine(displayLine);
    }

    private void UpdateLineActionIndicator()
    {
        int? target = !lineActionMenuClosing && !lineActionInvalidated && (lineActionMenuOpen || lineActionPointerPending)
            && IsLineActionTargetCurrent() ? lineActionTarget!.SourceLine : null;
        if (threadRenderer.ActionLineIndex == target && margin.ActionLineIndex == target) return;
        threadRenderer.ActionLineIndex = margin.ActionLineIndex = target;
        margin.InvalidateVisual();
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void Editor_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        lineActionRightButtonHeld = true; lineActionMenuOpen = false;
        CaptureLineActionTargetAtPoint(e.GetPosition(Editor));
        ScheduleUnusedLineActionTargetCleanup();
    }
    private void Editor_RightButtonUp(object sender, MouseButtonEventArgs e)
    {
        lineActionRightButtonHeld = false;
        ScheduleUnusedLineActionTargetCleanup();
    }
    private void Editor_LeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ReleaseUnusedLineActionCapture();
        HandleLineSelectionMouseDown(e);
    }
    private void ReleaseUnusedLineActionCaptureForKeyboard(bool rightButtonReleased)
    { if (rightButtonReleased) ReleaseUnusedLineActionCapture(); }
    private void ReleaseUnusedLineActionCapture()
    {
        if (lineActionMenuOpen || Editor.ContextMenu?.IsOpen == true) return;
        ResetLineActionCapture();
    }
    private void ScheduleUnusedLineActionTargetCleanup()
    {
        long version = lineActionVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (version != lineActionVersion || lineActionRightButtonHeld) return;
            ReleaseUnusedLineActionCapture();
        }));
    }
    private void ResetLineActionCapture()
    {
        lineActionMenuOpen = lineActionPointerPending = lineActionInvalidated = lineActionRightButtonHeld = lineActionMenuClosing = false;
        lineActionTarget = null; lineActionHighlightSeed = null; lineActionSelection = null;
        UpdateLineActionIndicator();
    }

    private bool CaptureLineActionTargetAtPoint(Point editorPoint)
    {
        var textView = Editor.TextArea.TextView;
        var point = Editor.TranslatePoint(editorPoint, textView);
        if (editorPoint.X >= 0 && editorPoint.X < Editor.ActualWidth && point.X < textView.ActualWidth && point.Y >= 0 && point.Y < textView.ActualHeight)
        {
            textView.EnsureVisualLines();
            double documentY = point.Y + textView.VerticalOffset;
            var visual = textView.VisualLines.FirstOrDefault(line => documentY >= line.VisualTop && documentY < line.VisualTop + line.Height);
            if (visual is not null)
            {
                // Determine the row before asking AvalonEdit for a column: it may clamp
                // blank space beneath the document to the final document position.
                double documentX = point.X < 0 ? 0 : point.X + textView.HorizontalOffset;
                var position = textView.GetPositionFloor(new Point(documentX, documentY));
                int line = position?.Line ?? visual.FirstDocumentLine.LineNumber;
                int column = point.X < 0 ? 1 : position?.Column ?? 1;
                return CaptureLineActionTarget(line, column);
            }
        }
        lineActionVersion++;
        ClearLineActionTarget(); lineActionPointerPending = true; lineActionInvalidated = true;
        UpdateLineActionIndicator();
        return false;
    }

    private void LineContext_Opened(object sender, RoutedEventArgs e)
    {
        lineActionMenuClosing = false;
        if (!lineActionPointerPending)
        {
            int line = Editor.TextArea.Caret.Line, column = Editor.TextArea.Caret.Column;
            CaptureLineActionTarget(line, column);
        }
        lineActionMenuOpen = true; lineActionPointerPending = false;
        UpdateLineActionIndicator();
        if (IsLineActionTargetCurrent() && !lineActionInvalidated)
        {
            int display = projection!.FindDisplayLine(lineActionTarget!.SourceLine + 1)!.Value;
            var row = Editor.Document.GetLineByNumber(display);
            Editor.TextArea.Caret.Offset = row.Offset + Math.Clamp(lineActionTarget.Column - 1, 0, row.Length);
            if (lineActionSelection is not null) Editor.TextArea.Selection = lineActionSelection;
        }
        if (sender is not ContextMenu menu) return;
        int? sourceLine = LineActionSourceLine();
        var point = data is not null && sourceLine is { } lineIndex ? LogTimeAnalysis.ResolveAnchor(data, lineIndex) : null;
        foreach (var item in LineMenuItems(menu))
        {
            switch (item.Tag as string)
            {
                case "line-title": item.Header = sourceLine is { } line ? $"작업 대상 · 원본 {line + 1:N0}줄" : "로그의 줄을 우클릭하세요"; item.IsEnabled = false; break;
                case "line": item.IsEnabled = sourceLine is not null && !busy; break;
                case "split-end":
                    item.IsEnabled = sourceLine is not null && separationStartLine is not null && !busy;
                    item.Header = separationStartLine is { } beginning ? $"시작 {beginning + 1:N0}줄~여기까지 새 탭으로 분리" : "여기까지 새 탭으로 분리";
                    break;
                case "split-clear": item.IsEnabled = separationStartLine is not null; break;
                case "time": item.IsEnabled = point is not null && !busy; break;
                case "time-compare": item.IsEnabled = point is not null && timeA is not null && !busy; break;
                case "anchor-a": item.IsEnabled = timeA is not null && !busy; break;
                case "anchor-b": item.IsEnabled = timeB is not null && !busy; break;
                case "time-pair": item.IsEnabled = timeA is not null && timeB is not null; break;
                case "time-clear": item.IsEnabled = timeA is not null || timeB is not null; break;
                case "highlight":
                case "highlight-manager": item.IsEnabled = sourceLine is not null && !busy; break;
            }
        }
    }

    private static IEnumerable<MenuItem> LineMenuItems(ItemsControl parent)
    {
        foreach (var item in parent.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in LineMenuItems(item)) yield return child;
        }
    }

    private void LineContext_Closed(object sender, RoutedEventArgs e)
    {
        lineActionMenuClosing = true;
        UpdateLineActionIndicator();
        long version = lineActionVersion;
        // Menu clicks and the closed notification can share the same dispatcher turn.
        // Retain the captured identity until the click has finished, then release it.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (version != lineActionVersion) return;
            ResetLineActionCapture();
        }));
    }

    private void ClearLineActionTarget()
    {
        lineActionTarget = null; lineActionHighlightSeed = null; lineActionSelection = null;
        if (lineActionMenuOpen || lineActionPointerPending) lineActionInvalidated = true;
        else { lineActionPointerPending = false; lineActionInvalidated = false; }
        UpdateLineActionIndicator();
    }

    private void SplitStart_Click(object sender, RoutedEventArgs e)
    {
        if (LineActionSourceLine() is not { } line) { OperationStatus.Text = "줄 대상이 변경되었습니다. 로그의 시작 줄을 다시 우클릭하세요."; return; }
        separationStartLine = line;
        UpdateSplitStatus();
        OperationStatus.Text = $"구간 시작: 원본 {line + 1:N0}줄 · 마지막 줄을 우클릭하여 ‘여기까지 새 탭으로 분리’를 선택하세요.";
    }
    private async void SplitEnd_Click(object sender, RoutedEventArgs e)
    {
        if (separationStartLine is not { } start || LineActionSourceLine() is not { } end)
        { OperationStatus.Text = "먼저 시작 줄을 지정하고 마지막 줄을 우클릭하세요."; return; }
        int first = Math.Min(start, end), last = Math.Max(start, end);
        if (ActiveScope is { } scope && (!scope.Contains(first) || !scope.Contains(last)))
        { OperationStatus.Text = "현재 탭 범위 안의 시작과 끝 줄을 지정하세요."; return; }
        if (await OpenLineSessionAsync(first, last))
            OperationStatus.Text = $"원본 {first + 1:N0}~{last + 1:N0}줄을 새 탭에 표시했습니다." + (start > end ? " · 시작·끝 순서를 바꿔 범위를 구성했습니다." : "");
    }
    private void SplitClear_Click(object sender, RoutedEventArgs e)
    { separationStartLine = null; UpdateSplitStatus(); OperationStatus.Text = "구간 시작 지정을 해제했습니다."; }
    private void UpdateSplitStatus()
    {
        if (SplitStatusPanel is null || SplitSummary is null) return;
        SplitStatusPanel.Visibility = separationStartLine is null ? Visibility.Collapsed : Visibility.Visible;
        SplitSummary.Text = separationStartLine is { } line ? $"구간 시작: 원본 {line + 1:N0}줄 · 마지막 줄을 우클릭하여 여기까지 새 탭으로 분리" : "";
    }
    private void HighlightSelection_Click(object sender, RoutedEventArgs e) => ShowHighlightPrompt();

    private string SelectedHighlightSeed(int? clickedOffset = null)
    {
        int offset = clickedOffset ?? Editor.TextArea.Caret.Offset;
        foreach (var segment in Editor.TextArea.Selection.Segments)
        {
            bool inside = offset >= segment.StartOffset && (clickedOffset is null ? offset <= segment.EndOffset : offset < segment.EndOffset);
            if (inside && segment.Length is > 0 and <= 4096) return Editor.Document.GetText(segment.StartOffset, segment.Length);
        }
        return "";
    }
    private string WordHighlightSeed(int offset) => ExtractHighlightWord(projection?.Text ?? "", offset);
    private void ShowHighlightPrompt()
    {
        if (data is null || projection is null || projection.Source != data || busy) return;
        if ((lineActionMenuOpen || lineActionPointerPending || lineActionInvalidated) && LineActionSourceLine() is null) return;
        var source = data;
        var sourceView = projection; var session = activeSession;
        string seed = lineActionMenuOpen || lineActionPointerPending ? lineActionHighlightSeed ?? "" : SelectedHighlightSeed();
        if (seed.Length == 0) seed = WordHighlightSeed(Editor.TextArea.Caret.Offset);
        var before = Array.AsReadOnly(keywordRules.Select(rule => new HighlightRuleDraft(rule.Keyword, rule.ColorIndex, rule.Enabled)).ToArray());
        var result = highlightPromptOverride is not null ? highlightPromptOverride(before, seed) : HighlightPrompt.Ask(this, before, seed);
        if (source != data || sourceView != projection || session != activeSession || result is null) return;
        ApplyHighlightRules(result);
    }
    private bool ApplyHighlightRules(IReadOnlyList<HighlightRuleDraft> rules)
    {
        if (!HighlightPrompt.IsValid(rules))
        {
            OperationStatus.Text = rules.Count > HighlightPrompt.MaximumRules
                ? "강조 문구는 최대 8개입니다. ‘강조 문구 관리’에서 기존 문구를 수정하거나 삭제하세요."
                : rules.Select(rule => rule is null ? "강조 규칙이 유효하지 않습니다. ‘강조 문구 관리’에서 다시 등록하세요." : HighlightPrompt.PhraseError(rule.Phrase)).FirstOrDefault(error => error is not null)
                    ?? "강조 색이 유효하지 않습니다. ‘강조 문구 관리’에서 각 규칙의 색을 다시 선택하세요.";
            return false;
        }
        foreach (var item in keywordRules) item.PropertyChanged -= Keyword_Changed;
        keywordRules.Clear();
        foreach (var draft in rules)
        {
            var item = new KeywordRuleItem(draft.Phrase, draft.ColorIndex) { Enabled = draft.Enabled };
            item.PropertyChanged += Keyword_Changed; keywordRules.Add(item);
        }
        _ = RefreshKeywordsAsync();
        OperationStatus.Text = $"강조 문구 {rules.Count}/8개를 적용했습니다. 같은 문구에서 Shift+F8을 누르면 강조를 해제합니다.";
        return true;
    }
}
