using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    // Sessions live in memory only. Each keeps its immutable source and its own view state.
    private readonly ObservableCollection<LogSession> sessions = [];
    private LogSession? activeSession;
    private bool selectingSession;
    private long fileBatchVersion;
    private int pastedSessionNumber;
    private int? separationStartLine;
    private LogLineRange? ActiveScope => activeSession?.Scope;

    private sealed class LogSession(LogData source, LogProjection view, TextDocument document, string title, LogLineRange? scope = null, string? sourceTitle = null)
    {
        public LogData Source { get; } = source;
        public LogLineRange? Scope { get; } = scope;
        public int? SeparationStart { get; set; }
        public LogProjection View { get; set; } = view;
        public TextDocument Document { get; set; } = document;
        public string DisplayTitle { get; } = title;
        public string SourceTitle { get; } = sourceTitle ?? title;
        public string ToolTip => (Source.SourcePath ?? "클립보드에서 연 로그 · 이 실행 중에만 유지됩니다.") +
            (Scope is { } range ? $"\n원본 {range.FirstLineIndex + 1:N0}~{range.LastLineIndex + 1:N0}줄 · 시작과 끝 포함 · 읽기 전용" : "");
        public List<ThreadItem> Threads { get; set; } = [];
        public Bookmark[] Bookmarks { get; set; } = [];
        public PositionAnchor? Position { get; set; }
        public int SelectionStart { get; set; }
        public int SelectionLength { get; set; }
        public int[]? WholeLineSelection { get; set; }
        public PositionAnchor? NormalPosition { get; set; }
        public PositionAnchor? EmptyPosition { get; set; }
        public IReadOnlySet<int?>? NormalThreads { get; set; }
        public EntryFilter Filter { get; set; } = EntryFilter.Empty;
        public string Includes { get; set; } = "";
        public string Excludes { get; set; } = "";
        public bool IncludeAll { get; set; }
        public bool FilterCase { get; set; }
        public TimeAnchor? TimeA { get; set; }
        public TimeAnchor? TimeB { get; set; }
        public KeywordRuleItem[] Keywords { get; set; } = [];
        public string KeywordInput { get; set; } = "";
        public int KeywordColor { get; set; }
        public bool Context { get; set; }
        public int? ContextLine { get; set; }
        public int? ContextEntry { get; set; }
        public string ContextText { get; set; } = "";
        public object? ContextTip { get; set; }
        public string ContextInput { get; set; } = "";
        public string ContextRadius { get; set; } = "20";
        public string Query { get; set; } = "";
        public bool SearchCase { get; set; }
        public bool SearchWord { get; set; }
        public bool SearchRegex { get; set; }
        public int SearchScope { get; set; }
        public IReadOnlyList<SourceTextRange>? SearchRanges { get; set; }
        public LocatedSearchHit? LastHit { get; set; }
        public bool SearchVisible { get; set; }
        public bool AnalysisExpanded { get; set; }
        public int AnalysisTool { get; set; }
        public bool BookmarksExpanded { get; set; }
        public bool FilterExpanded { get; set; }
    }

    private void InitializeSessions() => SessionTabs.ItemsSource = sessions;

    private void CaptureActiveSession()
    {
        if (activeSession is not { } session || data is null || projection is null) return;
        session.View = projection; session.Document = Editor.Document; session.Threads = threadItems;
        session.Position = CapturePosition(); session.EmptyPosition = emptyAnchor;
        session.SelectionStart = Editor.SelectionStart; session.SelectionLength = Editor.SelectionLength;
        session.WholeLineSelection = CaptureWholeLineSelection();
        session.NormalPosition = normalAnchor; session.NormalThreads = normalSelectedThreads;
        session.Bookmarks = bookmarks.Items.ToArray(); session.TimeA = timeA; session.TimeB = timeB;
        session.SeparationStart = separationStartLine;
        session.Filter = appliedFilter; session.Includes = IncludeBox.Text; session.Excludes = ExcludeBox.Text;
        session.IncludeAll = IncludeModeBox.SelectedIndex == 1; session.FilterCase = FilterCaseBox.IsChecked == true;
        session.Keywords = keywordRules.ToArray(); session.KeywordInput = KeywordBox.Text; session.KeywordColor = KeywordColorBox.SelectedIndex;
        session.Context = contextActive; session.ContextLine = margin.ContextLineIndex; session.ContextEntry = threadRenderer.ContextEntryIndex;
        session.ContextText = ContextStatus.Text; session.ContextTip = ContextStatus.ToolTip;
        session.ContextInput = ContextInputStatus.Text; session.ContextRadius = ContextRadiusBox.Text;
        session.Query = SearchBox.Text; session.SearchCase = SearchCaseBox.IsChecked == true;
        session.SearchWord = SearchWordBox.IsChecked == true; session.SearchRegex = SearchRegexBox.IsChecked == true;
        session.SearchScope = SearchScopeBox.SelectedIndex; session.SearchRanges = fixedSearchRanges;
        session.LastHit = lastSearchLocation; session.SearchVisible = SearchBar.Visibility == Visibility.Visible;
        session.AnalysisExpanded = AnalysisPanel.IsExpanded;
        session.AnalysisTool = TimeToolButton.IsChecked == true ? 1 : HighlightToolButton.IsChecked == true ? 2 : 0;
        session.BookmarksExpanded = BookmarkPanel.IsExpanded; session.FilterExpanded = ContentFilterExpander.IsExpanded;
    }

    private void DetachSessionHandlers()
    {
        foreach (var item in threadItems) item.PropertyChanged -= Thread_Changed;
        foreach (var rule in keywordRules) rule.PropertyChanged -= Keyword_Changed;
    }

    private void CancelSessionWork()
    {
        if (busy) RestoreFilters();
        var operation = work.Begin(); // Invalidate results already queued on the dispatcher as well.
        FinishWork(operation.Version);
        searchWork.Begin(); highlightWork.Begin(); selectionWork.Begin();
    }

    private void CommitLoadedSession(LogData source, LogProjection view, TextDocument document, LogSession? replace,
        LogLineRange? scope = null, string? title = null, string? sourceTitle = null)
    {
        CaptureActiveSession();
        DetachSessionHandlers();
        ClearLineActionTarget();
        viewReady = false; restoringPosition = true;
        try
        {
            // Publish the matching source/view before resetting controls which raise synchronous events.
            data = source; projection = view;
            ResetDocumentFeatures();
            keywordRules.Clear(); KeywordBox.Clear(); KeywordColorBox.SelectedIndex = 0;
            SearchBox.Clear(); SearchCaseBox.IsChecked = SearchWordBox.IsChecked = SearchRegexBox.IsChecked = false;
            SearchScopeBox.SelectedIndex = 0;
            SearchBar.Visibility = ResultsPanel.Visibility = GoToPanel.Visibility = Visibility.Collapsed;
            ContextInputStatus.Text = "로그를 클릭한 뒤 주변 보기를 누르세요. 기록은 헤더와 그 아래 본문을 묶은 단위입니다.";
            threadItems = LogSlice.GetThreadSummaries(source, scope).Select(t => new ThreadItem(t, theme)).ToList();
            foreach (var item in threadItems) item.PropertyChanged += Thread_Changed;
            ThreadList.ItemsSource = threadItems;
            var session = new LogSession(source, view, document, title ??
                (source.SourcePath is null ? $"붙여넣은 로그 {++pastedSessionNumber}" : Path.GetFileName(source.SourcePath)), scope, sourceTitle);
            selectingSession = true;
            if (replace is not null && sessions.IndexOf(replace) is var index && index >= 0) sessions[index] = session;
            else sessions.Add(session);
            activeSession = session; SessionTabs.SelectedItem = session; SessionTabs.Visibility = Visibility.Visible;
            PublishView(view, document, false);
        }
        finally { selectingSession = false; restoringPosition = false; viewReady = true; }
        UpdateSourceHeader(); UpdatePosition(); UpdateTime(); UpdateSplitStatus(); UpdateFilterSummary();
        _ = SearchAsync(); _ = RefreshKeywordsAsync(); UpdateMenus(); CaptureActiveSession();
    }

    private void ActivateSession(LogSession session)
    {
        if (session == activeSession || !sessions.Contains(session)) return;
        bool wasSelectingSession = selectingSession;
        CancelSessionWork(); CaptureActiveSession(); DetachSessionHandlers(); ClearLineActionTarget();
        viewReady = false; restoringPosition = true; selectingSession = true;
        try
        {
            activeSession = session; data = session.Source; projection = session.View;
            threadItems = session.Threads;
            foreach (var item in threadItems) { item.ApplyTheme(theme); item.PropertyChanged += Thread_Changed; }
            ThreadList.ItemsSource = threadItems;
            bookmarks.Clear(); foreach (var bookmark in session.Bookmarks) bookmarks.Toggle(bookmark.SourceLineIndex, bookmark.Label);
            timeA = session.TimeA; timeB = session.TimeB;
            separationStartLine = session.SeparationStart;
            appliedFilter = session.Filter; IncludeBox.Text = session.Includes; ExcludeBox.Text = session.Excludes;
            IncludeModeBox.SelectedIndex = session.IncludeAll ? 1 : 0; FilterCaseBox.IsChecked = session.FilterCase;
            keywordRules.Clear(); foreach (var rule in session.Keywords) { rule.PropertyChanged += Keyword_Changed; keywordRules.Add(rule); }
            KeywordBox.Text = session.KeywordInput; KeywordColorBox.SelectedIndex = session.KeywordColor;
            contextActive = session.Context; normalAnchor = session.NormalPosition; normalSelectedThreads = session.NormalThreads;
            emptyAnchor = session.EmptyPosition;
            margin.ContextLineIndex = session.ContextLine; threadRenderer.ContextEntryIndex = session.ContextEntry;
            ContextPanel.Visibility = session.Context ? Visibility.Visible : Visibility.Collapsed;
            ContextStatus.Text = session.ContextText; ContextStatus.ToolTip = session.ContextTip;
            ContextInputStatus.Text = session.ContextInput; ContextRadiusBox.Text = session.ContextRadius;
            SearchBox.Text = session.Query; SearchCaseBox.IsChecked = session.SearchCase;
            SearchWordBox.IsChecked = session.SearchWord; SearchRegexBox.IsChecked = session.SearchRegex;
            SearchScopeBox.SelectedIndex = session.SearchScope; fixedSearchRanges = session.SearchRanges; lastSearchLocation = session.LastHit;
            SearchBar.Visibility = session.SearchVisible ? Visibility.Visible : Visibility.Collapsed;
            ResultsPanel.Visibility = GoToPanel.Visibility = HiddenContextButton.Visibility = Visibility.Collapsed; hiddenTargetLine = null;
            SetAnalysisTool(session.AnalysisTool, false); AnalysisPanel.IsExpanded = false;
            BookmarkPanel.IsExpanded = session.BookmarksExpanded; ContentFilterExpander.IsExpanded = session.FilterExpanded;
            SessionTabs.SelectedItem = session; SessionTabs.ScrollIntoView(session);
            PublishView(session.View, session.Document, false);
            restoringPosition = true;
            if (session.Position is { } position) RestorePosition(position);
            int start = Math.Clamp(session.SelectionStart, 0, Editor.Document.TextLength);
            int end = Math.Clamp(start + session.SelectionLength, start, Editor.Document.TextLength);
            Editor.TextArea.Selection = Selection.Create(Editor.TextArea, start, end);
            if (session.WholeLineSelection is { } selectedLines) RestoreWholeLineSelection(selectedLines);
        }
        finally { selectingSession = wasSelectingSession; restoringPosition = false; viewReady = true; }
        UpdateSourceHeader(); UpdateExportLabel(contextActive); UpdateFilterSummary();
        UpdatePosition(); RefreshBookmarks(); UpdateTime(); UpdateSplitStatus(); UpdateAnalysisInputState();
        _ = SearchAsync(); _ = RefreshKeywordsAsync(); UpdateMenus();
        OperationStatus.Text = $"{session.DisplayTitle} · 탭 전환 완료";
    }

    private void UpdateSourceHeader()
    {
        requestedPath = data?.SourcePath; ReloadButton.IsEnabled = requestedPath is not null && ActiveScope is null;
        FileLabel.Text = activeSession?.DisplayTitle ?? "로그 원문";
        FileLabel.ToolTip = activeSession?.ToolTip ?? "줄 번호는 붙여넣은 텍스트의 첫 줄부터 1입니다.";
        ThreadScopeTitle.Text = ActiveScope is null ? "스레드 · 전체 파일" : "스레드 · 분리 세션";
        ThreadScopeHint.Text = ActiveScope is { } scope ? $"원본 {scope.FirstLineIndex + 1:N0}~{scope.LastLineIndex + 1:N0}줄 기준입니다." : "건수와 시간은 전체 파일 기준입니다.";
        ((ComboBoxItem)SearchScopeBox.Items[1]).Content = ActiveScope is null ? "전체 원본" : "전체 세션";
        Title = activeSession is null ? AppTitle : $"{FileLabel.Text} — {AppTitle}";
        ThreadCount.Text = $"{threadItems.Count:N0}개";
        EncodingStatus.Text = data?.EncodingDescription ?? "인코딩: 파일 없음";
        ParseStatus.Text = data is null ? "파싱: 대기" : $"{(ActiveScope is null ? "기록" : "원본 전체 파싱")}: 완전 {data.CompleteCount:N0} · 부분 {data.PartialCount:N0} · 미인식 {data.UnrecognizedCount:N0}";
        ParseStatus.ToolTip = data is null ? null : $"헤더 기준 {data.Entries.Count:N0}건 · 이어지는 본문 {data.ContinuationCount:N0}줄\n시간 헤더부터 다음 시간 헤더 직전까지 같은 기록입니다.";
        ExportButton.IsEnabled = !busy && projection is not null;
    }

    private async Task<bool> OpenLineSessionAsync(int start, int end)
    {
        if (data is null || busy) return false;
        var source = data;
        var parentScope = ActiveScope;
        var range = new LogLineRange(Math.Min(start, end), Math.Max(start, end));
        if (range.FirstLineIndex < 0 || range.LastLineIndex >= source.Lines.Count ||
            parentScope is { } parent && (!parent.Contains(start) || !parent.Contains(end)))
        { OperationStatus.Text = "분리할 시작과 끝은 현재 세션 안의 원본 줄이어야 합니다."; return false; }
        string baseTitle = activeSession?.SourceTitle ?? (source.SourcePath is null ? "붙여넣은 로그" : Path.GetFileName(source.SourcePath));
        var op = BeginWork("선택 구간을 새 탭으로 여는 중…", true);
        try
        {
            var result = await Task.Run(() =>
            {
                var view = LogProjection.CreateLineRange(source, range, op.Token);
                return (view, document: PrepareDocument(view.Text, op.Token, op.Progress));
            }, op.Token);
            op.Token.ThrowIfCancellationRequested();
            if (!work.IsCurrent(op.Version) || source != data || parentScope != ActiveScope) return false;
            separationStartLine = null;
            CommitLoadedSession(source, result.view, result.document, null, range,
                $"{baseTitle} · {range.FirstLineIndex + 1:N0}~{range.LastLineIndex + 1:N0}줄", baseTitle);
            OperationStatus.Text = "세션 분리 완료 · 시작과 끝 포함 · 숨겨진 원본 줄도 포함 · 원본 번호 유지";
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        { if (work.IsCurrent(op.Version)) ShowError("세션을 분리할 수 없습니다", ex); return false; }
        finally { FinishWork(op.Version); }
    }

    private async Task<bool> OpenFilesAsync(IEnumerable<string> paths)
    {
        string[] snapshot = paths.ToArray();
        if (snapshot.Length == 0) return false;
        long batch = ++fileBatchVersion;
        bool success = true;
        foreach (string path in snapshot)
        {
            if (batch != fileBatchVersion) return false;
            success &= await OpenAsync(path, EncodingMode.Auto);
        }
        return success;
    }

    private void SessionTab_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (!viewReady || selectingSession || SessionTabs.SelectedItem is not LogSession session) return;
        fileBatchVersion++; ActivateSession(session);
    }
    private void CloseSession_Click(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.Tag is LogSession session) CloseSession(session); }
    private void CloseActiveSession_Click(object sender, RoutedEventArgs e)
    { if (activeSession is { } session) CloseSession(session); }
    private void CloseSession(LogSession session)
    {
        int index = sessions.IndexOf(session); if (index < 0) return;
        fileBatchVersion++; selectingSession = true;
        try
        {
            if (session == activeSession)
            {
                CancelSessionWork(); CaptureActiveSession();
                var next = sessions.Where(s => s != session).ElementAtOrDefault(Math.Min(index, sessions.Count - 2));
                if (next is not null) ActivateSession(next); else ClearLastSession();
            }
            sessions.Remove(session);
            SessionTabs.SelectedItem = activeSession;
            SessionTabs.Visibility = sessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { selectingSession = false; }
        UpdateMenus();
    }
    private void ClearLastSession()
    {
        DetachSessionHandlers(); ClearLineActionTarget(); viewReady = false; restoringPosition = true;
        try
        {
            activeSession = null; data = null; projection = null; threadItems = [];
            ThreadList.ItemsSource = null; keywordRules.Clear(); KeywordBox.Clear();
            threadRenderer.Projection = margin.Projection = null;
            ResetDocumentFeatures(); Editor.Document = new TextDocument();
            SearchBox.Clear(); ResultsList.ItemsSource = null;
            searchHits = []; searchIndex = -1; searchLimited = false;
            searchRenderer.Index = keywordRenderer.Index = HighlightIndex.Empty; SearchStatus.Text = "";
            SearchBar.Visibility = ResultsPanel.Visibility = GoToPanel.Visibility = Visibility.Collapsed;
            CountStatus.Text = "0/0건 · 표시 0/0줄"; PositionStatus.Text = "읽기 전용";
            EmptyPanel.Visibility = Visibility.Visible; EmptyHint.Text = "로그 파일을 열거나 클립보드 로그를 붙여넣으세요.";
            EmptyDetail.Text = "파일 메뉴에서 여러 파일을 한 번에 열 수 있습니다.";
        }
        finally { restoringPosition = false; viewReady = true; }
        UpdateSourceHeader(); UpdateAnalysisInputState(); UpdateFilterSummary(); _ = RefreshKeywordsAsync();
    }
    private void NextSession_Click(object sender, RoutedEventArgs e) => CycleSession(false);
    private void PreviousSession_Click(object sender, RoutedEventArgs e) => CycleSession(true);
    private void CycleSession(bool previous)
    {
        if (sessions.Count < 2 || activeSession is null) return;
        int index = (sessions.IndexOf(activeSession) + (previous ? -1 : 1) + sessions.Count) % sessions.Count;
        fileBatchVersion++; ActivateSession(sessions[index]);
    }
}
