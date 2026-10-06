using System.IO;
using System.Text;
using System.Windows;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private async void RefreshSession_Click(object sender, RoutedEventArgs e) => await RefreshCurrentSessionAsync();

    private Task RefreshCurrentSessionAsync()
    {
        if (!CanRefreshCurrentSession || activeSession is not { } session) return Task.CompletedTask;
        string path = session.Source.SourcePath!;
        var encoding = session.EncodingMode;
        return RefreshSessionAsync((token, progress) => LogFileReader.ReadAsync(path, encoding, token, progress));
    }

    // Injection is confined to synthetic tests; the normal command reads the current source path only.
    private async Task RefreshSessionAsync(Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load)
    {
        if (!CanRefreshCurrentSession || activeSession is not { } previous) return;
        if (busy) RestoreFilters();
        CaptureActiveSession();
        var snapshot = CopySessionState(previous, previous.Source, previous.View, previous.Document);
        var selected = previous.Threads.Where(thread => thread.IsSelected).Select(thread => thread.Id).ToHashSet();
        bool allSelected = previous.Source.Threads.All(thread => selected.Contains(thread.ThreadId));
        var op = BeginWork("파일 새로 고침 준비…", true);
        try
        {
            var prepared = await Task.Run(async () =>
            {
                var updated = await load(op.Token, op.Progress);
                op.Token.ThrowIfCancellationRequested();
                var map = SourceLineRemap.Create(snapshot.Source, updated, op.Token);
                var selectedNext = updated.Threads.Where(thread => allSelected || selected.Contains(thread.ThreadId))
                    .Select(thread => thread.ThreadId).ToHashSet();
                int? center = snapshot.ContextLine is { } oldCenter ? map.Map(oldCenter) : null;
                bool context = snapshot.Context && center.HasValue;
                int radius = int.TryParse(snapshot.ContextRadius, out int value) ? Math.Clamp(value, 0, 10000) : 20;
                var view = context ? LogContext.Create(updated, center!.Value, radius, op.Token) :
                    LogProjection.CreateFiltered(updated, selectedNext, snapshot.Filter, op.Token, op.Progress);
                var next = CopySessionState(snapshot, updated, view, PrepareDocument(view.Text, op.Token, op.Progress));
                int removed = RemapSessionState(snapshot, next, map);
                bool relocated = snapshot.Position is { } previousPosition && map.Map(previousPosition.SourceLine) is null && next.Position is not null;
                next.Context = context;
                next.NormalThreads = context ? selectedNext : null;
                if (context)
                {
                    next.ContextLine = center;
                    int centerEntry = updated.Lines[center!.Value].EntryIndex;
                    next.ContextEntry = centerEntry;
                    int preceding = view.EntryIndexes.Count(index => index < centerEntry);
                    int following = view.EntryIndexes.Count(index => index > centerEntry);
                    next.ContextText = $"주변 로그 보기 중 · 기준: 원본 {center.Value + 1:N0}줄 · 앞 {preceding:N0}기록 / 뒤 {following:N0}기록 · 모든 스레드";
                    next.ContextTip = "새로 고친 원본에서 내용과 소유 헤더가 같은 기준 기록을 복원했습니다.";
                    next.ContextInput = $"원본 {center.Value + 1:N0}줄을 기준으로 표시했습니다.";
                }
                else { next.ContextLine = next.ContextEntry = null; next.NormalPosition = null; }
                op.Token.ThrowIfCancellationRequested();
                return (next, map, selectedNext, removed, relocated);
            }, op.Token);
            op.Token.ThrowIfCancellationRequested();
            if (!work.IsCurrent(op.Version) || activeSession != previous || !sessions.Contains(previous)) return;
            var next = prepared.next;
            next.Threads = next.Source.Threads.Select(summary => new ThreadItem(summary, theme)
                { IsSelected = prepared.selectedNext.Contains(summary.ThreadId) }).ToList();
            int historyCount = next.History.Back.Count + next.History.Forward.Count;
            RemapNavigationHistory(next, prepared.map);
            int removed = prepared.removed + historyCount - next.History.Back.Count - next.History.Forward.Count;
            selectingSession = true;
            try
            {
                sessions[sessions.IndexOf(previous)] = next;
                ActivateSession(next);
            }
            finally { selectingSession = false; }
            closedSessions.Trim(sessions);
            OperationStatus.Text = "파일 새로 고침 완료 · 인코딩과 필터·검색·강조 유지" +
                (prepared.relocated ? " · 이전 커서 위치가 없어 가까운 확인된 원본 줄로 이동했습니다." : "") +
                (removed > 0 ? $" · 내용 변경·삭제 또는 중복으로 위치 {removed:N0}개를 정확히 복원하지 않았습니다. 필요한 위치를 다시 지정하세요." : " · 확인된 원본 위치 복원");
            UpdateMenus();
        }
        catch (OperationCanceledException)
        { if (work.IsCurrent(op.Version)) { RestoreFilters(); OperationStatus.Text = "파일 새로 고침 취소 · 이전 원문과 분석 상태 유지"; } }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or DecoderFallbackException or
            ArgumentException or OutOfMemoryException or NotSupportedException)
        { if (work.IsCurrent(op.Version)) { RestoreFilters(); ShowError("파일 새로 고침 실패", ex); } }
        finally { FinishWork(op.Version); }
    }

    private static LogSession CopySessionState(LogSession old, LogData source, LogProjection view,
        ICSharpCode.AvalonEdit.Document.TextDocument document) => new(source, view, document, old.DisplayTitle, old.Scope, old.SourceTitle, old.IsBlank)
    {
        EncodingMode = old.EncodingMode, History = old.History, ThreadSearchQuery = old.ThreadSearchQuery,
        Bookmarks = old.Bookmarks.ToArray(), Position = old.Position, EmptyPosition = old.EmptyPosition,
        SelectionStart = old.SelectionStart, SelectionLength = old.SelectionLength, WholeLineSelection = old.WholeLineSelection?.ToArray(),
        NormalPosition = old.NormalPosition, NormalThreads = old.NormalThreads?.ToHashSet(), SeparationStart = old.SeparationStart,
        Filter = old.Filter, Includes = old.Includes, Excludes = old.Excludes, IncludeAll = old.IncludeAll, FilterCase = old.FilterCase,
        TimeA = old.TimeA, TimeB = old.TimeB, Keywords = old.Keywords.Select(rule =>
            new KeywordRuleItem(rule.Keyword, rule.ColorIndex) { Enabled = rule.Enabled }).ToArray(),
        KeywordInput = old.KeywordInput, KeywordColor = old.KeywordColor, Context = old.Context, ContextLine = old.ContextLine,
        ContextEntry = old.ContextEntry, ContextText = old.ContextText, ContextTip = old.ContextTip, ContextInput = old.ContextInput,
        ContextRadius = old.ContextRadius, Query = old.Query, SearchCase = old.SearchCase, SearchWord = old.SearchWord,
        SearchRegex = old.SearchRegex, SearchScope = old.SearchScope, SearchRanges = old.SearchRanges?.ToArray(), LastHit = old.LastHit,
        SearchVisible = old.SearchVisible, AnalysisExpanded = old.AnalysisExpanded, AnalysisTool = old.AnalysisTool,
        BookmarksExpanded = old.BookmarksExpanded, FilterExpanded = old.FilterExpanded
    };

    private static int RemapSessionState(LogSession old, LogSession next, SourceLineRemap map)
    {
        int removed = 0;
        int? Line(int? index)
        {
            if (index is null) return null;
            var result = map.Map(index.Value); if (result is null) removed++;
            return result;
        }
        PositionAnchor? Position(PositionAnchor? anchor)
        {
            if (anchor is null) return null;
            int? mapped = Line(anchor.SourceLine);
            if ((mapped ?? map.FindNearestMappedLine(anchor.SourceLine)) is not { } line) return null;
            return anchor with { SourceLine = line, Column = mapped.HasValue ? anchor.Column : 1,
                TopSourceLine = Line(anchor.TopSourceLine) ?? line };
        }
        next.Position = Position(old.Position); next.EmptyPosition = Position(old.EmptyPosition);
        next.NormalPosition = Position(old.NormalPosition); next.SeparationStart = Line(old.SeparationStart);
        next.Bookmarks = old.Bookmarks.Select(bookmark => (bookmark, line: Line(bookmark.SourceLineIndex)))
            .Where(pair => pair.line.HasValue).Select(pair => pair.bookmark with { SourceLineIndex = pair.line!.Value }).OrderBy(bookmark => bookmark.SourceLineIndex).ToArray();
        next.TimeA = old.TimeA is { } a && Line(a.SourceLineIndex) is { } aLine ? LogTimeAnalysis.ResolveAnchor(next.Source, aLine) : null;
        next.TimeB = old.TimeB is { } b && Line(b.SourceLineIndex) is { } bLine ? LogTimeAnalysis.ResolveAnchor(next.Source, bLine) : null;
        if (next.TimeA is null && next.TimeB is not null) { next.TimeB = null; removed++; }
        next.WholeLineSelection = old.WholeLineSelection?.Select(index => Line(index)).Where(index => index.HasValue).Select(index => index!.Value).ToArray();
        if (old.Context && old.ContextLine is { } context && map.Map(context) is null) removed++;
        next.SearchRanges = old.SearchRanges?.Select(range =>
        { var mapped = map.MapRange(range); if (mapped is null) removed++; return mapped; })
            .Where(range => range.HasValue).Select(range => range!.Value).ToArray();
        next.LastHit = null;
        if (old.LastHit is { } hit)
        {
            if (map.MapRange(new(hit.SourceOffset, hit.Length)) is { } range)
            {
                int line = next.Source.GetLineIndexAtOffset(range.Offset);
                next.LastHit = new(range.Offset, range.Length, line, range.Offset - next.Source.GetLineOffset(line) + 1, next.Source.Lines[line].EntryIndex);
            }
            else removed++;
        }
        next.SelectionStart = 0; next.SelectionLength = 0;
        if (next.Position is { } caret && next.View.FindDisplayLine(caret.SourceLine + 1) is { } display)
            next.SelectionStart = next.View.DisplayOffsets[display - 1] + Math.Clamp(caret.Column - 1, 0, next.Source.Lines[caret.SourceLine].RawText.Length);
        if (old.WholeLineSelection is null && old.SelectionLength > 0)
        {
            var ranges = SelectionSourceRanges.Map(old.View, [new(old.SelectionStart, old.SelectionLength)]);
            int first = -1, end = -1; bool valid = true;
            foreach (var oldRange in ranges)
            {
                if (map.MapRange(oldRange) is not { } range || next.View.GetDisplayOffset(range.Offset) is not { } start) { valid = false; break; }
                int line = next.Source.GetLineIndexAtOffset(range.Offset + range.Length - 1);
                if (next.View.FindDisplayLine(line + 1) is not { } lastDisplay) { valid = false; break; }
                int last = next.View.DisplayOffsets[lastDisplay - 1] + range.Offset + range.Length - next.Source.GetLineOffset(line);
                if (first < 0) first = start; else if (start != end) { valid = false; break; }
                end = last;
            }
            if (valid && first >= 0 && end >= first && old.View.Text.AsSpan(old.SelectionStart, old.SelectionLength)
                .SequenceEqual(next.View.Text.AsSpan(first, end - first)))
            { next.SelectionStart = first; next.SelectionLength = end - first; }
            else removed++;
        }
        return removed;
    }
}
