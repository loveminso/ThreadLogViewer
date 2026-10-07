using System.IO;
using System.Text;
using System.Windows;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    // Synthetic tests can pause this second background phase without relying on timing or large data.
    private Func<CancellationToken, Task>? refreshMapCheckpoint = null;
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
                var document = PrepareDocument(view.Text, op.Token, op.Progress);
                int preceding = 0, following = 0;
                if (context)
                {
                    int centerEntry = updated.Lines[center!.Value].EntryIndex;
                    preceding = view.EntryIndexes.Count(index => index < centerEntry);
                    following = view.EntryIndexes.Count(index => index > centerEntry);
                }
                op.Token.ThrowIfCancellationRequested();
                return (updated, view, document, map, selectedNext, context, center, preceding, following);
            }, op.Token);
            LogSession next;
            int removed;
            bool relocated;
            while (true)
            {
                op.Token.ThrowIfCancellationRequested();
                if (!work.IsCurrent(op.Version) || !viewReady || activeSession != previous || data != snapshot.Source || !sessions.Contains(previous)) return;
                CaptureActiveSession();
                var latest = CopySessionState(previous, previous.Source, previous.View, previous.Document);
                var back = previous.History.Back.ToArray();
                var forward = previous.History.Forward.ToArray();
                var checkpoint = refreshMapCheckpoint;
                var restored = await Task.Run(async () =>
                {
                    if (checkpoint is not null) await checkpoint(op.Token);
                    op.Token.ThrowIfCancellationRequested();
                    op.Progress.Report(new("분석 상태 위치 복원", 0));
                    var target = CopySessionState(latest, prepared.updated, prepared.view, prepared.document);
                    int dropped = RemapSessionState(latest, target, prepared.map, op.Token);
                    target.Context = prepared.context;
                    target.NormalThreads = prepared.context ? prepared.selectedNext : null;
                    if (prepared.context)
                    {
                        target.ContextLine = prepared.center;
                        target.ContextEntry = prepared.updated.Lines[prepared.center!.Value].EntryIndex;
                        target.ContextText = $"주변 로그 보기 중 · 기준: 원본 {prepared.center.Value + 1:N0}줄 · 앞 {prepared.preceding:N0}기록 / 뒤 {prepared.following:N0}기록 · 모든 스레드";
                        target.ContextTip = "새로 고친 원본에서 내용과 소유 헤더가 같은 기준 기록을 복원했습니다.";
                        target.ContextInput = $"원본 {prepared.center.Value + 1:N0}줄을 기준으로 표시했습니다.";
                    }
                    else { target.ContextLine = target.ContextEntry = null; target.NormalPosition = null; }
                    op.Token.ThrowIfCancellationRequested();
                    return (target, dropped);
                }, op.Token);
                op.Token.ThrowIfCancellationRequested();
                if (!work.IsCurrent(op.Version) || !viewReady || activeSession != previous || data != snapshot.Source || !sessions.Contains(previous)) return;
                CaptureActiveSession();
                // Mapping may be expensive. An input made during that await belongs to this tab too.
                // Reuse the already prepared immutable source/view and map the newest state again.
                if (!SameRefreshState(latest, previous, back, forward)) continue;
                next = restored.target;
                removed = restored.dropped;
                relocated = latest.Position is { } position && prepared.map.Map(position.SourceLine) is null && next.Position is not null;
                break;
            }
            next.Threads = next.Source.Threads.Select(summary => new ThreadItem(summary, theme)
                { IsSelected = prepared.selectedNext.Contains(summary.ThreadId) }).ToList();
            int historyCount = next.History.Back.Count + next.History.Forward.Count;
            RemapNavigationHistory(next, prepared.map);
            removed += historyCount - next.History.Back.Count - next.History.Forward.Count;
            selectingSession = true;
            try
            {
                sessions[sessions.IndexOf(previous)] = next;
                ActivateSession(next);
            }
            finally { selectingSession = false; }
            int evicted = TrimClosedSessions();
            OperationStatus.Text = "파일 새로 고침 완료 · 인코딩과 필터·검색·강조 유지" +
                (relocated ? " · 이전 커서 위치가 없어 가까운 확인된 원본 줄로 이동했습니다." : "") +
                (removed > 0 ? $" · 내용 변경·삭제 또는 중복으로 위치 {removed:N0}개를 정확히 복원하지 않았습니다. 필요한 위치를 다시 지정하세요." : " · 확인된 원본 위치 복원") +
                ClosedRetentionNotice(evicted);
            UpdateMenus();
        }
        catch (OperationCanceledException)
        { if (work.IsCurrent(op.Version)) { RestoreFilters(); OperationStatus.Text = "파일 새로 고침 취소 · 이전 원문과 분석 상태 유지"; } }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or DecoderFallbackException or
            ArgumentException or OutOfMemoryException or NotSupportedException)
        { if (work.IsCurrent(op.Version)) { RestoreFilters(); ShowError("파일 새로 고침 실패", ex); } }
        finally { FinishWork(op.Version); }
    }

    private static bool SameRefreshState(LogSession before, LogSession current, NavigationPoint[] back, NavigationPoint[] forward) =>
        before.View == current.View && before.Document == current.Document && before.Position == current.Position &&
        before.EmptyPosition == current.EmptyPosition && before.NormalPosition == current.NormalPosition &&
        before.SelectionStart == current.SelectionStart && before.SelectionLength == current.SelectionLength &&
        SameItems(before.WholeLineSelection, current.WholeLineSelection) &&
        (before.NormalThreads is null ? current.NormalThreads is null : current.NormalThreads is not null && before.NormalThreads.SetEquals(current.NormalThreads)) &&
        before.TimeA == current.TimeA && before.TimeB == current.TimeB &&
        before.SeparationStart == current.SeparationStart && before.Filter == current.Filter &&
        before.Includes == current.Includes && before.Excludes == current.Excludes && before.IncludeAll == current.IncludeAll && before.FilterCase == current.FilterCase &&
        before.Keywords.Select(rule => (rule.Keyword, rule.ColorIndex, rule.Enabled)).SequenceEqual(current.Keywords.Select(rule => (rule.Keyword, rule.ColorIndex, rule.Enabled))) &&
        before.KeywordInput == current.KeywordInput && before.KeywordColor == current.KeywordColor && before.ThreadSearchQuery == current.ThreadSearchQuery &&
        before.Context == current.Context && before.ContextLine == current.ContextLine && before.ContextEntry == current.ContextEntry &&
        before.ContextText == current.ContextText && Equals(before.ContextTip, current.ContextTip) && before.ContextInput == current.ContextInput && before.ContextRadius == current.ContextRadius &&
        before.Query == current.Query && before.SearchCase == current.SearchCase && before.SearchWord == current.SearchWord && before.SearchRegex == current.SearchRegex &&
        before.SearchScope == current.SearchScope && SameItems(before.SearchRanges, current.SearchRanges) && before.LastHit == current.LastHit && before.SearchVisible == current.SearchVisible &&
        before.AnalysisExpanded == current.AnalysisExpanded && before.AnalysisTool == current.AnalysisTool && before.FilterExpanded == current.FilterExpanded &&
        back.SequenceEqual(current.History.Back) && forward.SequenceEqual(current.History.Forward);

    private static bool SameItems<T>(IEnumerable<T>? first, IEnumerable<T>? second) =>
        first is null ? second is null : second is not null && first.SequenceEqual(second);

    private static LogSession CopySessionState(LogSession old, LogData source, LogProjection view,
        ICSharpCode.AvalonEdit.Document.TextDocument document) => new(source, view, document, old.DisplayTitle, old.Scope, old.SourceTitle, old.IsBlank)
    {
        EncodingMode = old.EncodingMode, History = old.History, ThreadSearchQuery = old.ThreadSearchQuery,
        Position = old.Position, EmptyPosition = old.EmptyPosition,
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
        FilterExpanded = old.FilterExpanded
    };

    private static int RemapSessionState(LogSession old, LogSession next, SourceLineRemap map, CancellationToken token = default)
    {
        int removed = 0;
        int? Line(int? index)
        {
            token.ThrowIfCancellationRequested();
            if (index is null) return null;
            var result = map.Map(index.Value); if (result is null) removed++;
            return result;
        }
        PositionAnchor? Position(PositionAnchor? anchor)
        {
            if (anchor is null) return null;
            int? mapped = Line(anchor.SourceLine);
            if ((mapped ?? map.FindNearestMappedLine(anchor.SourceLine, token)) is not { } line) return null;
            return anchor with { SourceLine = line, Column = mapped.HasValue ? anchor.Column : 1,
                TopSourceLine = Line(anchor.TopSourceLine) ?? line };
        }
        next.Position = Position(old.Position); next.EmptyPosition = Position(old.EmptyPosition);
        next.NormalPosition = Position(old.NormalPosition); next.SeparationStart = Line(old.SeparationStart);
        next.TimeA = old.TimeA is { } a && Line(a.SourceLineIndex) is { } aLine ? LogTimeAnalysis.ResolveAnchor(next.Source, aLine) : null;
        next.TimeB = old.TimeB is { } b && Line(b.SourceLineIndex) is { } bLine ? LogTimeAnalysis.ResolveAnchor(next.Source, bLine) : null;
        if (next.TimeA is null && next.TimeB is not null) { next.TimeB = null; removed++; }
        next.WholeLineSelection = old.WholeLineSelection?.Select(index => Line(index)).Where(index => index.HasValue).Select(index => index!.Value).ToArray();
        if (old.Context && old.ContextLine is { } context && map.Map(context) is null) removed++;
        next.SearchRanges = old.SearchRanges?.Select(range =>
        { var mapped = map.MapRange(range, token); if (mapped is null) removed++; return mapped; })
            .Where(range => range.HasValue).Select(range => range!.Value).ToArray();
        next.LastHit = null;
        if (old.LastHit is { } hit)
        {
            if (map.MapRange(new(hit.SourceOffset, hit.Length), token) is { } range)
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
                token.ThrowIfCancellationRequested();
                if (map.MapRange(oldRange, token) is not { } range || next.View.GetDisplayOffset(range.Offset) is not { } start) { valid = false; break; }
                int line = next.Source.GetLineIndexAtOffset(range.Offset + range.Length - 1);
                if (next.View.FindDisplayLine(line + 1) is not { } lastDisplay) { valid = false; break; }
                int last = next.View.DisplayOffsets[lastDisplay - 1] + range.Offset + range.Length - next.Source.GetLineOffset(line);
                if (first < 0) first = start; else if (start != end) { valid = false; break; }
                end = last;
            }
            if (valid && first >= 0 && end >= first && EqualSelection(old.View.Text, old.SelectionStart, old.SelectionLength,
                next.View.Text, first, end - first, token))
            { next.SelectionStart = first; next.SelectionLength = end - first; }
            else removed++;
        }
        token.ThrowIfCancellationRequested();
        return removed;
    }

    private static bool EqualSelection(string oldText, int oldStart, int oldLength, string newText, int newStart, int newLength, CancellationToken token)
    {
        if (oldLength != newLength) return false;
        for (int offset = 0; offset < oldLength; offset += 65536)
        {
            token.ThrowIfCancellationRequested();
            int length = Math.Min(65536, oldLength - offset);
            if (!oldText.AsSpan(oldStart + offset, length).SequenceEqual(newText.AsSpan(newStart + offset, length))) return false;
        }
        return true;
    }
}
