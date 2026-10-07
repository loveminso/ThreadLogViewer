using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private async void SaveAnalysis_Click(object sender, RoutedEventArgs e)
    {
        if (busy || data is null || IsBlankSession) return;
        var dialog = new SaveFileDialog { Title = "분석 상태 저장 — 새 파일만", Filter = "ThreadLog 분석 상태 (*.tlv-analysis.json)|*.tlv-analysis.json", FileName = "analysis.tlv-analysis.json", OverwritePrompt = false };
        if (dialog.ShowDialog(this) == true) await SaveAnalysisFileAsync(dialog.FileName);
    }
    private async void LoadAnalysis_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Title = "분석 상태 불러오기", Filter = "ThreadLog 분석 상태 (*.tlv-analysis.json)|*.tlv-analysis.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await LoadAnalysisFileAsync(dialog.FileName);
    }
    private async void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (busy || data is null || IsBlankSession) return;
        var dialog = new SaveFileDialog { Title = "현재 적용된 필터·강조 프리셋 저장 — 새 파일만", Filter = "ThreadLog 프리셋 (*.tlv-preset.json)|*.tlv-preset.json", FileName = "preset.tlv-preset.json", OverwritePrompt = false };
        if (dialog.ShowDialog(this) == true) await SavePresetFileAsync(dialog.FileName);
    }
    private async void LoadPreset_Click(object sender, RoutedEventArgs e)
    {
        if (busy || data is null || IsBlankSession) return;
        var dialog = new OpenFileDialog { Title = "필터·강조 프리셋 불러오기", Filter = "ThreadLog 프리셋 (*.tlv-preset.json)|*.tlv-preset.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await LoadPresetFileAsync(dialog.FileName);
    }

    private static SavedPosition? SavePosition(PositionAnchor? point) => point is null ? null :
        new(point.SourceLine, point.Column, point.TopSourceLine, point.TopDelta, point.Horizontal);
    private static PositionAnchor? ReadPosition(SavedPosition? point) => point is null ? null :
        new(point.Line, point.Column, point.TopLine, point.TopDelta, point.Horizontal);
    private static AnalysisPreset CapturePreset(LogSession session)
    {
        var selected = session.Context ? session.NormalThreads ?? session.View.SelectedThreads :
            session.Threads.Where(thread => thread.IsSelected).Select(thread => thread.Id).ToHashSet();
        return new()
        {
            Filter = new(session.Filter.Includes.ToArray(), session.Filter.Excludes.ToArray(), session.Filter.RequireAll, session.Filter.MatchCase),
            Threads = selected.ToArray(),
            AllThreads = LogSlice.GetThreadSummaries(session.Source, session.Scope).All(thread => selected.Contains(thread.ThreadId)),
            Highlights = session.Keywords.Select(rule => new SavedHighlight(rule.Keyword, rule.ColorIndex, rule.Enabled)).ToArray()
        };
    }
    private SavedAnalysis CaptureAnalysis(LogSession session) => new()
    {
        SourcePath = session.Source.SourcePath, PastedText = session.Source.SourcePath is null ? session.Source.Text : null,
        Encoding = session.EncodingMode, Title = session.DisplayTitle, SourceTitle = session.SourceTitle, Scope = session.Scope, Preset = CapturePreset(session),
        Bookmarks = session.Bookmarks.Select(bookmark => new SavedBookmark(bookmark.SourceLineIndex, bookmark.Label)).ToArray(),
        Position = SavePosition(session.Position), EmptyPosition = SavePosition(session.EmptyPosition), NormalPosition = SavePosition(session.NormalPosition),
        Selection = SelectionSourceRanges.Map(session.View, [new(session.SelectionStart, session.SelectionLength)]).ToArray(),
        WholeLines = session.WholeLineSelection?.ToArray(), IncludesDraft = session.Includes, ExcludesDraft = session.Excludes,
        RequireAllDraft = session.IncludeAll, FilterCaseDraft = session.FilterCase, ThreadQuery = session.ThreadSearchQuery,
        KeywordDraft = session.KeywordInput, KeywordColor = session.KeywordColor, Query = session.Query,
        SearchCase = session.SearchCase, SearchWord = session.SearchWord, SearchRegex = session.SearchRegex, SearchScope = session.SearchScope,
        SearchRanges = session.SearchRanges?.ToArray(), LastHit = session.LastHit, SearchVisible = session.SearchVisible,
        ContextLine = session.Context ? session.ContextLine : null,
        ContextRadius = int.TryParse(session.ContextRadius, out int radius) ? Math.Clamp(radius, 0, 10000) : 20,
        TimeA = session.TimeA?.SourceLineIndex, TimeB = session.TimeB?.SourceLineIndex, SeparationStart = session.SeparationStart,
        FilterExpanded = session.FilterExpanded, BookmarksExpanded = session.BookmarksExpanded, AnalysisTool = session.AnalysisTool,
        Back = session.History.Back.Select(point => new SavedNavigation(SavePosition(point.Position)!, point.Context ? point.ContextLine : null)).ToArray(),
        Forward = session.History.Forward.Select(point => new SavedNavigation(SavePosition(point.Position)!, point.Context ? point.ContextLine : null)).ToArray()
    };

    private async Task SaveAnalysisFileAsync(string path)
    {
        if (busy || activeSession is not { IsBlank: false } session) return;
        CaptureActiveSession(); var state = CaptureAnalysis(session);
        var op = BeginWork("분석 상태 저장…", true);
        try
        {
            await Task.Run(async () => await AnalysisFiles.SaveAnalysisAsync(state with { Source = AnalysisFiles.Stamp(session.Source, op.Token) }, path, op.Token), op.Token);
            if (work.IsCurrent(op.Version)) OperationStatus.Text = "분석 상태 저장 완료 · " + path +
                (state.SourcePath is null ? " · 붙여넣은 원문 포함" : " · 원문 파일은 변경하지 않았습니다. 복원할 때 같은 원문이 필요합니다.");
        }
        catch (OperationCanceledException) { if (work.IsCurrent(op.Version)) OperationStatus.Text = "분석 상태 저장 취소 · 이전 분석 유지"; }
        catch (Exception ex) when (AnalysisFileFailure(ex)) { if (work.IsCurrent(op.Version)) ShowError("분석 상태 저장 실패", ex); }
        finally { FinishWork(op.Version); }
    }
    private async Task SavePresetFileAsync(string path)
    {
        if (busy || activeSession is not { IsBlank: false } session) return;
        CaptureActiveSession(); var preset = CapturePreset(session);
        var op = BeginWork("필터·강조 프리셋 저장…", true);
        try
        {
            await Task.Run(() => AnalysisFiles.SavePresetAsync(preset, path, session.Source.SourcePath, op.Token), op.Token);
            if (work.IsCurrent(op.Version)) OperationStatus.Text = "필터·강조 프리셋 저장 완료 · 적용된 조건 저장 · " + path;
        }
        catch (OperationCanceledException) { if (work.IsCurrent(op.Version)) OperationStatus.Text = "프리셋 저장 취소 · 이전 분석 유지"; }
        catch (Exception ex) when (AnalysisFileFailure(ex)) { if (work.IsCurrent(op.Version)) ShowError("프리셋 저장 실패", ex); }
        finally { FinishWork(op.Version); }
    }
    private Func<CancellationToken, Task>? presetPrepareCheckpoint = null;
    private async Task LoadPresetFileAsync(string path)
    {
        if (busy || activeSession is not { IsBlank: false } previous) return;
        var source = previous.Source;
        var op = BeginWork("필터·강조 프리셋 불러오기…", true);
        try
        {
            var preset = await Task.Run(() => AnalysisFiles.ReadPresetAsync(path, op.Token), op.Token);
            LogSession next;
            while (true)
            {
                op.Token.ThrowIfCancellationRequested();
                if (!work.IsCurrent(op.Version) || !viewReady || previous != activeSession || source != data) return;
                CaptureActiveSession();
                var latest = CopySessionState(previous, source, previous.View, previous.Document);
                var back = previous.History.Back.ToArray(); var forward = previous.History.Forward.ToArray();
                var state = CaptureAnalysis(latest);
                var updated = state with { Preset = preset, ContextLine = null, NormalPosition = null,
                    IncludesDraft = string.Join(Environment.NewLine, preset.Filter.Includes), ExcludesDraft = string.Join(Environment.NewLine, preset.Filter.Excludes),
                    RequireAllDraft = preset.Filter.RequireAll, FilterCaseDraft = preset.Filter.MatchCase };
                var checkpoint = presetPrepareCheckpoint;
                next = await Task.Run(async () =>
                {
                    if (checkpoint is not null) await checkpoint(op.Token);
                    return PrepareAnalysisSession(updated, source, op.Token, op.Progress);
                }, op.Token);
                op.Token.ThrowIfCancellationRequested();
                if (!work.IsCurrent(op.Version) || !viewReady || previous != activeSession || source != data) return;
                CaptureActiveSession();
                if (SameRefreshState(latest, previous, back, forward)) break;
            }
            int evicted = InstallAnalysisSession(next, previous);
            OperationStatus.Text = "필터·강조 프리셋 적용 완료 · 북마크와 원문 유지" + ClosedRetentionNotice(evicted);
        }
        catch (OperationCanceledException) { if (work.IsCurrent(op.Version)) OperationStatus.Text = "프리셋 불러오기 취소 · 이전 분석 유지"; }
        catch (Exception ex) when (AnalysisFileFailure(ex)) { if (work.IsCurrent(op.Version)) ShowError("프리셋 불러오기 실패", ex); }
        finally { FinishWork(op.Version); }
    }
    private async Task LoadAnalysisFileAsync(string path)
    {
        if (busy) return;
        var previous = activeSession; var existing = data;
        var op = BeginWork("분석 상태 불러오기…", true);
        try
        {
            var next = await Task.Run(async () =>
            {
                var state = await AnalysisFiles.ReadAnalysisAsync(path, op.Token);
                LogData source;
                if (existing is not null && SameAnalysisSource(existing.SourcePath, state.SourcePath) && AnalysisFiles.Stamp(existing, op.Token) == state.Source) source = existing;
                else if (state.SourcePath is not null) source = await LogFileReader.ReadAsync(state.SourcePath, state.Encoding, op.Token, op.Progress);
                else source = LogParser.ParsePastedText(state.PastedText!, op.Token, op.Progress);
                if (AnalysisFiles.Stamp(source, op.Token) != state.Source)
                    throw new InvalidDataException("저장할 때의 원문과 내용이 다릅니다. 같은 원문을 선택하거나 현재 파일에서 분석을 다시 지정하세요.");
                return PrepareAnalysisSession(state, source, op.Token, op.Progress);
            }, op.Token);
            if (!work.IsCurrent(op.Version) || op.Token.IsCancellationRequested || previous != activeSession || existing != data) return;
            int evicted = InstallAnalysisSession(next, previous is { IsBlank: true } || next.Source == existing && next.Scope == previous?.Scope ? previous : null);
            OperationStatus.Text = "분석 상태 복원 완료 · 원문 일치 확인 · 북마크·조건·강조·위치 복원" + ClosedRetentionNotice(evicted);
        }
        catch (OperationCanceledException) { if (work.IsCurrent(op.Version)) OperationStatus.Text = "분석 상태 불러오기 취소 · 이전 분석 유지"; }
        catch (Exception ex) when (AnalysisFileFailure(ex)) { if (work.IsCurrent(op.Version)) ShowError("분석 상태 불러오기 실패", ex); }
        finally { FinishWork(op.Version); }
    }

    private static LogSession PrepareAnalysisSession(SavedAnalysis state, LogData source, CancellationToken token, IProgress<WorkProgress> progress)
    {
        AnalysisFiles.Validate(state.Preset); token.ThrowIfCancellationRequested();
        var requested = state.Preset.Threads.ToHashSet(); var selected = new HashSet<int?>();
        foreach (var thread in LogSlice.GetThreadSummaries(source, state.Scope))
        {
            token.ThrowIfCancellationRequested();
            if (state.Preset.AllThreads || requested.Contains(thread.ThreadId)) selected.Add(thread.ThreadId);
        }
        var view = state.ContextLine is { } center ? LogContext.Create(source, center, state.ContextRadius, token, state.Scope) :
            LogProjection.CreateFiltered(source, selected, state.Preset.Filter, token, progress, state.Scope);
        var session = new LogSession(source, view, PrepareDocument(view.Text, token, progress), state.Title, state.Scope, state.SourceTitle)
        {
            EncodingMode = state.Encoding, Filter = state.Preset.Filter, Includes = state.IncludesDraft, Excludes = state.ExcludesDraft,
            IncludeAll = state.RequireAllDraft, FilterCase = state.FilterCaseDraft, Position = ReadPosition(state.Position), EmptyPosition = ReadPosition(state.EmptyPosition),
            NormalPosition = ReadPosition(state.NormalPosition), NormalThreads = state.ContextLine is null ? null : selected,
            Keywords = state.Preset.Highlights.Select(rule => new KeywordRuleItem(rule.Phrase, rule.Color) { Enabled = rule.Enabled }).ToArray(),
            KeywordInput = state.KeywordDraft, KeywordColor = state.KeywordColor, ThreadSearchQuery = state.ThreadQuery,
            Bookmarks = state.Bookmarks.Select(bookmark => new Bookmark(bookmark.Line, bookmark.Label)).ToArray(),
            Query = state.Query, SearchCase = state.SearchCase, SearchWord = state.SearchWord, SearchRegex = state.SearchRegex, SearchScope = state.SearchScope,
            SearchRanges = state.SearchRanges, LastHit = state.LastHit, SearchVisible = state.SearchVisible, Context = state.ContextLine is not null,
            ContextLine = state.ContextLine, ContextRadius = state.ContextRadius.ToString(),
            TimeA = state.TimeA is { } a ? LogTimeAnalysis.ResolveAnchor(source, a) : null,
            TimeB = state.TimeB is { } b ? LogTimeAnalysis.ResolveAnchor(source, b) : null, SeparationStart = state.SeparationStart,
            FilterExpanded = state.FilterExpanded, BookmarksExpanded = state.BookmarksExpanded, AnalysisTool = state.AnalysisTool,
            WholeLineSelection = state.WholeLines?.Where(line => view.FindDisplayLine(line + 1) is not null).ToArray()
        };
        if (state.ContextLine is { } context)
        {
            session.ContextEntry = source.Lines[context].EntryIndex;
            session.ContextText = $"주변 로그 보기 중 · 기준: 원본 {context + 1:N0}줄 · 모든 스레드";
            session.ContextInput = session.ContextText; session.ContextTip = "저장된 분석 상태에서 같은 원문을 확인했습니다.";
        }
        foreach (var (saved, stack) in new[] { (state.Back, session.History.Back), (state.Forward, session.History.Forward) })
            foreach (var point in saved) stack.Add(new(ReadPosition(point.Position)!, point.ContextLine is not null, point.ContextLine));
        int first = -1, end = -1;
        foreach (var range in state.Selection.Where(range => range.Length > 0))
        {
            token.ThrowIfCancellationRequested();
            if (view.GetDisplayOffset(range.Offset) is not { } start) { first = -1; break; }
            int lastSource = source.GetLineIndexAtOffset(range.Offset + range.Length - 1);
            if (view.FindDisplayLine(lastSource + 1) is not { } last) { first = -1; break; }
            int finish = view.DisplayOffsets[last - 1] + range.Offset + range.Length - source.GetLineOffset(lastSource);
            if (first >= 0 && start != end || finish - start != range.Length || !source.Text.AsSpan(range.Offset, range.Length).SequenceEqual(view.Text.AsSpan(start, finish - start)))
            { first = -1; break; }
            if (first < 0) first = start; end = finish;
        }
        if (first >= 0) { session.SelectionStart = first; session.SelectionLength = end - first; }
        else if (session.Position is { } position && view.FindDisplayLine(position.SourceLine + 1) is { } display)
            session.SelectionStart = view.DisplayOffsets[display - 1] + Math.Clamp(position.Column - 1, 0, source.Lines[position.SourceLine].RawText.Length);
        // Threads are created on the UI thread when the prepared state is installed.
        token.ThrowIfCancellationRequested(); return session;
    }

    private int InstallAnalysisSession(LogSession next, LogSession? replace)
    {
        var selected = next.Context ? next.NormalThreads! : next.View.SelectedThreads;
        next.Threads = LogSlice.GetThreadSummaries(next.Source, next.Scope).Select(summary => new ThreadItem(summary, theme) { IsSelected = selected.Contains(summary.ThreadId) }).ToList();
        CaptureActiveSession(); selectingSession = true;
        try
        {
            if (replace is not null && sessions.IndexOf(replace) is int index && index >= 0) sessions[index] = next;
            else sessions.Add(next);
            SessionTabs.Visibility = Visibility.Visible; ActivateSession(next);
        }
        finally { selectingSession = false; }
        int evicted = TrimClosedSessions(); UpdateMenus(); return evicted;
    }
    private static bool AnalysisFileFailure(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
        ArgumentException or InvalidOperationException or NotSupportedException or OutOfMemoryException or DecoderFallbackException;
    private static bool SameAnalysisSource(string? first, string? second) => first is null || second is null ? first == second :
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
}
