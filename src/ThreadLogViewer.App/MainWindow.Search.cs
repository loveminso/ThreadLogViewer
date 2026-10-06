using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Rendering;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private LocatedSearchHit[] searchHits = [];
    private IReadOnlyList<SourceTextRange>? fixedSearchRanges;
    private bool selectingResult;
    private LocatedSearchHit? lastSearchLocation;
    private sealed record ResultRow(LogData Source, LocatedSearchHit Hit, bool Hidden)
    {
        public string Label
        {
            get
            {
                var entry = Source.Entries[Hit.EntryIndex];
                var header = Source.Lines[entry.StartLineIndex];
                var raw = Source.Lines[Hit.SourceLineIndex].RawText;
                int start = Math.Max(0, Hit.SourceColumn - 1 - 35);
                string preview = raw.Slice(Math.Min(start, raw.Length), Math.Min(150, Math.Max(0, raw.Length - start))).ToString();
                return $"{(Hidden ? "[숨김 · 문맥에서 열기] " : "")}{Hit.SourceLineIndex + 1:N0}줄:{Hit.SourceColumn} · {header.TimestampText.ToString()} · {(entry.ThreadId is { } id ? "스레드 " + id : "미분류")} · {preview}";
            }
        }
    }
    private void ShowSearch()
    {
        SearchBar.Visibility = Visibility.Visible; SearchBox.Focus(); SearchBox.SelectAll(); _ = SearchAsync();
    }
    private void Search_Click(object sender, RoutedEventArgs e) => ShowSearch();
    private void CloseSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBar.Visibility = ResultsPanel.Visibility = Visibility.Collapsed;
        searchWork.Cancel(); searchHits = []; ResultsList.ItemsSource = null;
        searchRenderer.Index = HighlightIndex.Empty;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background); Editor.Focus();
    }
    private async void Search_Changed(object sender, TextChangedEventArgs e) { if (viewReady) { lastSearchLocation = null; await SearchAsync(); } }
    private async void SearchOption_Changed(object sender, RoutedEventArgs e) { if (viewReady) { lastSearchLocation = null; await SearchAsync(); } }
    private async void SearchScope_Changed(object sender, SelectionChangedEventArgs e) { if (viewReady) { lastSearchLocation = null; await SearchAsync(); } }
    private async void CaptureSearchSelection_Click(object sender, RoutedEventArgs e)
    {
        if (projection is null) return;
        var spans = Editor.TextArea.Selection.Segments.Select(s => new SelectionSpan(s.StartOffset, s.Length));
        fixedSearchRanges = SelectionSourceRanges.Map(projection, spans);
        SearchScopeBox.SelectedIndex = 2;
        await SearchAsync();
    }
    private async Task SearchAsync()
    {
        if (!viewReady || SearchBox is null || SearchStatus is null) return;
        var op = searchWork.Begin();
        searchRenderer.Index = HighlightIndex.Empty; searchIndex = -1; searchHits = [];
        ResultsList.ItemsSource = null;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        string query = SearchBox.Text;
        var captured = projection;
        if (captured is null || query.Length == 0 || SearchBar.Visibility != Visibility.Visible)
        { SearchStatus.Text = ""; ResultsPanel.Visibility = Visibility.Collapsed; return; }
        int scope = SearchScopeBox.SelectedIndex;
        var capturedScope = ActiveScope;
        IReadOnlyList<SourceTextRange>? ranges = scope == 2 ? fixedSearchRanges ?? [] : null;
        if (capturedScope is not null)
        {
            var limit = LogSlice.GetTextRange(captured.Source, capturedScope);
            ranges = ranges is null ? [limit] : ranges.Select(r =>
            {
                int first = Math.Max(r.Offset, limit.Offset);
                int last = Math.Min(r.Offset + r.Length, limit.Offset + limit.Length);
                return new SourceTextRange(first, Math.Max(0, last - first));
            }).Where(r => r.Length > 0).ToArray();
        }
        SearchScopeStatus.Text = scope switch { 1 => capturedScope is null ? "전체 원본 기록" : "전체 세션 기록", 2 => ranges!.Count == 0 ? "선택 범위를 지정하세요" : $"고정 선택 {ranges.Count:N0}구간", _ => contextActive ? "현재 문맥 기록" : "현재 표시 기록" };
        var options = new SearchOptions(SearchCaseBox.IsChecked == true, SearchWordBox.IsChecked == true, SearchRegexBox.IsChecked == true);
        int firstEntry = capturedScope is { } entryScope ? captured.Source.Lines[entryScope.FirstLineIndex].EntryIndex : 0;
        int lastEntry = capturedScope is { } lastScope ? captured.Source.Lines[lastScope.LastLineIndex].EntryIndex : captured.Source.Entries.Count - 1;
        IEnumerable<int> entries = scope == 0 ? captured.EntryIndexes : Enumerable.Range(firstEntry, lastEntry - firstEntry + 1);
        SearchStatus.Text = "검색 중…";
        try
        {
            await Task.Delay(180, op.Token);
            var found = await Task.Run(() => LogSearch.Find(captured.Source, entries, query, options, ranges, op.Token), op.Token);
            if (!searchWork.IsCurrent(op.Version) || op.Token.IsCancellationRequested || captured != projection) return;
            searchHits = capturedScope is { } limitScope ? found.Hits.Where(h => limitScope.Contains(h.SourceLineIndex)).ToArray() : found.Hits; searchLimited = found.Limited;
            searchRenderer.Index = new HighlightIndex(searchHits.Where(h => h.Length > 0).Select(h =>
            {
                int? offset = captured.GetDisplayOffset(h.SourceOffset);
                return offset is null ? default : new HighlightSpan(offset.Value, h.Length, 0);
            }));
            ResultsList.ItemsSource = searchHits.Select(h => new ResultRow(captured.Source, h, captured.FindDisplayLine(h.SourceLineIndex + 1) is null)).ToArray();
            if (lastSearchLocation is { } previous)
            {
                searchIndex = Array.IndexOf(searchHits, previous);
                selectingResult = true; ResultsList.SelectedIndex = searchIndex; selectingResult = false;
            }
            ResultsPanel.Visibility = searchHits.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            SearchStatus.Text = searchHits.Length == 0 ? "결과 없음" : $"{searchHits.Length:N0}개{(found.Limited ? " (첫 100,000개)" : "")}";
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
        catch (OperationCanceledException) { }
        catch (RegexMatchTimeoutException) { if (searchWork.IsCurrent(op.Version)) SearchStatus.Text = OperationStatus.Text = "정규식 시간 제한 · 패턴을 좁히거나 정규식 옵션을 끄고 다시 검색하세요."; }
        catch (ArgumentException) { if (searchWork.IsCurrent(op.Version)) SearchStatus.Text = OperationStatus.Text = "올바르지 않은 정규식입니다. 패턴을 수정하거나 정규식 옵션을 끄세요."; }
        catch (OutOfMemoryException) { if (searchWork.IsCurrent(op.Version)) SearchStatus.Text = OperationStatus.Text = "검색 메모리 부족 · 검색어를 좁히거나 다른 탭을 닫고 다시 검색하세요."; }
    }
    private async Task NavigateSearchAsync(bool backwards)
    {
        var hits = searchHits;
        if (hits.Length == 0) return;
        searchIndex = searchIndex < 0 ? (backwards ? hits.Length - 1 : 0) : (searchIndex + (backwards ? -1 : 1) + hits.Length) % hits.Length;
        int index = searchIndex;
        selectingResult = true; ResultsList.SelectedIndex = index; selectingResult = false;
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        var hit = hits[index];
        lastSearchLocation = hit;
        await NavigateHitAsync(hit);
        // A context change starts a fresh search. Only update this batch's counter while it is current.
        if (hits == searchHits) SearchStatus.Text = $"{index + 1:N0} / {hits.Length:N0}{(searchLimited ? " (첫 100,000개)" : "")}";
    }
    private async void Result_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (selectingResult || ResultsList.SelectedItem is not ResultRow row || row.Source != data) return;
        searchIndex = ResultsList.SelectedIndex;
        lastSearchLocation = row.Hit;
        await NavigateHitAsync(row.Hit);
    }
    private async Task NavigateHitAsync(LocatedSearchHit hit)
    {
        if (ActiveScope is { } scope && !scope.Contains(hit.SourceLineIndex)) return;
        using var navigation = BeginNavigation();
        var source = data;
        if (projection?.FindDisplayLine(hit.SourceLineIndex + 1) is null && !await ShowContextAsync(hit.SourceLineIndex)) return;
        if (source != data || projection is null) return;
        if (hit.Length == 0) { NavigateVisible(hit.SourceLineIndex, hit.SourceColumn); return; }
        if (projection.GetDisplayOffset(hit.SourceOffset) is not { } offset) return;
        Editor.Select(offset, Math.Min(hit.Length, Editor.Document.TextLength - offset));
        if (Editor.SelectionStart != offset || Editor.SelectionLength != hit.Length)
        {
            SearchStatus.Text = "줄바꿈 안의 일치 · 선택은 편집기의 개행 경계로 표시";
            SearchScopeStatus.Text += " · 개행 선택은 편집기 경계";
        }
        var location = Editor.Document.GetLocation(offset);
        Editor.ScrollTo(location.Line, location.Column); Editor.Focus();
    }
    private async void Previous_Click(object sender, RoutedEventArgs e) => await NavigateSearchAsync(true);
    private async void Next_Click(object sender, RoutedEventArgs e) => await NavigateSearchAsync(false);
    private async void Search_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; await NavigateSearchAsync(Keyboard.Modifiers == ModifierKeys.Shift); } }
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (TryCancelResultsResize(e)) return;
        ReleaseUnusedLineActionCaptureForKeyboard(Mouse.RightButton == MouseButtonState.Released);
        var modifiers = Keyboard.Modifiers;
        if (TryHandleRecoveryShortcut(e, modifiers)) return;
        if (TryHandleMenuShortcut(e)) return;
        if (modifiers == ModifierKeys.Control && e.Key == Key.N) { e.Handled = true; CreateBlankSession(); }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.W) { e.Handled = true; CloseActiveSession_Click(this, new()); }
        else if (e.Key == Key.Tab && (modifiers == ModifierKeys.Control || modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
        { e.Handled = true; CycleSession(modifiers.HasFlag(ModifierKeys.Shift)); }
        else if (LogTransfer.OpensLogOnPaste(e.Key, modifiers, Keyboard.FocusedElement)) { e.Handled = true; await PasteAsync(); }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.F) { ShowSearch(); e.Handled = true; }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.O) { Open_Click(this, new()); e.Handled = true; }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.G) { GoTo_Click(this, new()); e.Handled = true; }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.F2) { BookmarkToggle_Click(this, new()); e.Handled = true; }
        else if ((modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift) && e.Key == Key.F2) { e.Handled = true; await NavigateBookmarkAsync(modifiers == ModifierKeys.Shift); }
        else if ((modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift) && e.Key == Key.F3) { e.Handled = true; await NavigateSearchAsync(modifiers == ModifierKeys.Shift); }
        else if (modifiers == ModifierKeys.None && e.Key == Key.F4) { e.Handled = true; await NavigateSearchAsync(true); }
        else if (modifiers == ModifierKeys.Shift && e.Key == Key.F8) { e.Handled = true; ToggleHighlightAtSelection(); }
        else if (modifiers == ModifierKeys.None && e.Key == Key.F1) { Help_Click(this, new()); e.Handled = true; }
        else if (modifiers == ModifierKeys.None && e.Key == Key.Escape)
        {
            if (GoToPanel.Visibility == Visibility.Visible) { CloseGoTo_Click(this, new()); e.Handled = true; }
            else if (SearchBar.Visibility == Visibility.Visible) { CloseSearch_Click(this, new()); e.Handled = true; }
        }
    }
}
