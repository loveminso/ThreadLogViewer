using System.IO;
using System.Text;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class CoreTests
{
    [Theory]
    [InlineData("[10:03:12] [T3] Write submitted (testcase.cpp:103)", "10:03:12")]
    [InlineData("[10:03:12.1234] [T 3] Write submitted (testcase.cpp:103)", "10:03:12.1234")]
    [InlineData("[014:15:50.113] [T 3] Write submitted (testcase.cpp:103)", "014:15:50.113")]
    public void ExtractsFieldsWithoutConfusingLineNumbers(string text, string stamp)
    {
        var line = LogParser.Parse(text).Lines[0];
        Assert.Equal(1, line.OriginalLineNumber);
        Assert.Equal(103, line.SourceLineNumber);
        Assert.Equal("testcase.cpp", line.SourceFile.ToString());
        Assert.Equal("Write submitted", line.Message.ToString());
        Assert.Equal(3, line.ThreadId);
        Assert.Equal(stamp, line.TimestampText.ToString());
        Assert.Equal(text, line.RawText.ToString());
        Assert.NotNull(line.TimeOfDay);
        Assert.Equal(ParseQuality.Complete, line.Quality);
    }

    [Fact]
    public void TimestampEntryIncludesBlankMalformedAndThreadLookingContinuationLines()
    {
        var log = LogParser.Parse("[10:03:12] [T 3] 한글 메시지\r\n\r\nDE AD BE EF\nwrong format\r[T7] no clock");
        Assert.Equal(5, log.Lines.Count);
        Assert.Equal("한글 메시지", log.Lines[0].Message.ToString());
        Assert.Null(log.Lines[0].SourceLineNumber);
        Assert.All(log.Lines, line => Assert.Equal(3, line.ThreadId));
        Assert.All(log.Lines.Skip(1), line => Assert.Equal(ParseQuality.Continuation, line.Quality));
        Assert.Equal(5, Assert.Single(log.Entries).LineCount);
        Assert.Equal(5, Assert.Single(log.Threads).Count);
        Assert.Equal(1, log.Threads[0].EntryCount);
        Assert.Equal("\r\n", log.Lines[1].LineEnding.ToString());
    }

    [Fact]
    public void DoesNotReadThreadTagsFromMessageBodies()
    {
        var log = LogParser.Parse("dump mentions [T3] but has no prefix\n[10:03:12] message mentions [T7]");
        Assert.All(log.Lines, l => Assert.Null(l.ThreadId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("x\r\n\r\ny\n")]
    [InlineData("x\ry")]
    public void AllProjectionPreservesExactDecodedText(string text)
    {
        var log = LogParser.Parse(text);
        var view = LogProjection.Create(log, log.Threads.Select(t => t.ThreadId));
        Assert.Equal(text, view.Text);
        Assert.Null(view.AtDisplayLine(0));
        Assert.Null(view.AtDisplayLine(view.Count + 1));
    }

    [Fact]
    public void Maps100And105AndFiltersWithoutSourceFile()
    {
        string text = string.Join('\n', Enumerable.Range(1, 110).Select(i => $"[10:00:00] [T{(i is 100 or 105 ? 3 : 7)}] event {i} (testcase.cpp:900)"));
        var log = LogParser.Parse(text, "file-does-not-exist.log");
        var view = LogProjection.Create(log, [3]);
        Assert.Equal(2, view.Count);
        Assert.Equal(100, view.AtDisplayLine(1)!.Value.OriginalLineNumber);
        Assert.Equal(105, view.AtDisplayLine(2)!.Value.OriginalLineNumber);
        Assert.Equal(900, view.AtDisplayLine(2)!.Value.SourceLineNumber);
        Assert.DoesNotContain("event 101 ", view.Text);
    }

    [Fact]
    public void UnclassifiedCanBeSelectedIndependentlyAndAllCanBeHidden()
    {
        var log = LogParser.Parse("[T3] a\n\ndump\n[T7] b");
        var view = LogProjection.Create(log, [null]);
        Assert.Equal("\ndump\n", view.Text);
        Assert.Equal(new[] { 1, 2 }, view.SourceIndexes);
        Assert.Empty(LogProjection.Create(log, []).Text);
        Assert.Empty(LogProjection.Create(log, []).SourceIndexes);
    }

    [Fact]
    public void UsesFileOrderAcrossMidnightAndKeepsOriginalPrecision()
    {
        var log = LogParser.Parse("[23:59:59] [T3] start\n[00:00:00] [T3] end\n[00:00:00] [T7] same time");
        var thread = log.Threads.Single(t => t.ThreadId == 3);
        Assert.Equal("23:59:59", thread.FirstRecordedTime);
        Assert.Equal("00:00:00", thread.LastRecordedTime);
        Assert.Equal(2, log.Lines[1].OriginalLineNumber);
    }

    [Theory]
    [InlineData("[99:99:99] [T3] broken", 3)]
    [InlineData("[xx:yy:zz] [T7] broken", 7)]
    [InlineData("[10:00:00] [T9999999999999999] broken", null)]
    public void InvalidFieldsDoNotDiscardLine(string value, int? expectedThread)
    {
        var line = Assert.Single(LogParser.Parse(value).Lines);
        Assert.Equal(value, line.RawText.ToString());
        Assert.Equal(expectedThread, line.ThreadId);
        Assert.NotEqual(ParseQuality.Complete, line.Quality);
    }

    [Fact]
    public void ColorsAreStableAcrossParsingAndFilterChanges()
    {
        string before = ThreadColors.ForThread(123);
        var log = LogParser.Parse("[T999] first\n[T123] second");
        _ = LogProjection.Create(log, [123]);
        Assert.Equal(before, ThreadColors.ForThread(LogParser.Parse("[T123] again").Lines[0].ThreadId));
        Assert.NotEqual(before, ThreadColors.ForThread(null));
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    public void ReadsSupportedEncodingsWithoutLosingKorean(string name)
    {
        const string text = "[10:00:00] [T 3] 한글 테스트\r\n";
        Encoding enc = name switch { "utf8bom" => new UTF8Encoding(true, true), "utf16le" => new UnicodeEncoding(false, true, true), "utf16be" => new UnicodeEncoding(true, true, true), _ => new UTF8Encoding(false, true) };
        var decoded = LogFileReader.Decode(enc.GetPreamble().Concat(enc.GetBytes(text)).ToArray());
        Assert.Equal(text, decoded.Text);
        Assert.Contains(name == "utf8" ? "추정" : "BOM 확인", decoded.Description);
    }

    [Fact]
    public void Cp949RequiresExplicitChoiceAndInvalidUtf8IsNotSilentlyReplaced()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] bytes = Encoding.GetEncoding(949).GetBytes("한글 테스트");
        Assert.Throws<InvalidDataException>(() => LogFileReader.Decode(bytes));
        var decoded = LogFileReader.Decode(bytes, EncodingMode.Cp949);
        Assert.Equal("한글 테스트", decoded.Text);
        Assert.Contains("사용자 지정", decoded.Description);
    }

    [Fact]
    public void CancellationStopsParseProjectionAndSearch()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => LogParser.Parse("[T3] test", cancellationToken: cancel.Token));
        var log = LogParser.Parse("[T3] test");
        Assert.Throws<OperationCanceledException>(() => LogProjection.Create(log, [3], cancel.Token));
        Assert.Throws<OperationCanceledException>(() => LogSearch.Find(log.Text, "test", cancel.Token));
    }

    [Fact]
    public void NewOperationCancelsOldAndPreventsStalePublication()
    {
        using var operations = new LatestOperation();
        var old = operations.Begin();
        var latest = operations.Begin();
        Assert.True(old.Token.IsCancellationRequested);
        Assert.False(operations.IsCurrent(old.Version));
        Assert.True(operations.IsCurrent(latest.Version));
        operations.Cancel();
        Assert.True(latest.Token.IsCancellationRequested);
    }

    [Fact]
    public void SearchesOnlyVisibleTextIncludingChunkBoundary()
    {
        var log = LogParser.Parse("[T3] visible TARGET\n[T7] hidden TARGET");
        var view = LogProjection.Create(log, [3]);
        Assert.Single(LogSearch.Find(view.Text, "target").Hits);
        var boundary = LogSearch.Find(new string('x', 65534) + "abcdef", "abcdef");
        Assert.Equal(65534, Assert.Single(boundary.Hits).Offset);
        Assert.Empty(LogSearch.Find(view.Text, "missing").Hits);
    }

    [Fact]
    public async Task ExportProtectsSourceAndPreservesOrderAndEncoding()
    {
        using var temp = new TempFolder();
        string source = Path.Combine(temp.Path, "source.log");
        const string original = "[T3] 한글\r\n[T7] hidden\n\n[T3] last";
        await File.WriteAllTextAsync(source, original);
        var log = await LogFileReader.ReadAsync(source);
        var view = LogProjection.Create(log, [3, null]);
        await Assert.ThrowsAsync<IOException>(() => LogExporter.ExportAsync(view, source));
        await Assert.ThrowsAsync<IOException>(() => LogExporter.ExportAsync(view, Path.Combine(temp.Path, ".", "source.log")));
        await Assert.ThrowsAsync<IOException>(() => LogExporter.ExportAsync(view, source.ToUpperInvariant()));
        Assert.Equal(original, await File.ReadAllTextAsync(source));
        string destination = Path.Combine(temp.Path, "result.log");
        await LogExporter.ExportAsync(view, destination);
        byte[] bytes = await File.ReadAllBytesAsync(destination);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal("[T3] 한글\r\n\n[T3] last", new UTF8Encoding(false, true).GetString(bytes));
        await Assert.ThrowsAsync<IOException>(() => LogExporter.ExportAsync(view, destination));
    }

    [Fact]
    public async Task CancelledExportLeavesNeitherDestinationNorTemporaryFiles()
    {
        using var temp = new TempFolder();
        var view = LogProjection.Create(LogParser.Parse("[T3] data", Path.Combine(temp.Path, "source.log")), [3]);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        string destination = Path.Combine(temp.Path, "result.log");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LogExporter.ExportAsync(view, destination, cancellation.Token));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task EmptyAndMissingFilesAreHandledByReader()
    {
        using var temp = new TempFolder();
        string path = Path.Combine(temp.Path, "empty.log");
        await File.WriteAllBytesAsync(path, []);
        Assert.Empty((await LogFileReader.ReadAsync(path)).Lines);
        await Assert.ThrowsAsync<FileNotFoundException>(() => LogFileReader.ReadAsync(Path.Combine(temp.Path, "missing.log")));
    }

    private sealed class TempFolder : IDisposable
    {
        // Tests write only under the invoking project's ignored test-result directory.
        public string Path { get; } = System.IO.Path.GetFullPath(System.IO.Path.Combine("TestResults", Guid.NewGuid().ToString("N")));
        public TempFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
