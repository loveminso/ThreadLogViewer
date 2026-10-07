using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    // Sessions live in memory only. Each keeps its immutable source and its own view state.
    private readonly ObservableCollection<LogSession> sessions = [];
    private readonly ClosedSessionRetention<LogSession> closedSessions = new(SessionResources);
    private LogSession? activeSession;
    private bool selectingSession;
    private long fileBatchVersion;
    private long fileOpenAttemptVersion;
    private long? explicitlyCancelledFileBatch;
    private bool lastFileOpenCancelled;
    private long? lastFileOpenWorkVersion;
    private long closedSessionEvictionCount;
    private int pastedSessionNumber;
    private int? separationStartLine;
    private LogLineRange? ActiveScope => activeSession?.Scope;
    private bool CanRestoreClosedSession => closedSessions.Count > 0;
    private bool CanRefreshCurrentSession => activeSession is { IsBlank: false, Scope: null } session && session.Source.SourcePath is not null;

    private sealed class LogSession(LogData source, LogProjection view, TextDocument document, string title, LogLineRange? scope = null, string? sourceTitle = null, bool isBlank = false)
    {
        public bool IsBlank { get; } = isBlank;
        public EncodingMode EncodingMode { get; set; } = EncodingMode.Auto;
        public NavigationHistory History { get; set; } = new();
        public string ThreadSearchQuery { get; set; } = "";
        public LogData Source { get; } = source;
        public LogLineRange? Scope { get; } = scope;
        public int? SeparationStart { get; set; }
        public LogProjection View { get; set; } = view;
        public TextDocument Document { get; set; } = document;
        public string DisplayTitle { get; } = title;
        public string SourceTitle { get; } = sourceTitle ?? title;
        public string ToolTip => (IsBlank ? "새 빈 탭 · Ctrl+V로 로그 붙여넣기 / Ctrl+O로 파일 열기" : Source.SourcePath ?? "클립보드에서 연 로그 · 이 실행 중에만 유지됩니다.") +
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

    private void InitializeSessions()
    {
        SessionTabs.ItemsSource = sessions;
        Closed += (_, _) => closedSessions.Clear();
    }

    private void DisposeSessions()
    {
        viewReady = false;
        DetachSessionHandlers();
        ClearLineActionTarget(); ResetLineSelectionGesture();
        closedSessions.Clear();
        SessionTabs.ItemsSource = null; sessions.Clear(); activeSession = null;
        data = null; projection = null; requestedPath = null;
        ThreadList.ItemsSource = null; threadItems = [];
        keywordRules.Clear(); bookmarks.Clear(); BookmarkList.ItemsSource = null;
        ResultsList.ItemsSource = null; searchHits = []; fixedSearchRanges = null; lastSearchLocation = null;
        searchRenderer.Index = keywordRenderer.Index = HighlightIndex.Empty;
        threadRenderer.Projection = margin.Projection = null;
        margin.Bookmarks = new HashSet<int>();
        emptyAnchor = normalAnchor = null; normalSelectedThreads = null;
        timeA = timeB = null; separationStartLine = null; contextActive = false;
        viewVersion++;
        Editor.Document = new TextDocument { UndoStack = { SizeLimit = 0 } };
    }

    private void CaptureActiveSession()
    {
        if (activeSession is not { } session || data is null || projection is null) return;
        session.View = projection; session.Document = Editor.Document; session.Threads = threadItems;
        session.ThreadSearchQuery = ThreadSearchQuery;
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

    private int CommitLoadedSession(LogData source, LogProjection view, TextDocument document, LogSession? replace,
        LogLineRange? scope = null, string? title = null, string? sourceTitle = null, bool isBlank = false,
        EncodingMode encodingMode = EncodingMode.Auto)
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
            ThreadSearchQuery = ""; RefreshThreadSearch();
            var session = new LogSession(source, view, document, title ??
                (source.SourcePath is null ? $"붙여넣은 로그 {++pastedSessionNumber}" : Path.GetFileName(source.SourcePath)), scope, sourceTitle, isBlank)
                { EncodingMode = encodingMode };
            selectingSession = true;
            if (replace is not null && sessions.IndexOf(replace) is var index && index >= 0) sessions[index] = session;
            else sessions.Add(session);
            activeSession = session; SessionTabs.SelectedItem = session; SessionTabs.Visibility = Visibility.Visible;
            SessionTabs.ScrollIntoView(session);
            PublishView(view, document, false);
        }
        finally { selectingSession = false; restoringPosition = false; viewReady = true; }
        UpdateSourceHeader(); UpdatePosition(); UpdateTime(); UpdateSplitStatus(); UpdateFilterSummary();
        _ = SearchAsync(); _ = RefreshKeywordsAsync(); UpdateMenus(); CaptureActiveSession();
        return TrimClosedSessions();
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
            ThreadSearchQuery = session.ThreadSearchQuery; RefreshThreadSearch();
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
            RestoreSessionViewport(session);
        }
        finally { selectingSession = wasSelectingSession; restoringPosition = false; viewReady = true; }
        UpdateSourceHeader(); UpdateExportLabel(contextActive); UpdateFilterSummary();
        UpdatePosition(); RefreshBookmarks(); UpdateTime(); UpdateSplitStatus(); UpdateAnalysisInputState();
        _ = SearchAsync(); _ = RefreshKeywordsAsync(); UpdateMenus();
        OperationStatus.Text = $"{session.DisplayTitle} · 탭 전환 완료";
    }

    private void RestoreSessionViewport(LogSession session)
    {
        if (session.Position is not { } anchor || projection is not { Count: > 0 } view) return;
        void RestoreViewport()
        {
            if (Editor.ActualWidth <= 0 || Editor.ActualHeight <= 0) return;
            Editor.UpdateLayout(); Editor.TextArea.TextView.EnsureVisualLines();
            int top = view.FindDisplayLine(anchor.TopSourceLine + 1, true) ?? 1;
            Editor.ScrollToVerticalOffset(Editor.TextArea.TextView.GetVisualTopByDocumentLine(top) + anchor.TopDelta);
            Editor.ScrollToHorizontalOffset(anchor.Horizontal);
        }
        // Document replacement and selection can clamp against the previous document's empty extent.
        // Restore after selection, then once more after WPF has rebuilt the visible document's layout.
        RestoreViewport();
        long expectedView = viewVersion;
        int caret = Editor.TextArea.Caret.Offset, start = Editor.SelectionStart, length = Editor.SelectionLength;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!viewReady || activeSession != session || projection != view || viewVersion != expectedView ||
                Editor.Document != session.Document || Editor.TextArea.Caret.Offset != caret ||
                Editor.SelectionStart != start || Editor.SelectionLength != length) return;
            bool previousRestoring = restoringPosition; restoringPosition = true;
            try { RestoreViewport(); }
            finally { restoringPosition = previousRestoring; }
        }));
    }

    private void UpdateSourceHeader()
    {
        requestedPath = data?.SourcePath; ReloadButton.IsEnabled = requestedPath is not null && ActiveScope is null;
        FileLabel.Text = activeSession?.DisplayTitle ?? "로그 원문";
        FileLabel.ToolTip = activeSession?.ToolTip ?? "줄 번호는 붙여넣은 텍스트의 첫 줄부터 1입니다.";
        ThreadScopeTitle.Text = IsBlankSession ? "스레드 · 입력 대기" : ActiveScope is null ? "스레드 · 전체 파일" : "스레드 · 분리 세션";
        ThreadScopeHint.Text = IsBlankSession ? "파일을 열거나 로그를 붙여넣으면 스레드를 표시합니다." : ActiveScope is { } scope ? $"원본 {scope.FirstLineIndex + 1:N0}~{scope.LastLineIndex + 1:N0}줄 기준입니다." : "건수와 시간은 전체 파일 기준입니다.";
        ((ComboBoxItem)SearchScopeBox.Items[1]).Content = ActiveScope is null ? "전체 원본" : "전체 세션";
        Title = activeSession is null ? AppTitle : $"{FileLabel.Text} — {AppTitle}";
        ThreadCount.Text = $"{threadItems.Count:N0}개";
        EncodingStatus.Text = data?.EncodingDescription ?? "인코딩: 파일 없음";
        ParseStatus.Text = IsBlankSession ? "파싱: 입력 대기" : data is null ? "파싱: 대기" : $"{(ActiveScope is null ? "기록" : "원본 전체 파싱")}: 완전 {data.CompleteCount:N0} · 부분 {data.PartialCount:N0} · 미인식 {data.UnrecognizedCount:N0}";
        ParseStatus.ToolTip = data is null ? null : $"헤더 기준 {data.Entries.Count:N0}건 · 이어지는 본문 {data.ContinuationCount:N0}줄\n시간 헤더부터 다음 시간 헤더 직전까지 같은 기록입니다.";
        ExportButton.IsEnabled = !busy && projection is not null && !IsBlankSession;
        UpdateBlankSessionHint();
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
            int evicted = CommitLoadedSession(source, result.view, result.document, null, range,
                $"{baseTitle} · {range.FirstLineIndex + 1:N0}~{range.LastLineIndex + 1:N0}줄", baseTitle,
                encodingMode: activeSession?.EncodingMode ?? EncodingMode.Auto);
            OperationStatus.Text = "세션 분리 완료 · 시작과 끝 포함 · 숨겨진 원본 줄도 포함 · 원본 번호 유지" + ClosedRetentionNotice(evicted);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        { if (work.IsCurrent(op.Version)) ShowError("세션을 분리할 수 없습니다", ex); return false; }
        finally { FinishWork(op.Version); }
    }

    private async Task<bool> OpenFilesAsync(IEnumerable<string> paths)
        => await OpenFileBatchAsync(paths.ToArray(), path => OpenAsync(path, EncodingMode.Auto));

    private void MarkFileBatchCancellation() => explicitlyCancelledFileBatch = fileBatchVersion;

    // The injected opener is used only by synthetic delay/cancellation regression tests.
    private async Task<bool> OpenFileBatchAsync(string[] snapshot, Func<string, Task<bool>> open)
    {
        if (snapshot.Length == 0) return false;
        long batch = ++fileBatchVersion;
        long evictionsBefore = closedSessionEvictionCount;
        int success = 0, processed = 0;
        var failed = new List<string>();
        LogSession? expectedSession = activeSession;
        long? expectedWork = null;
        foreach (string path in snapshot)
        {
            if (batch != fileBatchVersion) break;
            expectedSession = activeSession;
            fileOpenAttemptVersion++; lastFileOpenCancelled = false; lastFileOpenWorkVersion = null;
            var opening = open(path);
            expectedWork = lastFileOpenWorkVersion;
            bool opened = await opening;
            if (opened) { success++; processed++; expectedSession = activeSession; }
            else if (lastFileOpenCancelled || batch != fileBatchVersion) break;
            else { processed++; failed.Add(Path.GetFileName(path)); }
        }
        int unprocessed = snapshot.Length - processed;
        bool explicitlyCancelled = explicitlyCancelledFileBatch == batch && fileBatchVersion == batch + 1;
        if (viewReady && !busy && (expectedWork is null || work.IsCurrent(expectedWork.Value)) &&
            (batch == fileBatchVersion || explicitlyCancelled && activeSession == expectedSession))
        {
            string result = explicitlyCancelled ? "파일 묶음 열기 취소" : unprocessed > 0 ? "파일 묶음 열기 중단" : "파일 묶음 열기 완료";
            OperationStatus.Text = $"{result} · 성공 {success:N0}개 · 실패 {failed.Count:N0}개 · 미처리 {unprocessed:N0}개" +
                (success == 0 ? " · 이전 화면 유지" : "") +
                (failed.Count > 0 ? $" · 실패한 파일: {string.Join(", ", failed)}. 경로와 파일 잠금을 확인한 뒤 다시 열어 주세요." : "") +
                (unprocessed > 0 ? " · 미처리 파일은 다시 선택해 열어 주세요." : "") +
                ClosedRetentionNotice((int)Math.Min(int.MaxValue, closedSessionEvictionCount - evictionsBefore));
        }
        return success == snapshot.Length;
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
            bool retained = closedSessions.Add(session, index, sessions, out int evicted);
            SessionTabs.SelectedItem = activeSession;
            SessionTabs.Visibility = sessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            OperationStatus.Text = retained ? $"{session.DisplayTitle} · 탭 닫기 완료 · Ctrl+Shift+T로 복원" +
                (evicted > 0 ? " · 보관 한도로 오래된 닫은 탭을 정리했습니다." : "") :
                $"{session.DisplayTitle} · 탭을 닫았습니다. 복원 보관의 추정 메모리 128MiB 한도를 넘어 보관하지 않았습니다. " +
                (session.Source.SourcePath is null ? "붙여넣은 원문을 다시 복사해 열어 주세요." : "원본 파일을 다시 열어 주세요.");
        }
        finally { selectingSession = false; }
        UpdateMenus();
    }
    private void RestoreClosedSession_Click(object sender, RoutedEventArgs e)
    {
        if (!closedSessions.TryPop(out var session, out int index) || session is null) return;
        fileBatchVersion++;
        selectingSession = true;
        try
        {
            sessions.Insert(Math.Clamp(index, 0, sessions.Count), session);
            SessionTabs.Visibility = Visibility.Visible;
            ActivateSession(session);
            SessionTabs.SelectedItem = session;
        }
        finally { selectingSession = false; }
        int evicted = TrimClosedSessions();
        UpdateMenus();
        OperationStatus.Text = $"{session.DisplayTitle} · 닫은 탭 복원 완료 · 원문과 분석 상태 유지" + ClosedRetentionNotice(evicted);
    }

    private static string ClosedRetentionNotice(int evicted) => evicted <= 0 ? "" :
        $" · 열린 탭의 원본 공유 상태가 바뀌어 추정 보관 한도를 초과한 닫은 탭 {evicted:N0}개를 정리했습니다. 필요한 원본 파일이나 붙여넣은 원문을 다시 여세요.";

    private int TrimClosedSessions()
    {
        int evicted = closedSessions.Trim(sessions);
        closedSessionEvictionCount += evicted;
        return evicted;
    }

    private static IEnumerable<RetainedResource> SessionResources(LogSession session)
    {
        // Conservative estimates, not process working-set measurements. Shared immutable snapshots count once.
        yield return new(session.Source, 256L + 2L * session.Source.Text.Length +
            (Unsafe.SizeOf<LogLine>() + 4L) * session.Source.Lines.Count + 16L * session.Source.Entries.Count + 96L * session.Source.Threads.Count);
        yield return new(session.View, 128L + 8L * session.View.Count + 4L * session.View.EntryCount);
        if (!ReferenceEquals(session.View.Text, session.Source.Text)) yield return new(session.View.Text, 32L + 2L * session.View.Text.Length);
        yield return new(session.Document, 256L + 2L * session.Document.TextLength + 80L * session.View.Count);
        yield return new(session.Threads, 64L + 160L * session.Threads.Count);
        yield return new(session.History, 64L + 128L * (session.History.Back.Count + session.History.Forward.Count));
        if (session.WholeLineSelection is { } selection) yield return new(selection, 32L + 4L * selection.Length);
        yield return new(session, 2048L + 64L * session.Bookmarks.Length + 32L * (session.SearchRanges?.Count ?? 0));
        foreach (string text in new[] { session.Includes, session.Excludes, session.Query, session.KeywordInput,
            session.ThreadSearchQuery, session.ContextText, session.ContextInput, session.ContextRadius,
            session.DisplayTitle, session.SourceTitle }.Concat(session.Filter.Includes).Concat(session.Filter.Excludes)
            .Concat(session.Bookmarks.Select(bookmark => bookmark.Label)).Concat(session.Keywords.Select(rule => rule.Keyword)))
            yield return new(text, 32L + 2L * text.Length);
        foreach (var rule in session.Keywords) yield return new(rule, 96);
    }
    private void ClearLastSession()
    {
        DetachSessionHandlers(); ClearLineActionTarget(); viewReady = false; restoringPosition = true;
        try
        {
            activeSession = null; data = null; projection = null; threadItems = [];
            ThreadList.ItemsSource = null; keywordRules.Clear(); KeywordBox.Clear();
            ThreadSearchQuery = ""; RefreshThreadSearch();
            threadRenderer.Projection = margin.Projection = null;
            ResetDocumentFeatures(); Editor.Document = new TextDocument();
            SearchBox.Clear(); ResultsList.ItemsSource = null;
            searchHits = []; searchIndex = -1; searchLimited = false;
            searchRenderer.Index = keywordRenderer.Index = HighlightIndex.Empty; SearchStatus.Text = "";
            SearchBar.Visibility = ResultsPanel.Visibility = GoToPanel.Visibility = Visibility.Collapsed;
            CountStatus.Text = "0/0건 · 표시 0/0줄"; PositionStatus.Text = "읽기 전용";
            EmptyPanel.Visibility = Visibility.Visible; EmptyHint.Text = "로그 파일을 열거나 새 탭을 만드세요.";
            EmptyDetail.Text = "+ 버튼 / Ctrl+N 새 탭 · Ctrl+O 파일 열기 · Ctrl+V 로그 붙여넣기";
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
