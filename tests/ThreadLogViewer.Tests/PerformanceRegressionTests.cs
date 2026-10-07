using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class PerformanceRegressionTests
{
    [Fact]
    public void CompactLineFieldsRemainOriginalSlicesWithMixedLineEndingsAndContinuations()
    {
        const string text = "[025:02:03.1234567] [T12] 합성 message (sample.cpp:42)\r\n합성 body\r[bad:00:00] [T7] next\nlast";
        var source = LogParser.Parse(text);
        Assert.Equal(4, source.Lines.Count);
        Assert.Equal("025:02:03.1234567", source.Lines[0].TimestampText.ToString());
        Assert.Equal("합성 message", source.Lines[0].Message.ToString().TrimEnd());
        Assert.Equal("sample.cpp", source.Lines[0].SourceFile.ToString());
        Assert.Equal(42, source.Lines[0].SourceLineNumber);
        Assert.Equal("\r\n", source.Lines[0].LineEnding.ToString());
        Assert.Equal("\r", source.Lines[1].LineEnding.ToString());
        Assert.Equal(ParseQuality.Continuation, source.Lines[1].Quality);
        Assert.Equal(12, source.Lines[1].ThreadId);
        Assert.Equal(ParseQuality.Partial, source.Lines[2].Quality);
        Assert.Equal(text, string.Concat(source.Lines.Select(line => line.RawText.ToString() + line.LineEnding.ToString())));
        Assert.True(Unsafe.SizeOf<LogLine>() <= 112, "A physical line should not duplicate all parsed slice references.");
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("unicodeFFFE")]
    [InlineData("ks_c_5601-1987")]
    public void BlockDecodingPreservesCharactersAcrossBlockEdges(string encodingName)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding = Encoding.GetEncoding(encodingName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        string text = new string('a', 65535) + "합성 경계" + new string('b', 65535) + "끝";
        byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        var mode = encoding.CodePage == 949 ? EncodingMode.Cp949 : EncodingMode.Auto;
        Assert.Equal(text, LogFileReader.Decode(bytes, mode).Text);
    }

    [Fact]
    public void CancellationDuringRealDecodingStopsBeforePublication()
    {
        using var cancelled = new CancellationTokenSource();
        byte[] bytes = Encoding.UTF8.GetBytes(new string('x', 1024 * 1024));
        int reports = 0;
        var progress = new InlineProgress(p => { if (p.Phase == "텍스트 디코딩" && p.Percent > 0) { reports++; cancelled.Cancel(); } });
        Assert.Throws<OperationCanceledException>(() => LogFileReader.Decode(bytes, cancellationToken: cancelled.Token, progress: progress));
        Assert.True(reports > 0);
    }

    [Fact]
    public void CancellationDuringDocumentConstructionStopsAtARealBlock()
    {
        using var cancelled = new CancellationTokenSource();
        string text = string.Concat(Enumerable.Repeat("synthetic row\r\n", 100000));
        int reports = 0;
        var progress = new InlineProgress(p => { if (p.Phase == "화면 문서 준비" && p.Percent is > 0 and < 100) { reports++; cancelled.Cancel(); } });
        Assert.Throws<OperationCanceledException>(() => CancellableDocumentFactory.Create(text, cancelled.Token, progress));
        Assert.Equal(1, reports);
    }

    [Fact]
    public void CancellableDocumentPreservesUtf16AndCrLfAtInsertBoundaries()
    {
        string text = new string('x', 262143) + "\r\n" + new string('y', 262141) + "😀\r\n마지막";
        var document = CancellableDocumentFactory.Create(text, default, new InlineProgress(_ => { }));
        document.SetOwnerThread(Thread.CurrentThread);
        Assert.Equal(text, document.Text);
        Assert.Equal(3, document.LineCount);
        Assert.Equal(2, document.GetLineByNumber(1).DelimiterLength);
        Assert.Equal(2, document.GetLineByNumber(2).DelimiterLength);
    }

    [Fact]
    public Task LongLineUsesBoundedVisualRangesAndKeepsSearchSelectionAndSourceCoordinates() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            string text = "[000:00:00] [T1] " + new string('a', 65536) + "unique-target" + new string('b', 65536) + "\r\n[000:00:01] [T2] tail";
            await (Task)Invoke(window, "LoadAsync", (Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>>)
                ((token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress))), "synthetic long line", "synthetic failure")!;
            var editor = Control<TextEditor>(window, "Editor");
            await Layout(window);
            var visual = editor.TextArea.TextView.VisualLines[0];
            Assert.True(visual.VisualLength < LargeLineElementGenerator.DisplayBudget + 10);
            Assert.True(visual.Elements.Count(element => element.DocumentLength > element.VisualLength) > 0);
            var nextMarker = visual.Elements.Last(element => element.DocumentLength > element.VisualLength);
            nextMarker.GetType().GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(nextMarker,
                [new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent }]);
            await Layout(window);
            Assert.True(editor.CaretOffset >= LargeLineElementGenerator.DisplayBudget);
            var source = Field<LogData>(window, "data"); var view = Field<LogProjection>(window, "projection");
            var hit = Assert.Single(LogSearch.Find(source, view.EntryIndexes, "unique-target", new()).Hits);
            await (Task)Invoke(window, "NavigateHitAsync", hit)!;
            await Layout(window);
            Assert.Equal("unique-target", editor.SelectedText);
            var focus = LargeLineElementGenerator.GetWindow(editor.Document, editor.Document.GetLineByNumber(1), editor.CaretOffset);
            Assert.InRange(hit.SourceOffset, focus.StartOffset, focus.EndOffset);
            Assert.Equal(hit.SourceOffset, view.GetSourceOffset(editor.SelectionStart));
            editor.SelectAll();
            Assert.Equal(text, editor.SelectedText);
            Assert.Equal(text, editor.Document.Text);
            Assert.Equal(text, source.Text);
            Assert.Equal(2, editor.Document.LineCount);
            Assert.Equal(2, view.Count);
            var generator = Field<LargeLineElementGenerator>(window, "largeLineGenerator");
            var menu = Control<MenuItem>(window, "LargeLineMenu");
            menu.IsChecked = false; menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(generator.Enabled);
            Assert.Contains("전체 표시", Control<TextBlock>(window, "OperationStatus").Text);
            Assert.Equal(text, editor.Document.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void LongLineWindowsNeverSeparateASurrogatePair()
    {
        string text = new string('x', LargeLineElementGenerator.DisplayBudget - 1) + "😀" + new string('y', 20000);
        var document = CancellableDocumentFactory.Create(text, default, new InlineProgress(_ => { }));
        document.SetOwnerThread(Thread.CurrentThread);
        var line = document.GetLineByNumber(1);
        var first = LargeLineElementGenerator.GetWindow(document, line, 0);
        var next = LargeLineElementGenerator.GetWindow(document, line, first.EndOffset);
        Assert.False(char.IsHighSurrogate(document.GetCharAt(first.EndOffset - 1)));
        Assert.False(char.IsLowSurrogate(document.GetCharAt(next.StartOffset)));
        Assert.Contains("😀", document.GetText(first.StartOffset, first.EndOffset - first.StartOffset));
        Assert.Contains("😀", document.GetText(next.StartOffset, next.EndOffset - next.StartOffset));
    }

    [Fact]
    public Task PreviousMarkerAtSurrogateBoundaryOpensTheImmediatelyPreviousChunk() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            string text = new string('x', 8191) + "😀" + new string('y', 30000);
            await (Task)Invoke(window, "LoadAsync", (Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>>)
                ((token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress))), "synthetic surrogate boundary", "synthetic failure")!;
            var editor = Control<TextEditor>(window, "Editor");
            var generator = Field<LargeLineElementGenerator>(window, "largeLineGenerator");
            editor.CaretOffset = 8192;
            // Keep the exact test offset even if AvalonEdit normalizes its displayed caret.
            generator.FocusOffset = 8192; editor.TextArea.TextView.Redraw();
            await Layout(window);
            Assert.Equal(8191, LargeLineElementGenerator.GetWindow(editor.Document, editor.Document.GetLineByNumber(1), generator.FocusOffset).StartOffset);
            var previousMarker = editor.TextArea.TextView.VisualLines[0].Elements.First(element => element.DocumentLength > element.VisualLength);
            previousMarker.GetType().GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(previousMarker,
                [new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent }]);
            await Layout(window);
            Assert.Equal(4096, editor.CaretOffset);
            Assert.Equal(4096, generator.FocusOffset);
            Assert.Equal(text, editor.Document.Text);
            Assert.Equal(1, editor.Document.LineCount);
        }
        finally { window.Close(); }
    });

    private sealed class InlineProgress(Action<WorkProgress> action) : IProgress<WorkProgress>
    { public void Report(WorkProgress value) => action(value); }
    private static async Task Layout(MainWindow window)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1040, 600)); content.Arrange(new Rect(0, 0, 1040, 600)); content.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Control<TextEditor>(window, "Editor").TextArea.TextView.EnsureVisualLines();
    }
    private static T Control<T>(MainWindow window, string name) => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-performance-regression", Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
