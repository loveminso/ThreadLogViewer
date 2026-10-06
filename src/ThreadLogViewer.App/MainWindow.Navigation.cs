using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Rendering;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private readonly BookmarkState bookmarks = new();
    private readonly LatestOperation selectionWork = new();
    private bool restoringPosition, contextActive;
    private long viewVersion;
    private PositionAnchor? emptyAnchor, normalAnchor;
    private EntryFilter appliedFilter = EntryFilter.Empty;
    private bool positionMovedToNearest;
    private IReadOnlySet<int?>? normalSelectedThreads;
    private sealed record PositionAnchor(int SourceLine, int Column, int TopSourceLine, double TopDelta, double Horizontal);

    private int? CurrentSourceLine() => projection?.AtDisplayLine(Editor.TextArea.Caret.Line)?.OriginalLineNumber - 1;
    private PositionAnchor? CapturePosition()
    {
        if (projection is null) return null;
        if (projection.Count == 0) return emptyAnchor;
        int caret = CurrentSourceLine() ?? projection.SourceIndexes[^1];
        var textView = Editor.TextArea.TextView;
        // Capture the actual offset without forcing layout, which may consume a pending
        // scroll request and change the viewport while the snapshot is being taken.
        double vertical = textView.VerticalOffset;
        int topLine = Math.Min(textView.GetDocumentLineByVisualTop(vertical).LineNumber, projection.Count);
        int topSource = projection.AtDisplayLine(topLine)?.OriginalLineNumber - 1 ?? caret;
        return new(caret, Editor.TextArea.Caret.Column, topSource,
            vertical - textView.GetVisualTopByDocumentLine(topLine), textView.HorizontalOffset);
    }
    private void RestorePosition(PositionAnchor anchor)
    {
        positionMovedToNearest = false;
        if (projection is null) return;
        if (projection.Count == 0) { emptyAnchor = anchor; return; }
        emptyAnchor = null;
        int line = projection.FindDisplayLine(anchor.SourceLine + 1, true) ?? 1;
        positionMovedToNearest = projection.AtDisplayLine(line)?.OriginalLineNumber != anchor.SourceLine + 1;
        var documentLine = Editor.Document.GetLineByNumber(line);
        int column = Math.Clamp(anchor.Column, 1, documentLine.Length + 1);
        Editor.TextArea.Caret.Offset = documentLine.Offset + column - 1;
        Editor.ScrollTo(line, column);
        int top = projection.FindDisplayLine(anchor.TopSourceLine + 1, true) ?? line;
        Editor.ScrollToVerticalOffset(Editor.TextArea.TextView.GetVisualTopByDocumentLine(top) + anchor.TopDelta);
        Editor.ScrollToHorizontalOffset(anchor.Horizontal);
    }
    private async void UpdatePosition()
    {
        if (!viewReady || restoringPosition || projection is null) return;
        UpdateAnalysisInputState();
        var sourceLine = CurrentSourceLine();
        string position = sourceLine is null ? "원본 줄 없음" : $"원본 {sourceLine + 1:N0}줄 · {Editor.TextArea.Caret.Column:N0}열";
        var segments = Editor.TextArea.Selection.Segments.Select(s => new SelectionSpan(s.StartOffset, s.Length)).ToArray();
        var op = selectionWork.Begin();
        if (segments.All(s => s.Length == 0)) { PositionStatus.Text = position + " · 읽기 전용"; return; }
        PositionStatus.Text = position + " · 선택 계산 중…";
        var captured = projection;
        try
        {
            await Task.Delay(100, op.Token);
            var count = await Task.Run(() => SelectionMetrics.Compute(captured.Text, captured.DisplayOffsets, captured.Count, segments, op.Token), op.Token);
            if (selectionWork.IsCurrent(op.Version) && projection == captured)
                PositionStatus.Text = $"{position} · 선택 {count.LineCount:N0}줄 / {count.ScalarCount:N0}자";
        }
        catch (OperationCanceledException) { }
        catch (OutOfMemoryException) { if (selectionWork.IsCurrent(op.Version)) PositionStatus.Text = position + " · 선택 계산 메모리 부족"; }
    }
    private void GoTo_Click(object sender, RoutedEventArgs e)
    {
        HiddenContextButton.Visibility = Visibility.Collapsed; hiddenTargetLine = null;
        GoToPanel.Visibility = Visibility.Visible;
        GoToBox.Text = ((CurrentSourceLine() ?? 0) + 1).ToString();
        GoToBox.Focus(); GoToBox.SelectAll(); GoToStatus.Text = "";
    }
    private void CloseGoTo_Click(object sender, RoutedEventArgs e) { GoToPanel.Visibility = Visibility.Collapsed; Editor.Focus(); }
    private void GoTo_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { GoToSubmit_Click(sender, new()); e.Handled = true; } }
    private void GoToSubmit_Click(object sender, RoutedEventArgs e)
    {
        HiddenContextButton.Visibility = Visibility.Collapsed; hiddenTargetLine = null;
        if (data is null || !int.TryParse(GoToBox.Text, out int line) || line < 1 || line > data.Lines.Count)
        { GoToStatus.Text = $"1~{data?.Lines.Count ?? 0:N0}의 원본 줄 번호를 입력하세요."; return; }
        if (ActiveScope is { } scope && !scope.Contains(line - 1))
        { GoToStatus.Text = $"이 세션의 원본 {scope.FirstLineIndex + 1:N0}~{scope.LastLineIndex + 1:N0}줄 안에서 이동할 수 있습니다."; return; }
        if (!NavigateVisible(line - 1))
        {
            hiddenTargetLine = line - 1;
            GoToStatus.Text = "필터로 숨겨진 줄입니다. ‘숨긴 줄 문맥’ 버튼으로 확인하세요.";
            HiddenContextButton.Visibility = Visibility.Visible;
            return;
        }
        GoToStatus.Text = $"원본 {line:N0}줄로 이동";
        HiddenContextButton.Visibility = Visibility.Collapsed;
    }
    private int? hiddenTargetLine;
    private async void HiddenContext_Click(object sender, RoutedEventArgs e) { if (hiddenTargetLine is { } line) await ShowContextAsync(line); }
    private bool NavigateVisible(int sourceLineIndex, int column = 1, int length = 0)
    {
        int? line = projection?.FindDisplayLine(sourceLineIndex + 1);
        if (line is null) return false;
        using var navigation = BeginNavigation();
        var row = Editor.Document.GetLineByNumber(line.Value);
        int offset = row.Offset + Math.Clamp(column - 1, 0, row.Length);
        Editor.Select(offset, Math.Clamp(length, 0, Editor.Document.TextLength - offset));
        Editor.ScrollTo(line.Value, column); Editor.Focus();
        return true;
    }
    private async Task NavigateOriginalAsync(int sourceLine, int column = 1, int length = 0)
    {
        if (ActiveScope is { } scope && !scope.Contains(sourceLine)) return;
        using var navigation = BeginNavigation();
        var source = data;
        if (NavigateVisible(sourceLine, column, length)) return;
        if (await ShowContextAsync(sourceLine) && source == data) NavigateVisible(sourceLine, column, length);
    }
    private void BookmarkToggle_Click(object sender, RoutedEventArgs e)
    {
        if (LineActionSourceLine() is not { } line) return;
        bookmarks.Toggle(line); RefreshBookmarks(); BookmarkPanel.IsExpanded = true;
    }
    private sealed record BookmarkRow(Bookmark Bookmark, bool Hidden) { public string Label => $"{(Hidden ? "[숨김 · 문맥에서 열기] " : "")}{Bookmark.SourceLineIndex + 1:N0}줄{(Bookmark.Label.Length == 0 ? "" : " · " + Bookmark.Label)}"; }
    private void RefreshBookmarks()
    {
        BookmarkList.ItemsSource = bookmarks.Items.Select(b => new BookmarkRow(b, projection?.FindDisplayLine(b.SourceLineIndex + 1) is null)).ToArray();
        margin.Bookmarks = bookmarks.Items.Select(b => b.SourceLineIndex).ToHashSet(); margin.InvalidateVisual();
    }
    private async void Bookmark_Selected(object sender, SelectionChangedEventArgs e)
    { if (BookmarkList.SelectedItem is BookmarkRow row) await NavigateOriginalAsync(row.Bookmark.SourceLineIndex); }
    private void BookmarkRename_Click(object sender, RoutedEventArgs e)
    {
        int? line = lineActionMenuOpen || lineActionPointerPending ? LineActionSourceLine() : (BookmarkList.SelectedItem as BookmarkRow)?.Bookmark.SourceLineIndex ?? CurrentSourceLine();
        if (line is null) return;
        var current = bookmarks.Items.FirstOrDefault(b => b.SourceLineIndex == line);
        var source = data;
        string? label = NamePrompt.Ask(this, $"원본 {line + 1:N0}줄 북마크 이름", current?.Label ?? "");
        if (label is null || source != data) return;
        if (current is null) bookmarks.Toggle(line.Value, label); else bookmarks.SetLabel(line.Value, label);
        RefreshBookmarks();
    }
    private async Task NavigateBookmarkAsync(bool backwards)
    {
        int line = CurrentSourceLine() ?? -1;
        var bookmark = backwards ? bookmarks.Previous(line) : bookmarks.Next(line);
        if (bookmark is not null) await NavigateOriginalAsync(bookmark.SourceLineIndex);
    }
    private EntryFilter ReadDraftFilter() => new(Conditions(IncludeBox.Text), Conditions(ExcludeBox.Text),
        IncludeModeBox.SelectedIndex == 1, FilterCaseBox.IsChecked == true);
    private async void ApplyTextFilter_Click(object sender, RoutedEventArgs e)
    {
        if (data is null) { FilterDirtyStatus.Text = "로그를 연 뒤 필터를 적용하세요."; return; }
        await FilterAsync(ReadDraftFilter());
    }
    private static string[] Conditions(string text) => text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
    private async void ResetTextFilter_Click(object sender, RoutedEventArgs e)
    {
        IncludeBox.Clear(); ExcludeBox.Clear(); IncludeModeBox.SelectedIndex = 0; FilterCaseBox.IsChecked = false;
        await FilterAsync(EntryFilter.Empty);
        UpdateFilterDraftStatus();
    }
    private void Criteria_Changed(object sender, TextChangedEventArgs e) => UpdateFilterDraftStatus();
    private void FilterOption_Changed(object sender, RoutedEventArgs e) => UpdateFilterDraftStatus();
    private void UpdateFilterDraftStatus()
    {
        if (!viewReady || FilterDirtyStatus is null) return;
        var draft = ReadDraftFilter();
        bool pending = draft.RequireAll != appliedFilter.RequireAll || draft.MatchCase != appliedFilter.MatchCase ||
            !draft.Includes.SequenceEqual(appliedFilter.Includes, StringComparer.Ordinal) ||
            !draft.Excludes.SequenceEqual(appliedFilter.Excludes, StringComparer.Ordinal);
        FilterDirtyStatus.Text = pending ? "변경 사항 미적용 · ‘필터 적용’을 누르세요." : "입력한 조건과 현재 적용 조건이 같습니다.";
        ContentFilterHeader.Text = "내용 필터" + (HasContentFilter ? " · 적용 중" : " · 없음") + (pending ? " · 미적용 변경" : "");
    }
    private void UpdateFilterSummary()
    {
        var details = new List<string>();
        if (appliedFilter.Includes.Length > 0)
            details.Add($"포함 {appliedFilter.Includes.Length}개 중 {(appliedFilter.RequireAll ? "모두 포함" : "하나라도 포함")}");
        if (appliedFilter.Excludes.Length > 0) details.Add($"제외 {appliedFilter.Excludes.Length}개 중 하나라도 일치하면 숨김");
        if (details.Count > 0 && appliedFilter.MatchCase) details.Add("대소문자 구분");
        FilterSummary.Text = details.Count == 0 ? "현재 적용: 내용 필터 없음" : "현재 적용: " + string.Join(" · ", details);
        UpdateFilterDraftStatus();
        UpdateEmptyResultState();
    }
    private async void Context_Click(object sender, RoutedEventArgs e)
    {
        if (LineActionSourceLine() is { } line) await ShowContextAsync(line);
        else ContextInputStatus.Text = "로그를 연 뒤 본문에서 기준 기록을 선택하세요.";
    }
    private async void ContextRadius_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } || !int.TryParse(value, out int radius)) return;
        ContextRadiusBox.Text = radius.ToString();
        if (LineActionSourceLine() is { } line) await ShowContextAsync(line);
    }
    private async void ContextRadiusCustom_Click(object sender, RoutedEventArgs e)
    {
        if (!IsVisible || busy || LineActionSourceLine() is not { } line) return;
        var session = activeSession;
        string? value = NamePrompt.Ask(this, "앞뒤 각각 표시할 기록 수 (0~10,000)", ContextRadiusBox.Text);
        if (value is null || session != activeSession) return;
        if (!int.TryParse(value, out int radius) || radius < 0 || radius > 10000)
        { ShowError("앞뒤 기록 수를 확인하세요", new ArgumentException("0~10,000 사이의 정수를 입력하세요.")); return; }
        ContextRadiusBox.Text = radius.ToString();
        await ShowContextAsync(line);
    }
    private async Task<bool> ShowContextAsync(int sourceLine)
    {
        if (data is null || sourceLine < 0 || sourceLine >= data.Lines.Count) return false;
        if (ActiveScope is { } scope && !scope.Contains(sourceLine)) return false;
        using var navigation = BeginNavigation();
        if (!int.TryParse(ContextRadiusBox.Text, out int radius) || radius < 0 || radius > 10000)
        { ContextInputStatus.Text = "앞뒤 기록 수는 0~10,000 사이로 입력하세요."; ShowAnalysisTool(0); return false; }
        var captured = data;
        var capturedScope = ActiveScope;
        var op = BeginWork("주변 로그 준비…", true);
        try
        {
            var result = await Task.Run(() =>
            {
                var view = LogContext.Create(captured, sourceLine, radius, op.Token, capturedScope);
                return (view, document: PrepareDocument(view.Text, op.Token, op.Progress));
            }, op.Token);
            op.Token.ThrowIfCancellationRequested();
            if (!work.IsCurrent(op.Version) || captured != data) return false;
            if (!contextActive) { normalAnchor = CapturePosition(); normalSelectedThreads = threadItems.Where(t => t.IsSelected).Select(t => t.Id).ToHashSet(); }
            contextActive = true;
            PublishView(result.view, result.document);
            int centerEntry = captured.Lines[sourceLine].EntryIndex;
            int headerLine = captured.Entries[centerEntry].StartLineIndex;
            int preceding = result.view.EntryIndexes.Count(i => i < centerEntry);
            int following = result.view.EntryIndexes.Count(i => i > centerEntry);
            margin.ContextLineIndex = Math.Max(headerLine, capturedScope?.FirstLineIndex ?? 0);
            threadRenderer.ContextEntryIndex = centerEntry;
            ContextPanel.Visibility = Visibility.Visible;
            ContextStatus.Text = $"주변 로그 보기 중 · 기준: 원본 {sourceLine + 1:N0}줄 · 앞 {preceding:N0}기록 / 뒤 {following:N0}기록 · 모든 스레드";
            ContextStatus.ToolTip = $"기준 기록의 첫 줄: 원본 {headerLine + 1:N0}줄. 스레드와 내용 필터를 잠시 해제하고 파일 순서대로 표시합니다. 한 기록은 헤더와 그 아래 본문을 묶은 단위입니다.";
            ContextInputStatus.Text = $"원본 {sourceLine + 1:N0}줄을 기준으로 표시했습니다. 다른 줄을 선택해 다시 볼 수 있습니다.";
            UpdateExportLabel(true);
            NavigateVisible(sourceLine); margin.InvalidateVisual();
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException) { if (work.IsCurrent(op.Version)) ShowError("주변 로그 보기 실패", ex); return false; }
        finally { FinishWork(op.Version); }
    }
    private async void ReturnContext_Click(object sender, RoutedEventArgs e)
    {
        await FilterAsync(returnAnchor: normalAnchor);
        if (!contextActive) { margin.ContextLineIndex = null; UpdateExportLabel(false); margin.InvalidateVisual(); }
    }
}
