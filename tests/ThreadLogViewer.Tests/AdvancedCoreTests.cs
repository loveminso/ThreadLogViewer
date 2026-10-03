using System.Text.RegularExpressions;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class AdvancedCoreTests
{
    [Fact]
    public void SourceOffsetsPreserveMixedEndingsAndEntryBodies()
    {
        const string text = "preamble\r\n[10:00:00] [T3] header\nbody\r\r[10:00:01] [T7] last";
        var log = LogParser.Parse(text);
        Assert.Equal(new[] { 0, 10, 33, 38, 39, text.Length }, Enumerable.Range(0, log.Lines.Count + 1).Select(log.GetLineOffset));
        var entryRange = log.GetEntryRange(1);
        Assert.Equal("[10:00:00] [T3] header\nbody\r\r", text.Substring(entryRange.Offset, entryRange.Length));
        Assert.Equal(2, log.GetLineIndexAtOffset(log.GetLineOffset(2)));
        Assert.Equal(1, log.GetLineIndexAtOffset(log.GetLineOffset(2) - 1));
        Assert.Equal(-1, LogParser.Parse("").GetLineIndexAtOffset(0));
    }

    [Fact]
    public void FilterCombinesThreadsAndWholeEntryIncludeAndExcludeConditions()
    {
        const string first = "[10:00:00] [T3] timeout\r\nsecond phrase\n";
        const string excluded = "[10:00:01] [T3] timeout\nsecond phrase heartbeat\r";
        const string otherThread = "[10:00:02] [T7] timeout second phrase\n";
        var log = LogParser.Parse(first + excluded + otherThread);
        var view = LogProjection.CreateFiltered(log, [3], new(["timeout", "second phrase", "TIMEOUT", ""], ["heartbeat"], true));
        Assert.Equal(first, view.Text);
        Assert.Equal(1, view.EntryCount);
        Assert.Equal(2, view.Count);
        Assert.Equal(new[] { 0 }, view.EntryIndexes);
        Assert.Equal(new[] { 0, 1 }, view.SourceIndexes);
        Assert.Contains(3, view.SelectedThreads);
        Assert.Equal(2, LogProjection.CreateFiltered(log, [3, 7], new(["timeout", "missing"], ["heartbeat"])).EntryCount);
        Assert.Empty(LogProjection.CreateFiltered(log, [3, 7], new(["timeout", "missing"], [], true)).Text);
        Assert.Empty(LogProjection.CreateFiltered(log, [3], new(["TIMEOUT"], [], MatchCase: true)).Text);
        Assert.Equal(3, LogProjection.CreateFiltered(log, [3, 7], new([], [])).EntryCount);
    }

    [Fact]
    public void ExplicitEntriesDeduplicateAndKeepOriginalOrderAndActualCount()
    {
        var log = LogParser.Parse("[00:00:00] [T3] first\r\nbody\n[00:00:01] [T3] second\r[00:00:02] [T7] third");
        var view = LogProjection.CreateEntries(log, [2, 0, 2]);
        Assert.Equal(new[] { 0, 2 }, view.EntryIndexes);
        Assert.Equal(new[] { 0, 1, 3 }, view.SourceIndexes);
        Assert.Equal(2, view.EntryCount);
        Assert.Equal("[00:00:00] [T3] first\r\nbody\n[00:00:02] [T7] third", view.Text);
        Assert.Throws<ArgumentOutOfRangeException>(() => LogProjection.CreateEntries(log, [3]));
    }

    [Fact]
    public void NavigationHandlesHiddenLinesTieForwardAndGappedOffsets()
    {
        var log = LogParser.Parse(string.Join('\n', Enumerable.Range(1, 8).Select(i => $"[00:00:00] [T{(i is 2 or 6 ? 3 : 7)}] line {i}")));
        var view = LogProjection.Create(log, [3]);
        Assert.Equal(2, view.FindDisplayLine(6));
        Assert.Null(view.FindDisplayLine(4));
        Assert.Equal(2, view.FindDisplayLine(4, nearest: true));
        Assert.Equal(1, view.FindDisplayLine(3, nearest: true));
        Assert.Equal(2, view.FindDisplayLine(8, nearest: true));
        Assert.Null(view.FindDisplayLine(0, nearest: true));
        int sourceOffset = log.GetLineOffset(5) + 5;
        int displayOffset = view.GetDisplayOffset(sourceOffset)!.Value;
        Assert.Equal(sourceOffset, view.GetSourceOffset(displayOffset));
        Assert.Null(view.GetDisplayOffset(log.GetLineOffset(3)));
        Assert.Null(LogProjection.Create(log, []).FindDisplayLine(2, true));
    }

    [Fact]
    public void ContextUsesOwningRecordAndClipsInSourceOrder()
    {
        var log = LogParser.Parse("preamble\n[00:00:00] [T3] first\nbody\n[xx:yy:zz] [T7] invalid\n[00:00:00] [T9] same timestamp\n[00:00:01] [T3] last");
        var view = LogContext.Create(log, 2, 1);
        Assert.Equal(new[] { 0, 1, 2 }, view.EntryIndexes);
        Assert.Equal(new[] { 0, 1, 2, 3 }, view.SourceIndexes);
        Assert.Equal(2, LogContext.Create(log, log.Lines.Count - 1, 1).EntryCount);
        Assert.Equal(1, LogContext.Create(log, 2, 0).EntryCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => LogContext.Create(log, 0, -1));
    }

    [Fact]
    public void LocatedSearchHasCaseWordRegexAndBodyCoordinates()
    {
        var log = LogParser.Parse("[00:00:00] [T3] timeout TIMEOUT timeoutX\r\nbody retry 12 retry 345\n[00:00:01] [T7] hidden retry");
        var indexes = LogProjection.Create(log, [3]).EntryIndexes;
        Assert.Equal(2, LogSearch.Find(log, indexes, "timeout", new(WholeWord: true)).Hits.Length);
        Assert.Single(LogSearch.Find(log, indexes, "TIMEOUT", new(MatchCase: true)).Hits);
        var matches = LogSearch.Find(log, indexes, @"retry \d+", new(UseRegex: true));
        Assert.Equal(2, matches.Hits.Length);
        Assert.Equal(new[] { 8, 9 }, matches.Hits.Select(h => h.Length));
        Assert.All(matches.Hits, h => Assert.Equal(1, h.SourceLineIndex));
        Assert.Equal(6, matches.Hits[0].SourceColumn);
        Assert.Equal(0, matches.Hits[0].EntryIndex);
        Assert.Equal(log.GetLineOffset(1) + 5, matches.Hits[0].SourceOffset);
    }

    [Fact]
    public void WholeWordUsesKoreanCombiningMarksAndSupplementaryLetters()
    {
        var log = LogParser.Parse("[00:00:00] [T3] 한글 한글말 x한글 한글_x e\u0301 e\u20DD e\u00B2 e \U00010400x x \U00010400\n");
        Assert.Single(LogSearch.Find(log, [0], "한글", new(WholeWord: true)).Hits);
        Assert.Single(LogSearch.Find(log, [0], "e", new(WholeWord: true)).Hits);
        Assert.Single(LogSearch.Find(log, [0], "x", new(WholeWord: true)).Hits);
        Assert.Single(LogSearch.Find(log, [0], "\U00010400", new(WholeWord: true)).Hits);
    }

    [Fact]
    public void SearchCannotBridgeEntriesOrGappedSelectionsButCanSpanOneRecord()
    {
        var log = LogParser.Parse("[00:00:00] [T3] alpha\nbody beta\n[00:00:01] [T7] hidden\n[00:00:02] [T3] gamma\n");
        Assert.Single(LogSearch.Find(log, [0], "alpha\nbody", new()).Hits);
        Assert.Empty(LogSearch.Find(log, [0, 2], "beta\n[00:00:02]", new()).Hits);
        Assert.Empty(LogSearch.Find(log, [0, 1, 2], @"beta\n\[00:00:01\]", new(UseRegex: true)).Hits);
        int alpha = log.Text.IndexOf("alpha", StringComparison.Ordinal);
        int body = log.Text.IndexOf("body", StringComparison.Ordinal);
        SourceTextRange[] ranges = [new(alpha, 5), new(body, 9)];
        Assert.Empty(LogSearch.Find(log, [0], "alpha\nbody", new(), ranges).Hits);
        Assert.Single(LogSearch.Find(log, [0], "beta", new(), ranges).Hits);
        Assert.Empty(LogSearch.Find(log, [0], "alpha", new(), []).Hits);
        Assert.Single(LogSearch.Find(log, [0], "alpha", new(), [new(alpha, 5), new(alpha + 1, 3)]).Hits);
    }

    [Fact]
    public void RegexErrorsAndTimeoutAreExplicitAndZeroLengthHitsRemainInList()
    {
        var log = LogParser.Parse("[00:00:00] [T3] " + new string('a', 30_000) + "!");
        Assert.Throws<RegexParseException>(() => LogSearch.Find(log, [0], "[", new(UseRegex: true)));
        Assert.Equal(30_000, LogSearch.Find(log, [0], @"(?=a)", new(UseRegex: true)).Hits.Length);
        Assert.Throws<RegexMatchTimeoutException>(() => LogSearch.Find(log, [0], @"(a+)+$", new(UseRegex: true)));
    }

    [Fact]
    public void RegexDefaultAnchorsDoNotEnableMultilineAndZeroEndMatchesKeepOwner()
    {
        var log = LogParser.Parse("[00:00:00] [T3] header\nbody\n[00:00:01] [T7] next\n");
        Assert.Empty(LogSearch.Find(log, [0], "^body", new(UseRegex: true)).Hits);
        Assert.Single(LogSearch.Find(log, [0], "(?m)^body", new(UseRegex: true)).Hits);
        var firstEnd = Assert.Single(LogSearch.Find(log, [0], @"\z", new(UseRegex: true)).Hits);
        Assert.Equal(0, firstEnd.Length);
        Assert.Equal(0, firstEnd.EntryIndex);
        Assert.Equal(1, firstEnd.SourceLineIndex);
        Assert.Equal(5, firstEnd.SourceColumn);
        Assert.Equal(log.GetLineOffset(2), firstEnd.SourceOffset);
        var finalEnd = Assert.Single(LogSearch.Find(log, [1], @"\z", new(UseRegex: true)).Hits);
        Assert.Equal(2, finalEnd.SourceLineIndex);
        Assert.Equal(log.Lines[2].RawText.Length + 1, finalEnd.SourceColumn);
        Assert.Equal(log.Text.Length, finalEnd.SourceOffset);
    }

    [Fact]
    public void SearchCapReportsOnlyAdditionalActualHitsAndHonorsCancellation()
    {
        var exact = LogParser.Parse("[00:00:00] [T3] " + string.Concat(Enumerable.Repeat("z ", LogSearch.MaxHighlights)));
        var exactResult = LogSearch.Find(exact, [0], "z", new());
        Assert.Equal(LogSearch.MaxHighlights, exactResult.Hits.Length);
        Assert.False(exactResult.Limited);
        var excess = LogParser.Parse(exact.Text + "z");
        Assert.True(LogSearch.Find(excess, [0], "z", new()).Limited);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => LogSearch.Find(exact, [0], "z", new(), cancellationToken: cancel.Token));
        Assert.Throws<OperationCanceledException>(() => LogProjection.CreateFiltered(exact, [3], new([], []), cancel.Token));
        Assert.Throws<OperationCanceledException>(() => LogContext.Create(exact, 0, 0, cancel.Token));
    }

    [Fact]
    public void TimeAnchorsResolveOwnHeaderAndNeverBorrowAcrossInvalidHeaders()
    {
        var log = LogParser.Parse("preamble\n[014:15:50.113] [T3] start\nbody\n[014:15:50.125] [T7] end\n[xx:yy:zz] [T3] invalid\ninvalid body");
        var a = LogTimeAnalysis.ResolveAnchor(log, 2)!;
        var b = LogTimeAnalysis.ResolveAnchor(log, 3)!;
        Assert.Equal(1, a.HeaderLineIndex);
        Assert.Equal(2, a.SourceLineIndex);
        Assert.Equal("014:15:50.113", a.TimestampText);
        Assert.Equal(120_000, LogTimeAnalysis.DifferenceTicks(a, b));
        Assert.Equal(-120_000, LogTimeAnalysis.DifferenceTicks(b, a));
        Assert.Equal("+00:00:00.012 · 12 ms", LogTimeAnalysis.FormatTicks(LogTimeAnalysis.DifferenceTicks(a, b)));
        Assert.Null(LogTimeAnalysis.ResolveAnchor(log, 0));
        Assert.Null(LogTimeAnalysis.ResolveAnchor(log, 5));
        Assert.Null(LogTimeAnalysis.ResolveAnchor(log, log.Lines.Count));
    }

    [Fact]
    public void TimeCalculationsKeepOneTickMaxDurationAndUnadjustedMidnight()
    {
        var log = LogParser.Parse("[00:00:00] [T3] zero\n[00:00:00.0000001] [T3] tick\n[256204778:48:05.4775807] [T3] max\n[23:59:59] [T3] before\n[00:00:00] [T3] after");
        Assert.Equal(1, LogTimeAnalysis.DifferenceTicks(LogTimeAnalysis.ResolveAnchor(log, 0)!, LogTimeAnalysis.ResolveAnchor(log, 1)!));
        Assert.Equal(long.MaxValue, LogTimeAnalysis.ResolveAnchor(log, 2)!.Ticks);
        Assert.Equal("+00:00:00.0000001 · 0.0001 ms", LogTimeAnalysis.FormatTicks(1));
        Assert.StartsWith("+256204778:48:05.4775807", LogTimeAnalysis.FormatTicks(long.MaxValue));
        Assert.Equal(-863990000000, LogTimeAnalysis.DifferenceTicks(LogTimeAnalysis.ResolveAnchor(log, 3)!, LogTimeAnalysis.ResolveAnchor(log, 4)!));
        Assert.StartsWith("−23:59:59.000", LogTimeAnalysis.FormatTicks(-863990000000));
        Assert.StartsWith("−256204778:48:05.4775808", LogTimeAnalysis.FormatTicks(long.MinValue));
    }
}
