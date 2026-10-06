using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class EmptyRecoveryLayoutTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public Task EmptyRecoveryFitsSmallBodyAlongsideHiddenSourceSearchResults(bool dark, bool noThreads) => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        string text = string.Join("\n", Enumerable.Range(0, 100).Select(index =>
            $"[000:00:01] [T{index % 2 + 1}] synthetic target {index}"));
        try
        {
            Control<ComboBox>(window, "ThemeBox").SelectedIndex = dark ? 0 : 1;
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) =>
                Task.FromResult(LogParser.ParsePastedText(text, token, progress));
            await (Task)Invoke(window, "LoadAsync", load, "synthetic empty recovery", "synthetic failure")!;
            if (noThreads)
                await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(_ => false))!;
            else
            {
                await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(thread => thread.Id == 1))!;
                Control<TextBox>(window, "IncludeBox").Text = "synthetic impossible condition";
                await (Task)Invoke(window, "FilterAsync", new EntryFilter(["synthetic impossible condition"], []), null)!;
            }
            Assert.Equal(0, Field<LogProjection>(window, "projection").Count);
            var source = Field<LogData>(window, "data");
            var selected = Field<List<ThreadItem>>(window, "threadItems").Select(thread => thread.IsSelected).ToArray();
            await SearchWholeSource(window);
            await Layout(window);

            var grid = Control<Grid>(window, "LogContentGrid");
            var editor = Control<TextEditor>(window, "Editor");
            var viewport = Control<ScrollViewer>(window, "EmptyViewport");
            var empty = Control<StackPanel>(window, "EmptyPanel");
            var results = Control<Border>(window, "ResultsPanel");
            var splitter = Control<GridSplitter>(window, "ResultsSplitter");
            var button = Control<Button>(window, noThreads ? "EmptyClearAllButton" : "EmptyClearContentButton");
            Assert.Equal(100, Field<LocatedSearchHit[]>(window, "searchHits").Length);
            Assert.Equal(100, Control<ListBox>(window, "ResultsList").Items.Count);
            object firstResult = Control<ListBox>(window, "ResultsList").Items[0];
            Assert.StartsWith("[숨김", (string)firstResult.GetType().GetProperty("Label")!.GetValue(firstResult)!);
            Assert.InRange(results.ActualHeight, 178, 182);
            Assert.True(editor.ActualHeight >= 120, $"Editor height: {editor.ActualHeight:0.##} DIP.");
            Assert.Equal(Visibility.Visible, viewport.Visibility);
            Assert.True(viewport.ClipToBounds);
            Assert.Equal(ScrollBarVisibility.Auto, viewport.VerticalScrollBarVisibility);
            Assert.InRange(viewport.ActualHeight, editor.ActualHeight - 1, editor.ActualHeight + 1);
            AssertContained(Bounds(viewport, grid), Bounds(editor, grid), "Empty viewport must stay inside the body row.");
            Assert.True(Bounds(viewport, grid).Bottom <= Bounds(splitter, grid).Top + 1);
            Assert.True(Bounds(viewport, grid).Bottom <= Bounds(results, grid).Top + 1);
            Assert.Equal(Visibility.Visible, button.Visibility);
            Assert.True(button.IsEnabled);
            Assert.True(button.IsHitTestVisible);
            Assert.True(button.ActualHeight >= 29);
            AssertContained(Bounds(button, viewport), new Rect(viewport.RenderSize), "Recovery button must be visible without scrolling.");
            AssertContained(Bounds(Control<TextBlock>(window, "EmptyHint"), viewport), new Rect(viewport.RenderSize), "The cause must be visible beside the recovery action.");
            Assert.True(empty.ActualHeight + viewport.Padding.Top + viewport.Padding.Bottom <= viewport.ActualHeight + 1,
                $"Compact empty panel {empty.ActualHeight:0.##} exceeds viewport {viewport.ActualHeight:0.##}.");
            SaveImage(window, $"empty-{(noThreads ? "threads" : "content")}-{(dark ? "dark" : "light")}-1040x600.png");

            // Long help must scroll only in the body viewport, without enlarging it over the search pane.
            Control<TextBlock>(window, "EmptyDetail").Text = string.Join("\n", Enumerable.Repeat("합성 긴 안내 · 복구 버튼은 위에 있습니다.", 20));
            await Layout(window);
            Assert.True(viewport.ScrollableHeight > 0);
            Assert.True(Bounds(viewport, grid).Bottom <= Bounds(results, grid).Top + 1);
            viewport.ScrollToEnd(); await Layout(window);
            Assert.True(viewport.VerticalOffset > 0);
            viewport.ScrollToTop(); await Layout(window);
            AssertContained(Bounds(button, viewport), new Rect(viewport.RenderSize), "Scrolling back must restore access to recovery.");

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !Field<bool>(window, "busy"));
            await SearchWholeSource(window);
            await Layout(window);
            Assert.Equal(noThreads ? 100 : 50, Field<LogProjection>(window, "projection").Count);
            Assert.Equal(Visibility.Collapsed, empty.Visibility);
            Assert.Equal(Visibility.Collapsed, viewport.Visibility);
            Assert.Equal(100, Field<LocatedSearchHit[]>(window, "searchHits").Length);
            Assert.Empty(Field<EntryFilter>(window, "appliedFilter").Includes);
            var threads = Field<List<ThreadItem>>(window, "threadItems");
            if (noThreads) Assert.All(threads, thread => Assert.True(thread.IsSelected));
            else Assert.Equal(selected, threads.Select(thread => thread.IsSelected));
            Assert.Same(source, Field<LogData>(window, "data"));
            Assert.Equal(text, source.Text);
            Assert.True(editor.IsReadOnly);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    private static async Task SearchWholeSource(MainWindow window)
    {
        Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
        Control<ComboBox>(window, "SearchScopeBox").SelectedIndex = 1;
        Control<TextBox>(window, "SearchBox").Text = "target";
        await (Task)Invoke(window, "SearchAsync")!;
    }
    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
    private static void AssertContained(Rect child, Rect viewport, string reason)
    {
        Assert.True(child.Width > 0 && child.Height > 0, reason + $" Bounds are empty: {child}.");
        Assert.True(child.Left >= viewport.Left - 1 && child.Top >= viewport.Top - 1 &&
            child.Right <= viewport.Right + 1 && child.Bottom <= viewport.Bottom + 1,
            reason + $" Child={child}; viewport={viewport}.");
    }
    private static async Task Until(Func<bool> condition)
    {
        for (int pass = 0; pass < 200 && !condition(); pass++) await Task.Delay(10);
        Assert.True(condition());
    }
    private static async Task Layout(MainWindow window)
    {
        window.Width = 1040; window.Height = 600;
        var content = (FrameworkElement)window.Content;
        for (int pass = 0; pass < 3; pass++)
        {
            content.Measure(new Size(1040, 600)); content.Arrange(new Rect(0, 0, 1040, 600)); content.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
    }
    private static void SaveImage(MainWindow window, string name)
    {
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        Assert.True(File.Exists(System.IO.Path.Combine(root, "ThreadLogViewer.slnx")));
        string directory = System.IO.Path.Combine(root, "TestResults", "v0.7.0", "empty-recovery-layout");
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap(1040, 600, 96, 96, PixelFormats.Pbgra32);
        image.Render((FrameworkElement)window.Content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(System.IO.Path.Combine(directory, name)); encoder.Save(output);
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string method, params object?[] args) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-empty-layout", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
