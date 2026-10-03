using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

/// <summary>Whole-line selection through the real gutter event path, without shown windows or clipboard access.</summary>
public sealed class MultiLineSelectionUxTests
{
    private const string Text = "[000:00:01] [T1] synthetic alpha\r\nbody A한🙂\n[000:00:02] [T2] synthetic hidden-middle\r\nbody B middle\r[000:00:03] [T1] synthetic gamma\nlast unterminated";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task GutterDragSelectsInclusivePhysicalRowsInEitherDirection(bool reverse) => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            var editor = Control<TextEditor>(window, "Editor");
            Layout(editor, 850, 480);
            var margin = Field<OriginalLineMargin>(window, "margin");
            Assert.True(margin.BeginLineSelectionAtPoint(RowPoint(window, reverse ? 4 : 2), false));
            Assert.True(margin.ContinueLineSelectionAtPoint(RowPoint(window, reverse ? 2 : 4)));
            margin.EndLineSelection();
            Assert.Equal(new[] { 1, 2, 3 }, SelectedRows(window));
            Assert.Equal(Rows(Field<LogData>(window, "data"), 1, 2, 3), CopyObject(editor));
            Assert.Equal(reverse ? 2 : 4, editor.TextArea.Caret.Line);
            Assert.Equal(Text, Field<LogData>(window, "data").Text);
            Assert.Equal(Text, editor.Text);
            Assert.True(editor.IsReadOnly);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ControlGutterAndBodyToggleDisjointRowsAndCopyInOriginalOrderWithExactEndings() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            var editor = Control<TextEditor>(window, "Editor");
            Layout(editor, 850, 480);
            Pick(window, 6); Pick(window, 2, true);
            Assert.Equal(new[] { 1, 5 }, SelectedRows(window));
            Assert.Equal("body A한🙂\nlast unterminated", CopyObject(editor));
            Assert.True(editor.TextArea.Selection.Segments.Count() >= 2);
            Assert.False((bool)Invoke(window, "ToggleHighlightAtSelection")!);
            Assert.Empty(Control<ItemsControl>(window, "KeywordList").Items.Cast<object>());
            Assert.Equal(new[] { 1, 5 }, SelectedRows(window));
            var view = editor.TextArea.TextView;
            view.EnsureVisualLines();
            var row = view.VisualLines.Single(line => line.FirstDocumentLine.LineNumber == 6);
            Assert.True((bool)Invoke(window, "ToggleWholeLineAtPoint", new Point(25, row.VisualTop - view.VerticalOffset + row.Height / 2))!);
            Assert.Equal(new[] { 1 }, SelectedRows(window));
            Assert.Equal("body A한🙂\n", CopyObject(editor));
            Assert.False((bool)Invoke(window, "ToggleWholeDisplayLine", 0)!);
            Assert.Equal(new[] { 1 }, SelectedRows(window));
            Pick(window, 2, true);
            Assert.Empty(SelectedRows(window));
            Assert.Equal(Text, Field<LogData>(window, "data").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FilteredDisjointSegmentsKeepSourceGapsForCopyMetricsAndSelectionOnlySearch() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(item => item.Id == 1))!;
            var source = Field<LogData>(window, "data");
            var projection = Field<LogProjection>(window, "projection");
            var editor = Control<TextEditor>(window, "Editor");
            Assert.Equal(new[] { 0, 1, 4, 5 }, projection.SourceIndexes);
            Layout(editor, 850, 480);
            Pick(window, 3); Pick(window, 2, true);
            Assert.Equal(new[] { 1, 4 }, SelectedRows(window));
            string expected = Rows(source, 1, 4);
            Assert.Equal(expected, CopyObject(editor));
            Assert.DoesNotContain("middle", CopyObject(editor));
            var spans = editor.TextArea.Selection.Segments.Select(segment => new SelectionSpan(segment.StartOffset, segment.Length)).ToArray();
            var mapped = SelectionSourceRanges.Map(projection, spans);
            Assert.Equal(new[]
            {
                new SourceTextRange(source.GetLineOffset(1), source.Lines[1].RawText.Length + source.Lines[1].LineEnding.Length),
                new SourceTextRange(source.GetLineOffset(4), source.Lines[4].RawText.Length + source.Lines[4].LineEnding.Length)
            }, mapped);
            Assert.Equal(new SelectionSummary(2, expected.EnumerateRunes().Count()), SelectionMetrics.Compute(projection.Text, projection.DisplayOffsets, projection.Count, spans));

            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "middle";
            Invoke(window, "CaptureSearchSelection_Click", window, new RoutedEventArgs());
            await (Task)Invoke(window, "SearchAsync")!;
            Assert.Empty(Field<LocatedSearchHit[]>(window, "searchHits"));
            Control<TextBox>(window, "SearchBox").Text = "synthetic";
            await (Task)Invoke(window, "SearchAsync")!;
            Assert.Equal(4, Assert.Single(Field<LocatedSearchHit[]>(window, "searchHits")).SourceLineIndex);
            Assert.Equal(mapped, Field<IReadOnlyList<SourceTextRange>>(window, "fixedSearchRanges"));
            Assert.Equal(Text, source.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ScopedSelectionCopiesOnlyItsOriginalPhysicalRowsAndRejectsPhantomRows() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            var source = Field<LogData>(window, "data");
            Assert.True(await (Task<bool>)Invoke(window, "OpenLineSessionAsync", 1, 4)!);
            var editor = Control<TextEditor>(window, "Editor");
            Layout(editor, 850, 480);
            Pick(window, 4); Pick(window, 2, true);
            Assert.Equal(new[] { 2, 4 }, SelectedRows(window));
            Assert.Equal(Rows(source, 2, 4), CopyObject(editor));
            Assert.False((bool)Invoke(window, "ToggleWholeDisplayLine", 5)!);
            Assert.Equal(new[] { 2, 4 }, SelectedRows(window));
            Assert.Same(source, Field<LogData>(window, "data"));
            Assert.Equal(Text, source.Text);

            await Load(window, "[000:00:01] [T1] synthetic trailing\n");
            Layout(editor, 850, 480);
            Pick(window, 1);
            Assert.False((bool)Invoke(window, "ToggleWholeDisplayLine", 2)!);
            Assert.Equal(new[] { 0 }, SelectedRows(window));
            Assert.Equal("[000:00:01] [T1] synthetic trailing\n", CopyObject(editor));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task TabsRestoreDisjointRowsAndOrdinaryPhraseSelectionRemainsOrdinary() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            var source = Field<LogData>(window, "data");
            var editor = Control<TextEditor>(window, "Editor");
            Layout(editor, 850, 480);
            Pick(window, 2); Pick(window, 4, true);
            await Load(window, "[000:00:08] [T8] synthetic independent\nsecond tab body");
            Assert.Empty(SelectedRows(window));
            Layout(editor, 850, 480); Pick(window, 2);
            var tabs = Control<ListBox>(window, "SessionTabs");
            tabs.SelectedIndex = 0;
            Assert.Equal(new[] { 1, 3 }, SelectedRows(window));
            Assert.Equal(Rows(source, 1, 3), CopyObject(editor));

            int phrase = editor.Text.IndexOf("한🙂", StringComparison.Ordinal);
            editor.Select(phrase, "한🙂".Length);
            Assert.Empty(SelectedRows(window));
            Assert.Equal("한🙂", CopyObject(editor));
            var spans = editor.TextArea.Selection.Segments.Select(segment => new SelectionSpan(segment.StartOffset, segment.Length));
            Assert.Equal(new SelectionSummary(1, 2), SelectionMetrics.Compute(editor.Text, 6, spans));
            tabs.SelectedIndex = 1;
            Assert.Equal(new[] { 1 }, SelectedRows(window));
            Assert.Equal("second tab body", CopyObject(editor));
            tabs.SelectedIndex = 0;
            Assert.Empty(SelectedRows(window));
            Assert.Equal("한🙂", CopyObject(editor));
            Assert.Same(source, Field<LogData>(window, "data"));
            Assert.Equal(Text, source.Text);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ControlDragUnionAndRemovalRetractAgainstTheOriginalSetAndCannotEditSource() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            var editor = Control<TextEditor>(window, "Editor");
            Layout(editor, 850, 480);
            Pick(window, 1); Pick(window, 5, true);
            var margin = Field<OriginalLineMargin>(window, "margin");
            Assert.True(margin.BeginLineSelectionAtPoint(RowPoint(window, 2), true));
            Assert.True(margin.ContinueLineSelectionAtPoint(RowPoint(window, 4)));
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, SelectedRows(window));
            Assert.True(margin.ContinueLineSelectionAtPoint(RowPoint(window, 3)));
            Assert.Equal(new[] { 0, 1, 2, 4 }, SelectedRows(window));
            Assert.True(margin.ContinueLineSelectionAtPoint(RowPoint(window, 1)));
            Assert.Equal(new[] { 0, 1, 4 }, SelectedRows(window));
            margin.EndLineSelection();

