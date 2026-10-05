using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

/// <summary>Synthetic sources and settings, routed events and offscreen WPF layout; no visible windows or real input.</summary>
public sealed class SearchResultsLayoutTests
{
    private static readonly string SyntheticLog = string.Join('\n', Enumerable.Range(0, 72).Select(index =>
        $"[000:{index / 60:00}:{index % 60:00}.000] [T{index % 3 + 1}] synthetic {(index % 2 == 0 ? "target" : "normal")} record {index:00}"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SavedHeightAndCollapsedPreferenceReachActualResultsPane(bool collapsed) => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Assert.True(store.Save(new UiSettings { ResultsHeight = 260, ResultsCollapsed = collapsed }).Success);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await LoadAndSearch(window);
            await Layout(window, 1360, 860);
            Assert.Equal(260, Field<double>(window, "preferredResultsHeight"));
            Assert.Equal(collapsed, Field<bool>(window, "resultsCollapsed"));
            Assert.Equal(36, Field<LocatedSearchHit[]>(window, "searchHits").Length);
            Assert.Equal(Visibility.Visible, Control<Border>(window, "ResultsPanel").Visibility);
            Assert.True(Control<Button>(window, "ResultsToggleButton").IsEnabled);
            if (collapsed)
            {
                Assert.InRange(Control<RowDefinition>(window, "ResultsRow").ActualHeight, 1, 79);
                Assert.Equal(Visibility.Collapsed, Control<ListBox>(window, "ResultsList").Visibility);
                Assert.Equal(0, Control<RowDefinition>(window, "ResultsSplitterRow").ActualHeight);
            }
            else
            {
                Assert.InRange(Control<Border>(window, "ResultsPanel").ActualHeight, 258, 262);
                Assert.True(Control<ListBox>(window, "ResultsList").ActualHeight > 150);
                Assert.InRange(Control<RowDefinition>(window, "ResultsSplitterRow").ActualHeight, 5, 7);
            }
            AssertEditorAndPaneFit(window);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task RoutedSplitterDragAndKeyboardGrowResultsAndProtectMinimumEditorHeight() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await LoadAndSearch(window);
            await Layout(window, 1360, 1000);
            var panel = Control<Border>(window, "ResultsPanel");
            double initial = panel.ActualHeight;
            DragSplitter(window, -80);
            await Layout(window, 1360, 1000);
            Assert.True(panel.ActualHeight > initial + 60, "An upward routed drag must enlarge the results pane.");
            Assert.InRange(Field<double>(window, "preferredResultsHeight"), panel.ActualHeight - 2, panel.ActualHeight + 2);
            double draggedHeight = panel.ActualHeight;
            var key = KeyArgs(Key.Up);
            Control<GridSplitter>(window, "ResultsSplitter").RaiseEvent(key);
            Assert.True(key.Handled, "The splitter must handle its keyboard resize action.");
            await Layout(window, 1360, 1000);
            Assert.True(panel.ActualHeight > draggedHeight, "Up must enlarge the results pane through the routed key handler.");

            DragSplitter(window, -10000);
            await Layout(window, 1360, 1000);
            Assert.InRange(Field<double>(window, "preferredResultsHeight"), 80, 1200);
            AssertEditorAndPaneFit(window);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CollapseAndExpandPreserveMatchesHighlightsAndRoutedF3Navigation() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await LoadAndSearch(window);
            await Layout(window, 1360, 860);
            var hits = Field<LocatedSearchHit[]>(window, "searchHits");
            var list = Control<ListBox>(window, "ResultsList");
            var items = list.ItemsSource;
            var highlights = Field<SearchHighlightRenderer>(window, "searchRenderer").Index;
            Assert.Equal(hits.Length, highlights.Count);
            ToggleResults(window);
            await Layout(window, 1360, 860);
            Assert.True(Field<bool>(window, "resultsCollapsed"));
            Assert.Equal(Visibility.Visible, Control<Border>(window, "SearchBar").Visibility);
            Assert.Equal(Visibility.Collapsed, list.Visibility);
            Assert.Same(hits, Field<LocatedSearchHit[]>(window, "searchHits"));
            Assert.Same(items, list.ItemsSource);
            Assert.Same(highlights, Field<SearchHighlightRenderer>(window, "searchRenderer").Index);

            var firstKey = KeyArgs(Key.F3);
            window.RaiseEvent(firstKey);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(firstKey.Handled);
            Assert.Equal(hits[0].SourceOffset, Control<TextEditor>(window, "Editor").SelectionStart);
            Assert.Equal(hits[0].Length, Control<TextEditor>(window, "Editor").SelectionLength);
            var secondKey = KeyArgs(Key.F3);
            window.RaiseEvent(secondKey);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(secondKey.Handled);
            Assert.Equal(hits[1].SourceOffset, Control<TextEditor>(window, "Editor").SelectionStart);
            Assert.True(Field<bool>(window, "resultsCollapsed"));

            ToggleResults(window);
            await Layout(window, 1360, 860);
            Assert.False(Field<bool>(window, "resultsCollapsed"));
            Assert.True(list.ActualHeight > 80);
            Assert.Same(hits, Field<LocatedSearchHit[]>(window, "searchHits"));
            Assert.Same(items, list.ItemsSource);
            Assert.Same(highlights, Field<SearchHighlightRenderer>(window, "searchRenderer").Index);
            Assert.Equal(1, list.SelectedIndex);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ZeroMatchesSearchCloseAndTabWithoutSearchLeaveNoResultsRowsOrSplitterSpace() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await LoadAndSearch(window);
            await Layout(window, 1360, 860);
            Assert.True(Control<RowDefinition>(window, "ResultsRow").ActualHeight >= 80);
            await Search(window, "synthetic absent phrase");
            await Layout(window, 1360, 860);
            Assert.Empty(Field<LocatedSearchHit[]>(window, "searchHits"));
            AssertResultsHiddenWithoutSpace(window);

            await Search(window, "target");
            await Layout(window, 1360, 860);
            Invoke(window, "CloseSearch_Click", window, new RoutedEventArgs());
            await Layout(window, 1360, 860);
            Assert.Equal(Visibility.Collapsed, Control<Border>(window, "SearchBar").Visibility);
            AssertResultsHiddenWithoutSpace(window);

            await Search(window, "target");
            var tabs = Control<ListBox>(window, "SessionTabs");
            object searchedTab = tabs.SelectedItem;
            await Load(window, "[000:00:01] [T7] synthetic second tab without a query");
            await Layout(window, 1360, 860);
            object unsearchedTab = tabs.SelectedItem;
            AssertResultsHiddenWithoutSpace(window);
            tabs.SelectedItem = searchedTab;
            await (Task)Invoke(window, "SearchAsync")!;
            await Layout(window, 1360, 860);
            Assert.Equal(36, Field<LocatedSearchHit[]>(window, "searchHits").Length);
            Assert.True(Control<RowDefinition>(window, "ResultsRow").ActualHeight >= 80);
            tabs.SelectedItem = unsearchedTab;
            await Layout(window, 1360, 860);
            AssertResultsHiddenWithoutSpace(window);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SmallWindowAndContextClampActualHeightWithoutLosingPreferredHeightAndRenderBothThemes() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Assert.True(store.Save(new UiSettings { ResultsHeight = 420 }).Success);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await LoadAndSearch(window);
            await Layout(window, 1360, 1000);
            Assert.InRange(Control<Border>(window, "ResultsPanel").ActualHeight, 418, 422);
            await Layout(window, 1040, 600);
            double smallHeight = Control<Border>(window, "ResultsPanel").ActualHeight;
            Assert.True(smallHeight < 420,
                $"The small viewport must clamp the preferred 420 DIP pane; smallHeight={smallHeight:0.##}. " + DescribeResultsLayout(window));
            Assert.Equal(420, Field<double>(window, "preferredResultsHeight"));
            AssertEditorAndPaneFit(window);

            Control<TextBlock>(window, "ContextStatus").Text = "synthetic surrounding records · all three threads";
            Control<Border>(window, "ContextPanel").Visibility = Visibility.Visible;
            await Layout(window, 1040, 600);
            string contextLayout = DescribeResultsLayout(window);
            Assert.True(Control<Border>(window, "ContextPanel").ActualHeight > 20,
                "The visible context panel must receive actual layout space. " + contextLayout);
            Assert.True(Control<Border>(window, "ResultsPanel").ActualHeight <= smallHeight + 1,
                $"Context must not enlarge the clamped results pane (previous {smallHeight:0.##} DIP). " + contextLayout);
            Assert.Equal(420, Field<double>(window, "preferredResultsHeight"));
            AssertEditorAndPaneFit(window);
            SaveOffscreenImage(window, 1040, 600, "results-dark-small.png");

            Control<Border>(window, "ContextPanel").Visibility = Visibility.Collapsed;
            Control<ComboBox>(window, "ThemeBox").SelectedIndex = 1;
            await Layout(window, 1360, 1000);
            Assert.InRange(Control<Border>(window, "ResultsPanel").ActualHeight, 418, 422);
            Assert.Equal(420, Field<double>(window, "preferredResultsHeight"));
            await Layout(window, 1360, 860);
            AssertEditorAndPaneFit(window);
            SaveOffscreenImage(window, 1360, 860, "results-light-large.png");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task PersistenceDisabledKeepsSyntheticSettingsBytesAfterUserResizeAndCollapse() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Assert.True(store.Save(new UiSettings { ResultsHeight = 230 }).Success);
        byte[] original = File.ReadAllBytes(store.FilePath);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await LoadAndSearch(window);
            await Layout(window, 1360, 860);
            DragSplitter(window, -55);
            await Layout(window, 1360, 860);
            Assert.True(Field<double>(window, "preferredResultsHeight") > 230);
            ToggleResults(window);
            Assert.True(Field<bool>(window, "resultsCollapsed"));
            Invoke(window, "SaveSettingsNow");
            Assert.Equal(original, File.ReadAllBytes(store.FilePath));
        }
        finally { window.Close(); }
        Assert.Equal(original, File.ReadAllBytes(store.FilePath));
    });

    [Fact]
    public Task UserResizeAndCollapsePersistAndRestoreAfterWindowRecreation() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Assert.True(store.Save(new UiSettings { ResultsHeight = 220 }).Success);
        double preferred;
        var window = new MainWindow(folder.Path, true);
        try
        {
            await LoadAndSearch(window);
            await Layout(window, 1360, 860);
            DragSplitter(window, -65);
            await Layout(window, 1360, 860);
            preferred = Field<double>(window, "preferredResultsHeight");
            Assert.True(preferred > 220);
            ToggleResults(window);
            Invoke(window, "SaveSettingsNow");
            var saved = store.Load();
            Assert.True(saved.Success);
            Assert.InRange(saved.Settings.ResultsHeight, preferred - 0.5, preferred + 0.5);
            Assert.True(saved.Settings.ResultsCollapsed);
        }
        finally { window.Close(); }

        var reopened = new MainWindow(folder.Path, true);
        try
        {
            await LoadAndSearch(reopened);
            await Layout(reopened, 1360, 860);
            Assert.InRange(Field<double>(reopened, "preferredResultsHeight"), preferred - 0.5, preferred + 0.5);
            Assert.True(Field<bool>(reopened, "resultsCollapsed"));
            Assert.Equal(Visibility.Collapsed, Control<ListBox>(reopened, "ResultsList").Visibility);
            Assert.Equal(0, Control<RowDefinition>(reopened, "ResultsSplitterRow").ActualHeight);
            ToggleResults(reopened);
            await Layout(reopened, 1360, 860);
            Assert.InRange(Control<Border>(reopened, "ResultsPanel").ActualHeight, preferred - 2, preferred + 2);
            AssertEditorAndPaneFit(reopened);
        }
        finally { reopened.Close(); }
    });

    [Fact]
    public Task SettingsSaveDuringCancelledDragNeverPersistsTheTemporaryHeight() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Assert.True(store.Save(new UiSettings { ResultsHeight = 220 }).Success);
        var window = new MainWindow(folder.Path, true);
        try
        {
            await LoadAndSearch(window);
            await Layout(window, 1360, 860);
            Assert.InRange(Control<Border>(window, "ResultsPanel").ActualHeight, 218, 222);
            Invoke(window, "ScheduleSettingsSave");
            Assert.True(Field<bool>(window, "settingsDirty"));
            BeginSplitterDrag(window, -65);
            await Layout(window, 1360, 860);
            Assert.True(Control<Border>(window, "ResultsPanel").ActualHeight > 250,
                "The drag must show a temporary larger pane before it is cancelled. " + DescribeResultsLayout(window));
            Assert.Equal(220, Field<double>(window, "preferredResultsHeight"));

            // Exercise an already queued settings timer while the drag is still in progress.
            Invoke(window, "SaveSettingsNow");
            var duringDrag = store.Load();
            Assert.True(duringDrag.Success);
            Assert.Equal(220, duringDrag.Settings.ResultsHeight);
            CompleteSplitterDrag(window, -65, canceled: true);
            await Layout(window, 1360, 860);
            Assert.False(Field<bool>(window, "resultsDragActive"));
            Assert.Equal(220, Field<double>(window, "preferredResultsHeight"));
            Assert.InRange(Control<Border>(window, "ResultsPanel").ActualHeight, 218, 222);
            Assert.Equal(220, store.Load().Settings.ResultsHeight);
        }
        finally { window.Close(); }
        Assert.Equal(220, store.Load().Settings.ResultsHeight);

        var reopened = new MainWindow(folder.Path, true);
        try
        {
            await LoadAndSearch(reopened);
            await Layout(reopened, 1360, 860);
            Assert.Equal(220, Field<double>(reopened, "preferredResultsHeight"));
            Assert.False(Field<bool>(reopened, "resultsCollapsed"));
            Assert.InRange(Control<Border>(reopened, "ResultsPanel").ActualHeight, 218, 222);
            AssertEditorAndPaneFit(reopened);
        }
        finally { reopened.Close(); }
    });

    [Fact]
    public Task RoutedEscapeCancelsSyntheticDragAndPreservesSearchResultsAndHighlights() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Assert.True(store.Save(new UiSettings { ResultsHeight = 220 }).Success);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await LoadAndSearch(window);
            await Layout(window, 1360, 860);
            var hits = Field<LocatedSearchHit[]>(window, "searchHits");
            var list = Control<ListBox>(window, "ResultsList");
            var items = list.ItemsSource;
            var highlights = Field<SearchHighlightRenderer>(window, "searchRenderer").Index;
            BeginSplitterDrag(window, -65);
            await Layout(window, 1360, 860);
            Assert.True(Control<Border>(window, "ResultsPanel").ActualHeight > 250,
                "The drag must change visible geometry before Escape cancels it. " + DescribeResultsLayout(window));
            Assert.True(Field<bool>(window, "resultsDragActive"));
            // Raising routed drag events does not capture the real mouse or set Thumb.IsDragging.
            Assert.False(Control<GridSplitter>(window, "ResultsSplitter").IsDragging);

            var escape = KeyArgs(Key.Escape);
            window.RaiseEvent(escape);
            await Layout(window, 1360, 860);
            Assert.True(escape.Handled, "Escape must be consumed by the active resize before the search-close shortcut.");
            Assert.False(Field<bool>(window, "resultsDragActive"));
            Assert.Equal(220, Field<double>(window, "preferredResultsHeight"));
            Assert.InRange(Control<Border>(window, "ResultsPanel").ActualHeight, 218, 222);
            Assert.Equal(Visibility.Visible, Control<Border>(window, "SearchBar").Visibility);
            Assert.Equal(Visibility.Visible, Control<Border>(window, "ResultsPanel").Visibility);
            Assert.Same(hits, Field<LocatedSearchHit[]>(window, "searchHits"));
            Assert.Same(items, list.ItemsSource);
            Assert.Same(highlights, Field<SearchHighlightRenderer>(window, "searchRenderer").Index);
            Assert.Equal(36, hits.Length);
            Assert.Equal(hits.Length, highlights.Count);
            Assert.Equal("target", Control<TextBox>(window, "SearchBox").Text);

            // A completion queued after cancellation must not commit the abandoned drag height.
            CompleteSplitterDrag(window, -65, canceled: false);
            await Layout(window, 1360, 860);
            Assert.Equal(220, Field<double>(window, "preferredResultsHeight"));
            Assert.InRange(Control<Border>(window, "ResultsPanel").ActualHeight, 218, 222);
            Assert.Same(hits, Field<LocatedSearchHit[]>(window, "searchHits"));
            AssertEditorAndPaneFit(window);
        }
        finally { window.Close(); }
    });

    private static void AssertEditorAndPaneFit(MainWindow window)
    {
        var grid = Control<Grid>(window, "LogContentGrid");
        var editor = Control<TextEditor>(window, "Editor");
        var panel = Control<Border>(window, "ResultsPanel");
        Rect editorBounds = new(editor.TranslatePoint(new Point(), grid), editor.RenderSize);
        Rect resultBounds = new(panel.TranslatePoint(new Point(), grid), panel.RenderSize);
        string layout = DescribeResultsLayout(window);
        Assert.True(editor.ActualHeight >= 119, $"Results pane left only {editor.ActualHeight:0.##} DIP for the editor. " + layout);
        Assert.True(resultBounds.Top >= editorBounds.Bottom - 1, "The results pane must not overlap the editor. " + layout);
        Assert.InRange(resultBounds.Bottom, 0, grid.ActualHeight + 1);
        Assert.False(window.IsVisible);
    }

    private static string DescribeResultsLayout(MainWindow window)
    {
        var context = Control<Border>(window, "ContextPanel");
        var grid = Control<Grid>(window, "LogContentGrid");
        var editor = Control<TextEditor>(window, "Editor");
        var results = Control<Border>(window, "ResultsPanel");
        var root = (FrameworkElement)window.Content;
        DependencyObject? ancestor = VisualTreeHelper.GetParent(grid);
        while (ancestor is not null && ancestor is not Grid) ancestor = VisualTreeHelper.GetParent(ancestor);
        var parentGrid = ancestor as Grid;
        string parentLayout = parentGrid is null ? "ParentGrid: none" :
            $"ParentGrid: ActualHeight={parentGrid.ActualHeight:0.##}, DesiredHeight={parentGrid.DesiredSize.Height:0.##}, " +
            $"LayoutSlotHeight={LayoutInformation.GetLayoutSlot(parentGrid).Height:0.##}";
        string rows = string.Join("; ", grid.RowDefinitions.Select((row, index) =>
            $"row{index}: Height={row.Height}, ActualHeight={row.ActualHeight:0.##}, Min={row.MinHeight:0.##}, Max={row.MaxHeight:0.##}"));
        return $"ContextPanel: Visibility={context.Visibility}, DesiredSize={context.DesiredSize}, ActualHeight={context.ActualHeight:0.##}, " +
            $"MeasureValid={context.IsMeasureValid}, ArrangeValid={context.IsArrangeValid}; " +
            $"LogContentGrid: ActualHeight={grid.ActualHeight:0.##}, DesiredSize={grid.DesiredSize}, " +
            $"LayoutSlotHeight={LayoutInformation.GetLayoutSlot(grid).Height:0.##}, MeasureValid={grid.IsMeasureValid}, ArrangeValid={grid.IsArrangeValid}; " +
            $"{parentLayout}; RootContentActualHeight={root.ActualHeight:0.##}, RootContentDesiredHeight={root.DesiredSize.Height:0.##}; " +
            $"EditorHeight={editor.ActualHeight:0.##}; ResultsPanel: Visibility={results.Visibility}, Height={results.ActualHeight:0.##}; " +
            $"PreferredHeight={Field<double>(window, "preferredResultsHeight"):0.##}; {rows}";
    }

    private static void AssertResultsHiddenWithoutSpace(MainWindow window)
    {
        Assert.Equal(Visibility.Collapsed, Control<Border>(window, "ResultsPanel").Visibility);
        Assert.Equal(0, Control<RowDefinition>(window, "ResultsRow").ActualHeight);
        Assert.Equal(0, Control<RowDefinition>(window, "ResultsSplitterRow").ActualHeight);
        Assert.Equal(Visibility.Collapsed, Control<GridSplitter>(window, "ResultsSplitter").Visibility);
    }

    private static void ToggleResults(MainWindow window) =>
        Control<Button>(window, "ResultsToggleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void DragSplitter(MainWindow window, double verticalChange)
    {
        BeginSplitterDrag(window, verticalChange);
        CompleteSplitterDrag(window, verticalChange, canceled: false);
    }

    private static void BeginSplitterDrag(MainWindow window, double verticalChange)
    {
        var splitter = Control<GridSplitter>(window, "ResultsSplitter");
        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        splitter.RaiseEvent(new DragDeltaEventArgs(0, verticalChange) { RoutedEvent = Thumb.DragDeltaEvent });
    }

    private static void CompleteSplitterDrag(MainWindow window, double verticalChange, bool canceled) =>
        Control<GridSplitter>(window, "ResultsSplitter").RaiseEvent(new DragCompletedEventArgs(0, verticalChange, canceled)
            { RoutedEvent = Thumb.DragCompletedEvent });

    private static async Task LoadAndSearch(MainWindow window)
    {
        await Load(window, SyntheticLog);
        await Search(window, "target");
    }

    private static Task Load(MainWindow window, string text)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) =>
            Task.FromResult(LogParser.ParsePastedText(text, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic results layout", "synthetic failure")!;
    }

    private static async Task Search(MainWindow window, string query)
    {
        Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
        Control<TextBox>(window, "SearchBox").Text = query;
        await (Task)Invoke(window, "SearchAsync")!;
    }

    private static async Task Layout(MainWindow window, double width, double height)
    {
        window.Width = width; window.Height = height;
        var content = (FrameworkElement)window.Content;
        for (int pass = 0; pass < 3; pass++)
        {
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        Control<TextEditor>(window, "Editor").TextArea.TextView.EnsureVisualLines();
    }

    private static void SaveOffscreenImage(MainWindow window, int width, int height, string name)
    {
        string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        Assert.True(File.Exists(System.IO.Path.Combine(projectRoot, "ThreadLogViewer.slnx")));
        string outputDirectory = System.IO.Path.Combine(projectRoot, "TestResults", "results-layout");
        Directory.CreateDirectory(outputDirectory);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render((FrameworkElement)window.Content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(System.IO.Path.Combine(outputDirectory, name));
        encoder.Save(output);
    }

    private static KeyEventArgs KeyArgs(Key key) => new(Keyboard.PrimaryDevice, new SyntheticPresentationSource(), 0, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;

    private sealed class SyntheticPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = new Canvas();
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-results-layout", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
