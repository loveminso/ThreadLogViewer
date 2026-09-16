using System.IO;
using System.Text;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class EntryTests
{
    private const string First = "[014:15:50.113] [T 1124525] 로그 내용 (testcase.cpp:132)\r\n  DE AD BE EF\r\n\r\n[T7] dump mentions another thread\n";
    private const string Other = "[014:15:50.113] [T7] hidden (read.cpp:40)\n  hidden dump\n";
    private const string Last = "[025:00:01] [T1124525] last\n  tail without final newline";

    [Fact]
    public void FiltersWholeEntriesAndPreservesPhysicalMappingThroughRepeatedToggles()
    {
        var log = LogParser.Parse(First + Other + Last, "nonexistent-source.log");
        Assert.Equal(new[] { new LogEntry(0, 4, 1124525), new LogEntry(4, 2, 7), new LogEntry(6, 2, 1124525) }, log.Entries);
        for (int i = 0; i < 3; i++)
        {
            var view = LogProjection.Create(log, [1124525]);
            Assert.Equal(First + Last, view.Text);
            Assert.Equal(new[] { 0, 1, 2, 3, 6, 7 }, view.SourceIndexes);
            Assert.Equal(2, view.EntryCount);
            Assert.Equal(7, view.AtDisplayLine(5)!.Value.OriginalLineNumber);
            Assert.Equal(132, view.AtDisplayLine(1)!.Value.SourceLineNumber);
            Assert.All(view.SourceIndexes, index => Assert.Equal(1124525, log.Lines[index].ThreadId));
            Assert.Empty(LogProjection.Create(log, []).Text);
            Assert.Equal(First + Other + Last, LogProjection.Create(log, [1124525, 7]).Text);
        }
        var summary = log.Threads.Single(t => t.ThreadId == 1124525);
        Assert.Equal(6, summary.Count);
        Assert.Equal(2, summary.EntryCount);
        Assert.Equal("014:15:50.113", summary.FirstRecordedTime);
        Assert.Equal("025:00:01", summary.LastRecordedTime);
        Assert.Equal(5, log.ContinuationCount);
        Assert.Equal(2, log.CompleteCount);
        Assert.Equal(1, log.PartialCount);
        Assert.Equal(0, log.UnrecognizedCount);
        Assert.Null(log.Lines[1].TimeOfDay);
        Assert.Null(log.Lines[1].SourceLineNumber);
        Assert.Empty(LogSearch.Find(LogProjection.Create(log, [1124525]).Text, "hidden").Hits);
    }

    [Fact]
    public void ThreadlessAndMalformedTimestampHeadersResetOwnershipButBodyTimeDoesNot()
    {
        const string text = "preamble\n\n[014:15:50.113] [T1124525] event\ntext [014:15:51] remains here\n[014:15:52] global\n  no thread dump\n[xx:yy:zz] malformed\n[T7] still malformed entry\n[014:99:00] [T3] invalid clock\n  T3 dump\n[014:15:53] [T7] recovered";
        var log = LogParser.Parse(text);
        var unclassified = LogProjection.Create(log, [null]);
        Assert.Equal(new[] { 0, 1, 4, 5, 6, 7 }, unclassified.SourceIndexes);
        Assert.Equal(new[] { 2, 3 }, LogProjection.Create(log, [1124525]).SourceIndexes);
        Assert.Equal(new[] { 8, 9 }, LogProjection.Create(log, [3]).SourceIndexes);
        Assert.Equal(new[] { 10 }, LogProjection.Create(log, [7]).SourceIndexes);
        Assert.Null(log.Lines[8].TimeOfDay);
        Assert.Equal("014:99:00", log.Lines[8].TimestampText.ToString());
        Assert.Null(log.Threads.Single(t => t.ThreadId == 3).FirstRecordedTime);
    }

    [Theory]
    [InlineData("014:15:50.113", 14, 15, 50, 1130000)]
    [InlineData("100:00:00", 100, 0, 0, 0)]
    [InlineData("00:00:00.0000001", 0, 0, 0, 1)]
    [InlineData("024:59:59.9", 24, 59, 59, 9000000)]
    public void PreservesElapsedHoursAndExactInputPrecision(string stamp, int hours, int minutes, int seconds, int ticks)
    {
        var line = LogParser.Parse($"[{stamp}] [T 1124525] event").Lines[0];
        Assert.Equal(new TimeSpan(hours, minutes, seconds) + TimeSpan.FromTicks(ticks), line.TimeOfDay);
        Assert.Equal(stamp, line.TimestampText.ToString());
        Assert.Equal(1124525, line.ThreadId);
    }

    [Theory]
    [InlineData("014:60:00")]
    [InlineData("014:00:60")]
    [InlineData("014:00:00.12345678")]
    [InlineData("999999999999999999999999999:00:00")]
    [InlineData("9999999999999:00:00")]
    public void InvalidTimeValuesRemainEntriesWithoutInventedTime(string stamp)
    {
        var log = LogParser.Parse($"[00:00:00] [T3] previous\n[{stamp}] [T7] broken\n  dump");
        Assert.Equal(2, log.Entries.Count);
        Assert.Null(log.Lines[1].TimeOfDay);
        Assert.Equal(stamp, log.Lines[1].TimestampText.ToString());
        Assert.Equal(new[] { 1, 2 }, LogProjection.Create(log, [7]).SourceIndexes);
    }

    [Fact]
    public async Task ExportIncludesWholeMultilineEntriesWithExactNewlinesAndProtectsOriginal()
    {
        string folder = Path.GetFullPath(Path.Combine("TestResults", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        try
        {
            string source = Path.Combine(folder, "source.log"), destination = Path.Combine(folder, "result.log");
            byte[] original = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(First + Other + Last)).ToArray();
            await File.WriteAllBytesAsync(source, original);
            var view = LogProjection.Create(await LogFileReader.ReadAsync(source), [1124525]);
            await Assert.ThrowsAsync<IOException>(() => LogExporter.ExportAsync(view, source));
            await LogExporter.ExportAsync(view, destination);
            Assert.Equal(Encoding.UTF8.GetBytes(First + Last), await File.ReadAllBytesAsync(destination));
            Assert.Equal(original, await File.ReadAllBytesAsync(source));
        }
        finally { Directory.Delete(folder, true); }
    }
}