            Assert.True(margin.BeginLineSelectionAtPoint(RowPoint(window, 5), true));
            Assert.True(margin.ContinueLineSelectionAtPoint(RowPoint(window, 2)));
            Assert.Equal(new[] { 0 }, SelectedRows(window));
            Assert.True(margin.ContinueLineSelectionAtPoint(RowPoint(window, 4)));
            Assert.Equal(new[] { 0, 1 }, SelectedRows(window));
            Assert.True(margin.ContinueLineSelectionAtPoint(RowPoint(window, 6)));
            Assert.Equal(new[] { 0, 1 }, SelectedRows(window));
            margin.EndLineSelection();
            string copied = CopyObject(editor);
            editor.TextArea.Selection.ReplaceSelectionWithText("synthetic replacement must not be written");
            Assert.Equal(copied, CopyObject(editor));
            Assert.Equal(Text, editor.Text);
            Assert.Equal(Text, Field<LogData>(window, "data").Text);
            Assert.True(editor.IsReadOnly);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DragAutoScrollAdvancesPhysicalRowsAcrossWrappingAndStopsAtBothSourceEnds() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            string body = string.Join(' ', Enumerable.Repeat("synthetic wrapping body", 18));
            string text = "[000:00:01] [T1] synthetic start\r\n" + body + "\n\nlast unterminated";
            await Load(window, text);
            var editor = Control<TextEditor>(window, "Editor");
            editor.WordWrap = true;
            Layout(editor, 300, 130);
            editor.TextArea.TextView.EnsureVisualLines();
            var margin = Field<OriginalLineMargin>(window, "margin");
            Assert.True(margin.BeginLineSelectionAtPoint(RowPoint(window, 1), false));
            Assert.True(margin.AutoScrollLineSelection(1));
            Assert.Equal(new[] { 0, 1 }, SelectedRows(window));
            Assert.Equal(2, editor.TextArea.Caret.Line);
            var wrapped = editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(2));
            Assert.True(wrapped.Height > editor.TextArea.TextView.DefaultLineHeight * 2);
            Assert.True(margin.AutoScrollLineSelection(1));
            Assert.Equal(3, editor.TextArea.Caret.Line);
            Assert.True(margin.AutoScrollLineSelection(1));
            Assert.Equal(new[] { 0, 1, 2, 3 }, SelectedRows(window));
            Assert.Equal(text, CopyObject(editor));
            Assert.False(margin.AutoScrollLineSelection(1));
            Assert.True(margin.AutoScrollLineSelection(-1));
            Assert.Equal(new[] { 0, 1, 2 }, SelectedRows(window));
            Assert.True(margin.AutoScrollLineSelection(-1));
            Assert.Equal(new[] { 0, 1 }, SelectedRows(window));
            Assert.Equal(2, editor.TextArea.Caret.Line);
            Assert.True(margin.AutoScrollLineSelection(-1));
            Assert.Equal(new[] { 0 }, SelectedRows(window));
            Assert.False(margin.AutoScrollLineSelection(-1));
            margin.EndLineSelection();
            Assert.False(margin.AutoScrollLineSelection(1));
            Assert.Equal(text, Field<LogData>(window, "data").Text);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task RightClickInsideDisjointSelectionRestoresTheSetForCopyAndDoesNotClearItOnMenuClose() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            var editor = Control<TextEditor>(window, "Editor");
            Layout(editor, 850, 480);
            Pick(window, 2); Pick(window, 4, true);
            string copied = CopyObject(editor);
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 4, 2)!);
            editor.Select(editor.Document.GetLineByNumber(4).Offset + 1, 0);
            var menu = new ContextMenu();
            Invoke(window, "LineContext_Opened", menu, new RoutedEventArgs());
            Assert.Equal(new[] { 1, 3 }, SelectedRows(window));
            Assert.Equal(copied, CopyObject(editor));
            Assert.Equal(3, Field<OriginalLineMargin>(window, "margin").ActionLineIndex);
            Invoke(window, "LineContext_Closed", menu, new RoutedEventArgs());
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Null(Field<OriginalLineMargin>(window, "margin").ActionLineIndex);
            Assert.Equal(new[] { 1, 3 }, SelectedRows(window));
            Assert.Equal(copied, CopyObject(editor));
            Assert.Equal(Text, Field<LogData>(window, "data").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task PublishingNewProjectionClearsOldCustomSelectionAndCancelsActiveGutterDragEvenWithSameDocument() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            var editor = Control<TextEditor>(window, "Editor");
            var source = Field<LogData>(window, "data");
            Layout(editor, 850, 480);
            var margin = Field<OriginalLineMargin>(window, "margin");
            Assert.True(margin.BeginLineSelectionAtPoint(RowPoint(window, 2), false));
            Assert.True(margin.ContinueLineSelectionAtPoint(RowPoint(window, 4)));
            await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(item => item.Id == 1))!;
            Layout(editor, 850, 480);
            Assert.Empty(SelectedRows(window));
            Assert.Empty(margin.SelectedLineIndexes);
            Assert.False(margin.ContinueLineSelectionAtPoint(RowPoint(window, 2)));
            Assert.False(margin.AutoScrollLineSelection(1));

            Assert.True(margin.BeginLineSelectionAtPoint(RowPoint(window, 1), false));
            var document = editor.Document;
            var equivalent = LogProjection.Create(source, new HashSet<int?> { 1 });
            Assert.Equal(document.Text, equivalent.Text);
            Invoke(window, "PublishView", equivalent, document, false);
            Assert.Same(document, editor.Document);
            Assert.Same(equivalent, Field<LogProjection>(window, "projection"));
            Assert.Empty(SelectedRows(window));
            Assert.IsNotType<WholeLineSelection>(editor.TextArea.Selection);
            Assert.Empty(margin.SelectedLineIndexes);
            Assert.False(margin.ContinueLineSelectionAtPoint(RowPoint(window, 2)));
            Assert.False(margin.AutoScrollLineSelection(-1));
            Assert.Equal(Text, source.Text);
            Assert.True(editor.IsReadOnly);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task WrappedDisjointRowSelectionRendersBothThemesWithoutShowingWindow(bool dark) => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false) { Width = 1160, Height = 780 };
        try
        {
            string longBody = string.Join(' ', Enumerable.Repeat("synthetic wrapped physical row · 합성 본문", 12));
            string text = string.Join('\n', Enumerable.Range(0, 18).Select(i => i == 1 ? longBody : $"[000:00:{i:00}] [T{i % 3}] synthetic record {i + 1:00}"));
            await Load(window, text);
            Control<ComboBox>(window, "ThemeBox").SelectedIndex = dark ? 0 : 1;
            Control<ToggleButton>(window, "WrapBox").IsChecked = true;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var content = (FrameworkElement)window.Content;
            Layout(content, 1160, 780);
            var editor = Control<TextEditor>(window, "Editor");
            editor.TextArea.TextView.EnsureVisualLines();
            var wrapped = editor.TextArea.TextView.VisualLines.Single(line => line.FirstDocumentLine.LineNumber == 2);
            Assert.True(wrapped.Height > editor.TextArea.TextView.DefaultLineHeight * 2);
            var margin = Field<OriginalLineMargin>(window, "margin");
            Assert.True(margin.BeginLineSelectionAtPoint(RowPoint(window, 2, true), false));
            Assert.True(margin.ContinueLineSelectionAtPoint(RowPoint(window, 2)));
            margin.EndLineSelection();
            Assert.Equal(new[] { 1 }, SelectedRows(window));
            Pick(window, 5, true); Pick(window, 9, true);
            Assert.Equal(new[] { 1, 4, 8 }, SelectedRows(window));
            Assert.Equal(Rows(Field<LogData>(window, "data"), 1, 4, 8), CopyObject(editor));
            Layout(content, 1160, 780);
            editor.TextArea.TextView.EnsureVisualLines();
            Assert.Equal(text, Field<LogData>(window, "data").Text);
            Assert.True(editor.IsReadOnly);
            Assert.False(window.IsVisible);

            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            Assert.True(File.Exists(Path.Combine(root, "ThreadLogViewer.slnx")));
            string outputFolder = Path.Combine(root, "TestResults", "v0.6.2");
            Directory.CreateDirectory(outputFolder);
            var bitmap = new RenderTargetBitmap(1160, 780, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(outputFolder, dark ? "multi-line-dark.png" : "multi-line-light.png"));
            encoder.Save(output);
        }
        finally { window.Close(); }
    });

    private static void Pick(MainWindow window, int display, bool control = false)
    {
        var margin = Field<OriginalLineMargin>(window, "margin");
        Assert.True(margin.BeginLineSelectionAtPoint(RowPoint(window, display), control));
        margin.EndLineSelection();
    }
    private static Point RowPoint(MainWindow window, int display, bool bottom = false)
    {
        var editor = Control<TextEditor>(window, "Editor");
        var view = editor.TextArea.TextView; view.EnsureVisualLines();
        var visual = view.VisualLines.Single(line => line.FirstDocumentLine.LineNumber == display);
        var margin = Field<OriginalLineMargin>(window, "margin");
        var point = view.TranslatePoint(new Point(0, visual.VisualTop - view.VerticalOffset + (bottom ? visual.Height - 1 : visual.Height / 2)), margin);
        point.X = margin.ActualWidth / 2;
        return point;
    }
    private static int[] SelectedRows(MainWindow window) => (int[]?)Invoke(window, "CaptureWholeLineSelection") ?? [];
    private static string CopyObject(TextEditor editor) => (string)editor.TextArea.Selection.CreateDataObject(editor.TextArea)!.GetData(DataFormats.UnicodeText)!;
    private static string Rows(LogData source, params int[] indexes) => string.Concat(indexes.Select(index => source.Lines[index].RawText.ToString() + source.Lines[index].LineEnding.ToString()));
    private static Task Load(MainWindow window, string text)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic multiple row selection", "synthetic failure")!;
    }
    private static void Layout(FrameworkElement element, double width, double height)
    { element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-multi-line-selection", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
