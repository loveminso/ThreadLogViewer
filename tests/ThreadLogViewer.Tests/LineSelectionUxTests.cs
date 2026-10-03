using System.IO;
using System.Reflection;
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

/// <summary>Synthetic, unshown WPF row selection and transient context-row indication.</summary>
public sealed class LineSelectionUxTests
{
    [Fact]
    public Task WholeRowSelectionPreservesMixedDelimitersAndCaretStaysOnClickedRow() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            const string text = "[000:00:01] [T1] synthetic alpha\r\nbody beta\n\r\n[000:00:02] [T2] synthetic gamma\rtail";
            await Load(window, text);
            var editor = Control<TextEditor>(window, "Editor");
            var source = Field<LogData>(window, "data");
            Assert.Equal(5, source.Lines.Count);
            for (int display = 1; display <= 5; display++)
            {
                Invoke(window, "SelectWholeDisplayLine", display);
                var row = source.Lines[display - 1];
                Assert.Equal(row.RawText.ToString() + row.LineEnding.ToString(), editor.SelectedText);
                var line = editor.Document.GetLineByNumber(display);
                Assert.Equal(line.Offset, editor.SelectionStart);
                Assert.Equal(line.TotalLength, editor.SelectionLength);
                Assert.Equal(line.Offset, editor.TextArea.Caret.Offset);
                Assert.Equal(display, editor.TextArea.Caret.Line);
            }
            Invoke(window, "BookmarkToggle_Click", window, new RoutedEventArgs());
            Assert.Equal(4, Assert.Single(Field<BookmarkState>(window, "bookmarks").Items).SourceLineIndex);
            Assert.True(editor.IsReadOnly);
            Assert.Equal(text, source.Text);
            Assert.Equal(text, editor.Text);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FilteredAndScopedSelectionsUsePhysicalOriginalRowsWithoutAdjacentContent() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            const string text = "[000:00:01] [T1] synthetic alpha\r\nbody one\n[000:00:02] [T2] synthetic middle\r\nbody two\n[000:00:03] [T1] synthetic last\r";
            await Load(window, text);
            var source = Field<LogData>(window, "data");
            var editor = Control<TextEditor>(window, "Editor");
            await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(item => item.Id == 1))!;
            Assert.Equal(new[] { 0, 1, 4 }, Field<LogProjection>(window, "projection").SourceIndexes);
            Invoke(window, "SelectWholeDisplayLine", 3);
            Assert.Equal(source.Lines[4].RawText.ToString() + "\r", editor.SelectedText);
            Assert.Equal(4, (int?)Invoke(window, "CurrentSourceLine"));
            Assert.True(await (Task<bool>)Invoke(window, "OpenLineSessionAsync", 1, 3)!);
            Assert.Equal(new[] { 1, 2, 3 }, Field<LogProjection>(window, "projection").SourceIndexes);
            Invoke(window, "SelectWholeDisplayLine", 3);
            Assert.Equal("body two\n", editor.SelectedText);
            Assert.Equal(3, (int?)Invoke(window, "CurrentSourceLine"));
            Assert.DoesNotContain("synthetic last", editor.SelectedText);
            Assert.Same(source, Field<LogData>(window, "data"));
            Assert.Equal(text, source.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EmptyPhysicalAndUnterminatedRowsAreSelectableButPhantomAndEmptyDocumentAreRejected() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic\r\n\r\ntail");
            var editor = Control<TextEditor>(window, "Editor");
            Invoke(window, "SelectWholeDisplayLine", 2);
            Assert.Equal("\r\n", editor.SelectedText);
            Assert.Equal(2, editor.TextArea.Caret.Line);
            Invoke(window, "SelectWholeDisplayLine", 3);
            Assert.Equal("tail", editor.SelectedText);
            Invoke(window, "SelectWholeDisplayLine", 0);
            Invoke(window, "SelectWholeDisplayLine", 4);
            Assert.Equal("tail", editor.SelectedText);

            await Load(window, "[000:00:01] [T1] synthetic trailing\n");
            Assert.Equal(2, editor.Document.LineCount);
            Assert.Single(Field<LogData>(window, "data").Lines);
            Invoke(window, "SelectWholeDisplayLine", 1);
            string selected = editor.SelectedText;
            Invoke(window, "SelectWholeDisplayLine", 2);
            Assert.Equal(selected, editor.SelectedText);
            Assert.Equal(1, editor.TextArea.Caret.Line);
            await Load(window, "");
            Invoke(window, "SelectWholeDisplayLine", 1);
            Assert.Equal("", editor.SelectedText);
            Assert.Empty(Field<LogData>(window, "data").Lines);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ActualGutterSelectionRouteSelectsOneWrappedPhysicalRowAndRejectsBlankSpace() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            string body = string.Join(' ', Enumerable.Repeat("synthetic wrapped body", 20));
            await Load(window, "[000:00:01] [T1] synthetic first\r\n" + body + "\r\n[000:00:02] [T2] synthetic last");
            var editor = Control<TextEditor>(window, "Editor");
            editor.WordWrap = true;
            Layout(editor, 320, 680);
            editor.TextArea.TextView.EnsureVisualLines();
            var view = editor.TextArea.TextView;
            var margin = Field<OriginalLineMargin>(window, "margin");
            var wrapped = view.VisualLines.Single(line => line.FirstDocumentLine.LineNumber == 2);
            Assert.True(wrapped.Height > view.DefaultLineHeight * 2);
            var wrappedPoint = view.TranslatePoint(new Point(0, wrapped.VisualTop - view.VerticalOffset + wrapped.Height - 1), margin);
            wrappedPoint.X = margin.ActualWidth / 2;
            Assert.True(margin.SelectLineAtPoint(wrappedPoint));
            Assert.Equal(body + "\r\n", editor.SelectedText);
            Assert.Equal(2, editor.TextArea.Caret.Line);
            Invoke(window, "BookmarkToggle_Click", window, new RoutedEventArgs());
            Assert.Equal(1, Assert.Single(Field<BookmarkState>(window, "bookmarks").Items).SourceLineIndex);
            string selected = editor.SelectedText;
            view.EnsureVisualLines();
            var last = view.VisualLines.Last();
            var below = view.TranslatePoint(new Point(0, last.VisualTop - view.VerticalOffset + last.Height + 8), margin);
            below.X = margin.ActualWidth / 2;
            Assert.False(margin.SelectLineAtPoint(below));
            Assert.False(margin.SelectLineAtPoint(new Point(-1, wrappedPoint.Y)));
            Assert.Equal(selected, editor.SelectedText);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ContextRowIndicatorKeepsSelectedPhraseAndClearsWhenMenuCloses() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic first phrase\n[000:00:02] [T2] synthetic second\n");
            var editor = Control<TextEditor>(window, "Editor");
            int phraseOffset = editor.Text.IndexOf("first phrase", StringComparison.Ordinal);
            editor.Select(phraseOffset, "first phrase".Length);
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 1, phraseOffset + 2)!);
            AssertIndicator(window, 0);
            var menu = new ContextMenu();
            Invoke(window, "LineContext_Opened", menu, new RoutedEventArgs());
            AssertIndicator(window, 0);
            Assert.Equal("first phrase", editor.SelectedText);
            string? seed = null;
            Func<IReadOnlyList<HighlightRuleDraft>, string, IReadOnlyList<HighlightRuleDraft>?> inspect = (_, selected) => { seed = selected; return null; };
            Set(window, "highlightPromptOverride", inspect);
            Invoke(window, "ShowHighlightPrompt");
            Assert.Equal("first phrase", seed);
            Assert.Equal("first phrase", editor.SelectedText);
            Invoke(window, "LineContext_Closed", menu, new RoutedEventArgs());
            await Idle(window);
            AssertIndicator(window, null);
            Assert.Equal("first phrase", editor.SelectedText);
            Assert.Equal(0, (int?)Invoke(window, "LineActionSourceLine"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ContextRowIndicatorCannotCarryIntoNewProjectionOrAnotherSession() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic first\n[000:00:02] [T2] synthetic second");
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 2, 1)!);
            Invoke(window, "LineContext_Opened", new ContextMenu(), new RoutedEventArgs());
            AssertIndicator(window, 1);
            await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(item => item.Id == 1))!;
            AssertIndicator(window, null);
            Assert.Null((int?)Invoke(window, "LineActionSourceLine"));
            Invoke(window, "LineContext_Closed", new ContextMenu(), new RoutedEventArgs());
            await Idle(window);
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 1, 1)!);
            AssertIndicator(window, 0);
            await Load(window, "[000:00:03] [T3] synthetic another session");
            AssertIndicator(window, null);
            Control<ListBox>(window, "SessionTabs").SelectedIndex = 0;
            AssertIndicator(window, null);
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 1, 1)!);
            Control<ListBox>(window, "SessionTabs").SelectedIndex = 1;
            AssertIndicator(window, null);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ContextRowAccentCoversWrappedRowInBothThemesAndRendersWithoutShowingWindow(bool dark) => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false) { Width = 1100, Height = 700 };
        try
        {
            string body = string.Join(' ', Enumerable.Repeat("synthetic physical row · 합성 본문", 14));
            string text = "[000:00:01] [T1] synthetic header\r\n" + body + "\r\n\r\n[000:00:02] [T2] synthetic next record\nsynthetic last body";
            await Load(window, text);
            Control<ComboBox>(window, "ThemeBox").SelectedIndex = dark ? 0 : 1;
            Control<ToggleButton>(window, "WrapBox").IsChecked = true;
            var editor = Control<TextEditor>(window, "Editor");
            int phrase = editor.Text.IndexOf("physical row", StringComparison.Ordinal);
            editor.Select(phrase, "physical row".Length);
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 2, phrase - editor.Document.GetLineByNumber(2).Offset + 2)!);
            Invoke(window, "LineContext_Opened", new ContextMenu(), new RoutedEventArgs());
            await Idle(window);
            var content = (FrameworkElement)window.Content;
            Layout(content, 1100, 700);
            var view = editor.TextArea.TextView;
            view.EnsureVisualLines();
            var wrapped = view.VisualLines.Single(line => line.FirstDocumentLine.LineNumber == 2);
            Assert.True(wrapped.Height > view.DefaultLineHeight * 2);
            var renderer = Field<ThreadBackgroundRenderer>(window, "threadRenderer");
            Assert.Equal(dark, renderer.Theme.IsDark);
            AssertIndicator(window, 1);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) renderer.Draw(view, context);
            var accent = Assert.Single(drawing.Drawing.Children.OfType<GeometryDrawing>(), item => ReferenceEquals(item.Brush, renderer.Theme.Accent));
            var rectangle = Assert.IsType<RectangleGeometry>(accent.Geometry).Rect;
            Assert.Equal(wrapped.VisualTop - view.VerticalOffset, rectangle.Top);
            Assert.Equal(wrapped.Height, rectangle.Height);
            Assert.Equal("physical row", editor.SelectedText);
            Assert.Equal(text, Field<LogData>(window, "data").Text);
            Assert.False(window.IsVisible);

            string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            Assert.True(File.Exists(Path.Combine(projectRoot, "ThreadLogViewer.slnx")));
            string outputDirectory = Path.Combine(projectRoot, "TestResults", "v0.6.1");
            Directory.CreateDirectory(outputDirectory);
            var bitmap = new RenderTargetBitmap(1100, 700, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(outputDirectory, dark ? "line-action-dark.png" : "line-action-light.png"));
            encoder.Save(output);
        }
        finally { window.Close(); }
    });

    private static void AssertIndicator(MainWindow window, int? line)
    {
        Assert.Equal(line, Field<OriginalLineMargin>(window, "margin").ActionLineIndex);
        Assert.Equal(line, Field<ThreadBackgroundRenderer>(window, "threadRenderer").ActionLineIndex);
    }
    private static Task Load(MainWindow window, string text)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic row selection", "synthetic failure")!;
    }
    private static async Task Idle(MainWindow window) => await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Layout(FrameworkElement element, double width, double height)
    { element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-row-selection", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
