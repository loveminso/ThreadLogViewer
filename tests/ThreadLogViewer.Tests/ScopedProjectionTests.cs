using System.IO;
using System.Text;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class ScopedProjectionTests
{
    private const string Original = "synthetic prefix\r\n[025:00:01.123] [T3] HEADER_ONLY first\r\nbody INCLUDED first\rbody BLOCK_OUTSIDE\n[025:00:02] [T7] HEADER7 second\nbody INCLUDED middle\r\n[026:00:00] [T3] HEADER3 final\rbody final without newline";

    [Fact]
    public void PhysicalRangeSharesSourceAndKeepsOriginalNumbersOffsetsAndOwningHeader()
    {
        var source = LogParser.Parse(Original, "synthetic-source.log");
        var scope = new LogLineRange(2, 5);
        var view = LogProjection.CreateLineRange(source, scope);
        Assert.Same(source, view.Source);
        Assert.Equal(new[] { 2, 3, 4, 5 }, view.SourceIndexes);
        Assert.Equal(new[] { 1, 2 }, view.EntryIndexes);
        Assert.Equal(new[] { 3, 4, 5, 6 }, Enumerable.Range(1, view.Count).Select(x => view.AtDisplayLine(x)!.Value.OriginalLineNumber));
        Assert.Equal("body INCLUDED first\rbody BLOCK_OUTSIDE\n[025:00:02] [T7] HEADER7 second\nbody INCLUDED middle\r\n", view.Text);
        Assert.DoesNotContain("HEADER_ONLY", view.Text);
        Assert.DoesNotContain("HEADER3", view.Text);
        Assert.Equal(1, view.AtDisplayLine(1)!.Value.EntryIndex);
        Assert.Equal(ParseQuality.Continuation, view.AtDisplayLine(1)!.Value.Quality);
        var anchor = LogTimeAnalysis.ResolveAnchor(source, view.SourceIndexes[0]);
        Assert.NotNull(anchor);
        Assert.Equal(1, anchor.HeaderLineIndex);
        Assert.Equal("025:00:01.123", anchor.TimestampText);
        Assert.Equal(5, view.GetDisplayOffset(source.GetLineOffset(2) + 5));
        Assert.Equal(source.GetLineOffset(2) + 5, view.GetSourceOffset(5));
        Assert.Null(view.GetDisplayOffset(source.GetLineOffset(1)));
        Assert.Null(view.FindDisplayLine(2));
        Assert.Equal(Original, source.Text);
    }

    [Theory]
    [InlineData(2, 2, "body INCLUDED first\r")]
    [InlineData(5, 5, "body INCLUDED middle\r\n")]
    [InlineData(6, 6, "[026:00:00] [T3] HEADER3 final\r")]
    [InlineData(7, 7, "body final without newline")]
    public void SinglePhysicalLineNeverExpandsToHeaderOrFollowingBodyAndPreservesItsEnding(int first, int last, string expected)
    {
        var source = LogParser.Parse(Original);
        var view = LogProjection.CreateLineRange(source, new(first, last));
        Assert.Equal(expected, view.Text);
        Assert.Equal(first, Assert.Single(view.SourceIndexes));
        Assert.Equal(first + 1, view.AtDisplayLine(1)!.Value.OriginalLineNumber);
        Assert.Single(view.EntryIndexes);
    }

    [Theory]
    [InlineData("synthetic\r\n\r\ntail\n", 1, "\r\n")]
    [InlineData("synthetic\n\ntail", 1, "\n")]
    public void BlankPhysicalLineIsPreservedWithoutInventingAnotherLine(string original, int line, string expected)
    {
        var source = LogParser.Parse(original);
        var view = LogProjection.CreateLineRange(source, new(line, line));
        Assert.Equal(expected, view.Text);
        Assert.Single(view.SourceIndexes);
        Assert.Null(view.AtDisplayLine(2));
    }

    [Fact]
    public void FullPhysicalRangeRetainsOriginalTextAndOlderUnscopedCallsRemainWholeEntries()
    {
        var source = LogParser.Parse(Original);
        var range = LogProjection.CreateLineRange(source, new(0, source.Lines.Count - 1));
        Assert.Same(source.Text, range.Text);
        Assert.Equal(Enumerable.Range(0, source.Lines.Count), range.SourceIndexes);
        var legacy = LogProjection.CreateFiltered(source, [3], new(["HEADER_ONLY"], []));
        Assert.Equal(new[] { 1, 2, 3 }, legacy.SourceIndexes);
        Assert.Contains("HEADER_ONLY", legacy.Text);
        Assert.Contains("BLOCK_OUTSIDE", legacy.Text);
        Assert.Equal(new[] { 1, 2, 3 }, LogContext.Create(source, 2, 0).SourceIndexes);
        Assert.Equal(Original, LogProjection.Create(source, source.Threads.Select(x => x.ThreadId)).Text);
    }

    [Fact]
    public void ScopedFiltersMatchOnlyIntersectingTextAndPreserveTheThreadSelection()
    {
        var source = LogParser.Parse(Original);
        var oneBodyLine = new LogLineRange(2, 2);
        Assert.Empty(LogProjection.CreateFiltered(source, [3], new(["HEADER_ONLY"], []), lineRange: oneBodyLine).Text);
        var body = LogProjection.CreateFiltered(source, [3], new(["INCLUDED"], ["BLOCK_OUTSIDE"]), lineRange: oneBodyLine);
        Assert.Equal("body INCLUDED first\r", body.Text);
        Assert.Equal(new[] { 2 }, body.SourceIndexes);
        Assert.Equal(1, Assert.Single(body.EntryIndexes));
        var noThreads = LogProjection.CreateFiltered(source, [7], EntryFilter.Empty, lineRange: oneBodyLine);
        Assert.Empty(noThreads.SourceIndexes);
        Assert.Empty(noThreads.EntryIndexes);
        Assert.True(noThreads.SelectedThreads.SetEquals([7]));

        var scope = new LogLineRange(2, 5);
        var both = LogProjection.CreateFiltered(source, [null, 3, 7], new(["INCLUDED"], ["HEADER_ONLY"]), lineRange: scope);
        Assert.Equal(new[] { 2, 3, 4, 5 }, both.SourceIndexes);
        Assert.Equal(new[] { 1, 2 }, both.EntryIndexes);
        Assert.True(both.SelectedThreads.SetEquals([null, 3, 7]));
        var onlyFirst = LogProjection.CreateFiltered(source, [3, 7], new(["INCLUDED", "first"], [], true), lineRange: scope);
        Assert.Equal(new[] { 2, 3 }, onlyFirst.SourceIndexes);
        var excludeVisibleHeader = LogProjection.CreateFiltered(source, [3, 7], new([], ["HEADER7"]), lineRange: scope);
        Assert.Equal(new[] { 2, 3 }, excludeVisibleHeader.SourceIndexes);
    }

    [Fact]
    public void ScopedEntryProjectionDropsOutsideEntriesAndNeverConcatenatesTheirText()
    {
        var source = LogParser.Parse(Original);
        var scope = new LogLineRange(2, 5);
        var view = LogProjection.CreateEntries(source, [3, 2, 0, 1, 2], lineRange: scope);
        Assert.Equal(new[] { 1, 2 }, view.EntryIndexes);
        Assert.Equal(new[] { 2, 3, 4, 5 }, view.SourceIndexes);
        Assert.Equal(LogProjection.CreateLineRange(source, scope).Text, view.Text);
        var empty = LogProjection.CreateEntries(source, [0, 3], lineRange: scope);
        Assert.Empty(empty.EntryIndexes);
        Assert.Empty(empty.SourceIndexes);
        Assert.Empty(empty.Text);
    }

    [Fact]
    public void ScopedContextClipsRecordsAndRejectsCentersOutsideThePhysicalRange()
    {
        var source = LogParser.Parse(Original);
        var scope = new LogLineRange(2, 5);
        var first = LogContext.Create(source, 2, 0, lineRange: scope);
        Assert.Equal(new[] { 2, 3 }, first.SourceIndexes);
        Assert.DoesNotContain("HEADER_ONLY", first.Text);
        Assert.Equal(new[] { 2, 3, 4, 5 }, LogContext.Create(source, 2, int.MaxValue, lineRange: scope).SourceIndexes);
        Assert.Equal(new[] { 4, 5 }, LogContext.Create(source, 5, 0, lineRange: scope).SourceIndexes);
        Assert.Throws<ArgumentOutOfRangeException>(() => LogContext.Create(source, 1, lineRange: scope));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogContext.Create(source, 6, lineRange: scope));
        Assert.Equal("body INCLUDED first\r", LogContext.Create(source, 2, 20, lineRange: new(2, 2)).Text);
    }

    [Fact]
    public void SliceHelpersExposeExactTextIntersectionsAndScopeOnlyThreadSummaries()
    {
        var source = LogParser.Parse(Original);
        var scope = new LogLineRange(2, 5);
        var range = LogSlice.GetTextRange(source, scope);
        Assert.Equal(source.GetLineOffset(2), range.Offset);
        Assert.Equal(source.GetLineOffset(6) - source.GetLineOffset(2), range.Length);
        Assert.Equal(LogProjection.CreateLineRange(source, scope).Text, source.Text.Substring(range.Offset, range.Length));
        Assert.Null(LogSlice.GetEntryRange(source, 0, scope));
        Assert.Null(LogSlice.GetEntryRange(source, 3, scope));
        Assert.Equal(new SourceTextRange(source.GetLineOffset(2), source.GetLineOffset(4) - source.GetLineOffset(2)), LogSlice.GetEntryRange(source, 1, scope));
        Assert.Equal(source.GetEntryRange(2), LogSlice.GetEntryRange(source, 2, scope));
        var summaries = LogSlice.GetThreadSummaries(source, scope);
        Assert.Equal(new[] { new ThreadSummary(3, 2, "025:00:01.123", "025:00:01.123", 1),
            new ThreadSummary(7, 2, "025:00:02", "025:00:02", 1) }, summaries);
        Assert.Same(source.Threads, LogSlice.GetThreadSummaries(source));
        Assert.Equal(new SourceTextRange(0, source.Text.Length), LogSlice.GetTextRange(source));
    }

    [Fact]
    public void ScopedSummaryCountsOwningEntriesOnceAndRetainsFileOrderAcrossMidnight()
    {
        var source = LogParser.Parse("synthetic preface\n[23:59:59] [T3] first\nbody one\nbody two\n[00:00:00] [T3] second\nbody last\n[99:99:99] [T7] malformed\nbody invalid");
        var summaries = LogSlice.GetThreadSummaries(source, new(2, 7));
        Assert.Equal(new[] { new ThreadSummary(3, 4, "23:59:59", "00:00:00", 2), new ThreadSummary(7, 2, null, null, 1) }, summaries);
        Assert.Equal(new ThreadSummary(null, 1, null, null, 1), Assert.Single(LogSlice.GetThreadSummaries(source, new(0, 0))));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 8)]
    [InlineData(8, 8)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void InvalidLineRangesFailAcrossEveryScopedApi(int first, int last)
    {
        var source = LogParser.Parse(Original);
        var scope = new LogLineRange(first, last);
        Assert.Throws<ArgumentOutOfRangeException>(() => LogProjection.CreateLineRange(source, scope));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogProjection.CreateFiltered(source, [3], EntryFilter.Empty, lineRange: scope));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogProjection.CreateEntries(source, [1], lineRange: scope));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogContext.Create(source, 2, lineRange: scope));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogSlice.GetTextRange(source, scope));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogSlice.GetThreadSummaries(source, scope));
    }

    [Fact]
    public void EmptySourceHasNoPhysicalRangeAndCancelledScopeWorkDoesNotPublishPartialResults()
    {
        var empty = LogParser.Parse("");
        Assert.Throws<ArgumentOutOfRangeException>(() => LogProjection.CreateLineRange(empty, new(0, 0)));
        Assert.Empty(LogProjection.CreateFiltered(empty, [], EntryFilter.Empty).Text);
        var source = LogParser.Parse(Original);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var scope = new LogLineRange(2, 5);
        Assert.Throws<OperationCanceledException>(() => LogProjection.CreateLineRange(source, scope, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => LogProjection.CreateFiltered(source, [3], EntryFilter.Empty, cancelled.Token, lineRange: scope));
        Assert.Throws<OperationCanceledException>(() => LogContext.Create(source, 2, cancellationToken: cancelled.Token, lineRange: scope));
        Assert.Throws<OperationCanceledException>(() => LogSlice.GetThreadSummaries(source, scope, cancelled.Token));
        Assert.Equal(Original, source.Text);
    }

    [Fact]
    public async Task ScopedExportWritesOnlySelectedPhysicalLinesAndProtectsTheFullOriginalFile()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "synthetic-scope-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "synthetic-original.log");
            byte[] original = new UTF8Encoding(true, true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Original)).ToArray();
            await File.WriteAllBytesAsync(path, original);
            var source = await LogFileReader.ReadAsync(path);
            var view = LogProjection.CreateLineRange(source, new(2, 5));
            await Assert.ThrowsAsync<IOException>(() => LogExporter.ExportAsync(view, path));
            await Assert.ThrowsAsync<IOException>(() => LogExporter.ExportAsync(view, Path.Combine(folder, ".", "synthetic-original.log")));
            string target = Path.Combine(folder, "synthetic-range.log");
            await LogExporter.ExportAsync(view, target);
            Assert.Equal(Encoding.UTF8.GetBytes(view.Text), await File.ReadAllBytesAsync(target));
            Assert.DoesNotContain("HEADER_ONLY", await File.ReadAllTextAsync(target));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            await Assert.ThrowsAsync<IOException>(() => LogExporter.ExportAsync(view, target));
        }
        finally { Directory.Delete(folder, true); }
    }
}
