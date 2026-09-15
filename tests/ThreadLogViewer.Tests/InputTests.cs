using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class InputTests
{
    [Fact]
    public void PasteHasNoInventedFileOrEncodingAndPreservesRelativeLineNumbers()
    {
        const string text = "[10:00:00] [T3] 한글 (source.cpp:800)\r\n\r\ndump\n[T7] other";
        var log = LogParser.ParsePastedText(text);
        Assert.Null(log.SourcePath);
        Assert.Contains("원본 인코딩 알 수 없음", log.EncodingDescription);
        Assert.Equal(text, log.Text);
        var view = LogProjection.Create(log, [3, null]);
        Assert.Equal(3, view.Count);
        Assert.Equal(1, view.AtDisplayLine(1)!.Value.OriginalLineNumber);
        Assert.Equal(800, view.AtDisplayLine(1)!.Value.SourceLineNumber);
        Assert.Equal(3, view.AtDisplayLine(3)!.Value.OriginalLineNumber);
        Assert.Equal("[10:00:00] [T3] 한글 (source.cpp:800)\r\n\r\ndump\n", view.Text);
    }

    [Fact]
    public async Task PastedLogExportsWithoutSourcePathAndStillProtectsExistingFiles()
    {
        string directory = Path.GetFullPath(Path.Combine("TestResults", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            string output = Path.Combine(directory, "pasted.log");
            var view = LogProjection.Create(LogParser.ParsePastedText("[T3] 붙여넣기\r\n"), [3]);
            await LogExporter.ExportAsync(view, output);
            Assert.Equal("[T3] 붙여넣기\r\n", await File.ReadAllTextAsync(output));
            await Assert.ThrowsAsync<IOException>(() => LogExporter.ExportAsync(view, output));
            Assert.Equal("[T3] 붙여넣기\r\n", await File.ReadAllTextAsync(output));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void PastedParsingRemainsCancellable()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => LogParser.ParsePastedText("[T3] test", cancel.Token));
    }

    [Fact]
    public void WpfFileDropAndFileCopyUseTheSameSingleFile()
    {
        InSta(() =>
        {
            const string path = @"C:\synthetic\한글 폴더\sample.LOG";
            var data = new DataObject(DataFormats.FileDrop, new[] { path });
            data.SetData(DataFormats.UnicodeText, "This is a file name, not log text");
            Assert.Equal(path, LogTransfer.ReadDroppedFile(data));
            var pasted = LogTransfer.ReadClipboard(data);
            Assert.Equal(path, pasted.FilePath);
            Assert.Null(pasted.Text);
        });
    }

    [Fact]
    public void ClipboardUnicodePreservesKoreanBlankLinesAndLineEndings()
    {
        InSta(() =>
        {
            const string text = "[T3] 한글\r\n\r\n[T7] text\n";
            var result = LogTransfer.ReadClipboard(new DataObject(DataFormats.UnicodeText, text));
            Assert.Null(result.FilePath);
            Assert.Equal(text, result.Text);
            Assert.Equal("\n", LogTransfer.ReadClipboard(new DataObject(DataFormats.UnicodeText, "\n")).Text);
        });
    }

    [Fact]
    public void EmptyUnsupportedOrMultipleFilesDoNotReplaceTheLog()
    {
        InSta(() =>
        {
            Assert.Throws<InvalidDataException>(() => LogTransfer.ReadClipboard(null));
            Assert.Throws<InvalidDataException>(() => LogTransfer.ReadClipboard(new DataObject()));
            Assert.Throws<InvalidDataException>(() => LogTransfer.ReadClipboard(new DataObject(DataFormats.UnicodeText, "")));
            foreach (var files in new[] { Array.Empty<string>(), new[] { "a.log", "b.txt" }, new[] { "program.exe" } })
            {
                var data = new DataObject(DataFormats.FileDrop, files);
                data.SetData(DataFormats.UnicodeText, "fallback text must not hide a rejected file selection");
                Assert.False(LogTransfer.IsSingleLogFile(files));
                Assert.Throws<InvalidDataException>(() => LogTransfer.ReadDroppedFile(data));
                Assert.Throws<InvalidDataException>(() => LogTransfer.ReadClipboard(data));
            }
            Assert.True(LogTransfer.IsSingleLogFile(["sample.TXT"]));
        });
    }

    [Fact]
    public void SearchBoxKeepsNormalPasteButExplicitLogShortcutWorksAnywhere()
    {
        InSta(() =>
        {
            var search = new TextBox();
            var logSurface = new ICSharpCode.AvalonEdit.Editing.TextArea();
            Assert.False(LogTransfer.OpensLogOnPaste(Key.V, ModifierKeys.Control, search));
            Assert.False(LogTransfer.OpensLogOnPaste(Key.Insert, ModifierKeys.Shift, search));
            Assert.True(LogTransfer.OpensLogOnPaste(Key.V, ModifierKeys.Control, logSurface));
            Assert.True(LogTransfer.OpensLogOnPaste(Key.Insert, ModifierKeys.Shift, logSurface));
            Assert.True(LogTransfer.OpensLogOnPaste(Key.V, ModifierKeys.Control | ModifierKeys.Shift, search));
            Assert.False(LogTransfer.OpensLogOnPaste(Key.V, ModifierKeys.None, logSurface));
            Assert.False(LogTransfer.OpensLogOnPaste(Key.V, ModifierKeys.Control | ModifierKeys.Alt, logSurface));
        });
    }

    // Exercises real WPF data objects and controls without reading/writing the user's clipboard.
    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA input test timed out");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
