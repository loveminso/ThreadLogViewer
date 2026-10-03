using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

/// <summary>Line actions and scoped tabs on unshown synthetic WPF windows; no clipboard or modal UI.</summary>
public sealed class LineWorkflowUxTests
{
    private const string Text = "[000:00:01.000] [T1] synthetic outside-before\r\nbody A start\n[000:00:01.012] [T2] synthetic hidden-middle\r\nbody B middle\n[000:00:02.000] [T1] synthetic range-end\r\n[000:00:03.000] [T3] synthetic outside-after";

    [Fact]
    public Task RightClickLineUsesPhysicalTargetInsteadOfPreviousCaretAndRejectsStaleView() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            var editor = Control<TextEditor>(window, "Editor");
            editor.TextArea.Caret.Line = 6;
            Capture(window, 2);
            Assert.Equal(1, Invoke(window, "LineActionSourceLine"));
            Assert.Equal(2, editor.TextArea.Caret.Line);
            Invoke(window, "SetTimePoint", true);
            var anchor = Field<TimeAnchor>(window, "timeA");
            Assert.Equal(1, anchor.SourceLineIndex);
            Assert.Equal(0, anchor.HeaderLineIndex);

            await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(t => t.Id == 1))!;
            Assert.False((bool)Invoke(window, "IsLineActionTargetCurrent")!);
            typeof(MainWindow).GetField("lineActionMenuOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            Assert.Null(Invoke(window, "LineActionSourceLine"));
            Assert.Same(anchor, Field<TimeAnchor>(window, "timeA"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SplittingIncludesHiddenPhysicalLinesAndKeepsOriginalNumbersAndSource() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            var source = Field<LogData>(window, "data");
            await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(t => t.Id == 1))!;
            Assert.Equal(new[] { 0, 1, 4 }, Field<LogProjection>(window, "projection").SourceIndexes);
            Assert.True(await Split(window, 1, 4));
            Assert.Equal(2, Control<ListBox>(window, "SessionTabs").Items.Count);
            var range = Field<LogProjection>(window, "projection");
            Assert.Same(source, range.Source);
            Assert.Equal(new[] { 1, 2, 3, 4 }, range.SourceIndexes);
            Assert.Equal(2, range.AtDisplayLine(1)?.OriginalLineNumber);
            Assert.Equal(5, range.AtDisplayLine(4)?.OriginalLineNumber);
            Assert.Contains("hidden-middle", range.Text);
            Assert.Contains("body B middle", range.Text);
            Assert.DoesNotContain("outside-before", range.Text);
            Assert.DoesNotContain("outside-after", range.Text);
            Assert.Equal(Text, source.Text);
            Assert.True(Control<TextEditor>(window, "Editor").IsReadOnly);

            Control<ListBox>(window, "SessionTabs").SelectedIndex = 0;
            Assert.Equal(new[] { 0, 1, 4 }, Field<LogProjection>(window, "projection").SourceIndexes);
            Assert.Same(source, Field<LogData>(window, "data"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ScopedTabCannotEscapeThroughFullSearchContextFilterOrOriginalNavigation() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            Assert.True(await Split(window, 1, 4));
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<ComboBox>(window, "SearchScopeBox").SelectedIndex = 1;
            Control<TextBox>(window, "SearchBox").Text = "outside";
            await (Task)Invoke(window, "SearchAsync")!;
            Assert.Empty(Field<LocatedSearchHit[]>(window, "searchHits"));
            Assert.False(await (Task<bool>)Invoke(window, "ShowContextAsync", 0)!);
            Assert.Equal(new[] { 1, 2, 3, 4 }, Field<LogProjection>(window, "projection").SourceIndexes);
            await (Task)Invoke(window, "NavigateOriginalAsync", 5, 1, 0)!;
            Assert.Equal(new[] { 1, 2, 3, 4 }, Field<LogProjection>(window, "projection").SourceIndexes);
            Control<TextBox>(window, "GoToBox").Text = "1";
            Invoke(window, "GoToSubmit_Click", window, new RoutedEventArgs());
            Assert.Equal(Visibility.Collapsed, Control<Button>(window, "HiddenContextButton").Visibility);

            Assert.True(await (Task<bool>)Invoke(window, "ShowContextAsync", 2)!);
            Assert.All(Field<LogProjection>(window, "projection").SourceIndexes, line => Assert.InRange(line, 1, 4));
            await (Task)Invoke(window, "FilterAsync", null, Field<object?>(window, "normalAnchor"))!;
            Assert.Equal(new[] { 1, 2, 3, 4 }, Field<LogProjection>(window, "projection").SourceIndexes);
            await (Task)Invoke(window, "FilterAsync", new EntryFilter(["outside-before"], []), null)!;
            Assert.Empty(Field<LogProjection>(window, "projection").SourceIndexes);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ReverseEndpointsNormalizeAndNestedSplitsRejectLinesOutsideCurrentScope() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            Assert.True(await Split(window, 4, 1));
            Assert.Equal(new[] { 1, 2, 3, 4 }, Field<LogProjection>(window, "projection").SourceIndexes);
            int before = Control<ListBox>(window, "SessionTabs").Items.Count;
            Assert.False(await Split(window, 0, 3));
            Assert.Equal(before, Control<ListBox>(window, "SessionTabs").Items.Count);
            Assert.True(await Split(window, 3, 3));
            var single = Field<LogProjection>(window, "projection");
            Assert.Equal(new[] { 3 }, single.SourceIndexes);
            Assert.Equal(4, single.AtDisplayLine(1)?.OriginalLineNumber);
            Assert.Contains("body B middle", single.Text);
            Assert.DoesNotContain("hidden-middle", single.Text);
            Assert.True(await (Task<bool>)Invoke(window, "ShowContextAsync", 3)!);
            Assert.Equal(new[] { 3 }, Field<LogProjection>(window, "projection").SourceIndexes);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SplitStartAndTimeRemainIndependentAcrossTabsAndCompactResultKeepsPreviousValidTime() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01.000] [T1] synthetic first\nbody\n[000:00:01.012] [T2] synthetic second\n[000:99:00] [T3] synthetic invalid");
            Capture(window, 2);
            Invoke(window, "SplitStart_Click", window, new RoutedEventArgs());
            Invoke(window, "SetTimePoint", true);
            Assert.Equal(1, Field<int?>(window, "separationStartLine"));
            Assert.Equal(Visibility.Visible, Control<Border>(window, "SplitStatusPanel").Visibility);
            Assert.Contains("원본 2줄", Control<TextBlock>(window, "SplitSummary").Text);
            Capture(window, 3);
            Invoke(window, "SetTimePoint", false);
            var firstTime = Field<TimeAnchor>(window, "timeA");
            var secondTime = Field<TimeAnchor>(window, "timeB");
            Assert.Equal(Visibility.Visible, Control<Border>(window, "TimeResultPanel").Visibility);
            Assert.Contains("0.012", Control<TextBlock>(window, "TimeSummary").Text);
            Assert.Equal(1, Field<OriginalLineMargin>(window, "margin").TimeALineIndex);
            Assert.Equal(2, Field<OriginalLineMargin>(window, "margin").TimeBLineIndex);
            Capture(window, 4);
            Invoke(window, "SetTimePoint", false);
            Assert.Same(secondTime, Field<TimeAnchor>(window, "timeB"));

            await Load(window, "[000:00:05] [T8] synthetic independent");
            Assert.Null(Field<int?>(window, "separationStartLine"));
            Assert.Null(Field<object?>(window, "timeA"));
            Assert.Null(Field<object?>(window, "timeB"));
            Assert.Equal(Visibility.Collapsed, Control<Border>(window, "TimeResultPanel").Visibility);
            Assert.Equal(Visibility.Collapsed, Control<Border>(window, "SplitStatusPanel").Visibility);
            Control<ListBox>(window, "SessionTabs").SelectedIndex = 0;
            Assert.Equal(1, Field<int?>(window, "separationStartLine"));
            Assert.Equal(Visibility.Visible, Control<Border>(window, "SplitStatusPanel").Visibility);
            Assert.Same(firstTime, Field<TimeAnchor>(window, "timeA"));
            Assert.Same(secondTime, Field<TimeAnchor>(window, "timeB"));
            Assert.Equal(Visibility.Visible, Control<Border>(window, "TimeResultPanel").Visibility);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert.Equal(Visibility.Collapsed, Control<ScrollViewer>(window, "ToolsScroll").Visibility);
            Assert.Equal(Visibility.Collapsed, Control<Expander>(window, "AnalysisPanel").Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task F3AndF4RouteToNextAndPreviousResultsAndMenusExposeHighlightShortcut() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "synthetic";
            await (Task)Invoke(window, "SearchAsync")!;
            await (Task)Invoke(window, "NavigateSearchAsync", false)!;
            Assert.Equal(0, Field<int>(window, "searchIndex"));
            var next = KeyArgs(Key.F3);
            Invoke(window, "Window_KeyDown", window, next);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(next.Handled);
            Assert.Equal(1, Field<int>(window, "searchIndex"));
            var previous = KeyArgs(Key.F4);
            Invoke(window, "Window_KeyDown", window, previous);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(previous.Handled);
            Assert.Equal(0, Field<int>(window, "searchIndex"));
            Assert.Contains("F4", Control<MenuItem>(window, "FindPreviousMenu").InputGestureText);
            var highlight = Control<MenuItem>(window, "AnalysisMenu").Items.OfType<MenuItem>().Single(item => Equals(item.Tag, "highlight"));
            Assert.Equal("Shift+F8", highlight.InputGestureText);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ScopedWorkbenchKeepsLogSpaceAndRendersSyntheticContentWithoutShowingWindow() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false) { Width = 1360, Height = 860 };
        try
        {
            string synthetic = string.Join('\n', Enumerable.Range(0, 70).Select(i =>
                $"[000:{i / 60:00}:{i % 60:00}.000] [T{i % 3}] synthetic {(i % 2 == 0 ? "Write" : "Read")} completed · 합성 로그 {i:000} (synthetic.cpp:{100 + i})"));
            await Load(window, synthetic);
            Assert.True(await Split(window, 5, 54));
            Capture(window, 4); Invoke(window, "SetTimePoint", true);
            Capture(window, 16); Invoke(window, "SetTimePoint", false);
            Capture(window, 20); Invoke(window, "SplitStart_Click", window, new RoutedEventArgs());
            Assert.True((bool)Invoke(window, "ApplyHighlightRules", (object)new HighlightRuleDraft[] { new("Write", 0), new("Read", 2) })!);
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "Write";
            await (Task)Invoke(window, "SearchAsync")!;
            await (Task)Invoke(window, "RefreshKeywordsAsync")!;
            Invoke(window, "UpdateLayoutLimits");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1360, 860));
            content.Arrange(new Rect(0, 0, 1360, 860));
            content.UpdateLayout();
            var editor = Control<TextEditor>(window, "Editor");
            editor.TextArea.TextView.EnsureVisualLines();
            Assert.True(editor.ActualHeight > 250, $"Scoped workbench left only {editor.ActualHeight:0.##} pixels for the log.");
            Assert.Equal(Visibility.Collapsed, Control<Expander>(window, "AnalysisPanel").Visibility);
            Assert.Equal(Visibility.Visible, Control<Border>(window, "SplitStatusPanel").Visibility);
            Assert.Equal(Visibility.Visible, Control<Border>(window, "TimeResultPanel").Visibility);
            Assert.All(Field<LogProjection>(window, "projection").SourceIndexes, line => Assert.InRange(line, 5, 54));
            Assert.False(window.IsVisible);

            string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            Assert.True(File.Exists(System.IO.Path.Combine(projectRoot, "ThreadLogViewer.slnx")));
            string outputDirectory = System.IO.Path.Combine(projectRoot, "TestResults", "v0.6.0");
            Directory.CreateDirectory(outputDirectory);
            var bitmap = new RenderTargetBitmap(1360, 860, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(System.IO.Path.Combine(outputDirectory, "offscreen-v0.6.0.png"));
            encoder.Save(output);
        }
        finally { window.Close(); }
    });

    private static KeyEventArgs KeyArgs(Key key) => new(Keyboard.PrimaryDevice, new SyntheticPresentationSource(), 0, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };

    private static void Capture(MainWindow window, int displayLine)
    {
        Invoke(window, "CaptureLineActionTarget", displayLine, 1);
        typeof(MainWindow).GetField("lineActionMenuOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
    }
    private static Task<bool> Split(MainWindow window, int first, int last) => (Task<bool>)Invoke(window, "OpenLineSessionAsync", first, last)!;
    private static Task Load(MainWindow window, string text)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic line workflow", "synthetic failure")!;
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class SyntheticPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = new Canvas();
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-line-workflow", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
