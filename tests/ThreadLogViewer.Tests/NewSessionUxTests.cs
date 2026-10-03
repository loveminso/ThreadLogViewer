using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
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

/// <summary>Blank input stages, synthetic transfers and unhosted layout; no native input or clipboard.</summary>
public sealed class NewSessionUxTests
{
    private const string First = "[000:00:01] [T1] synthetic A first keep\r\nsynthetic A body\r\n[000:00:02] [T2] synthetic A hidden\n[000:00:03] [T1] synthetic A last keep\nsynthetic A tail";
    private const string Second = "[000:00:09] [T7] synthetic B header\r\nsynthetic B body";

    [Fact]
    public Task PlusAndFileMenuCreateDistinctBlankTabsAndClosingNeverReusesTheirNames() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            var tabs = Control<ListBox>(window, "SessionTabs");
            var plus = Control<Button>(window, "NewSessionButton");
            var menu = Control<MenuItem>(window, "NewSessionMenu");
            Assert.Empty(tabs.Items.Cast<object>());
            Assert.Equal(Visibility.Visible, Control<Border>(window, "SessionTabBar").Visibility);
            Assert.Equal(Visibility.Visible, plus.Visibility);
            Assert.True(plus.IsEnabled);
            Assert.Equal("Ctrl+N", menu.InputGestureText);

            plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var first = tabs.SelectedItem;
            var firstSource = Field<LogData>(window, "data");
            AssertBlank(window, "새 탭 1");
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var second = tabs.SelectedItem;
            AssertBlank(window, "새 탭 2");
            Assert.NotSame(first, second);
            Assert.NotSame(firstSource, Field<LogData>(window, "data"));
            Invoke(window, "CreateBlankSession");
            AssertBlank(window, "새 탭 3");
            Assert.Equal(3, tabs.Items.Count);

            Invoke(window, "NextSession_Click", window, new RoutedEventArgs());
            Assert.Same(first, tabs.SelectedItem);
            Invoke(window, "PreviousSession_Click", window, new RoutedEventArgs());
            AssertBlank(window, "새 탭 3");
            Invoke(window, "CloseActiveSession_Click", window, new RoutedEventArgs());
            Assert.Same(second, tabs.SelectedItem);
            while (tabs.Items.Count > 0) Invoke(window, "CloseActiveSession_Click", window, new RoutedEventArgs());
            Assert.Null(Field<object?>(window, "activeSession"));
            Assert.Equal(Visibility.Visible, Control<Border>(window, "SessionTabBar").Visibility);
            Assert.Equal(Visibility.Visible, plus.Visibility);
            Assert.True(plus.IsEnabled);
            plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            AssertBlank(window, "새 탭 4");
            Assert.Single(tabs.Items.Cast<object>());
            Assert.False(window.IsVisible);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task BlankTabSwitchingPreservesLoadedFilterDisjointSelectionSearchBookmarksTimeAndKeywords() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string path = await folder.Write("synthetic-A.log", First);
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await Open(window, path));
            var tabs = Control<ListBox>(window, "SessionTabs");
            await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(item => item.Id == 1))!;
            var view = Field<LogProjection>(window, "projection");
            var source = Field<LogData>(window, "data");
            object loaded = tabs.SelectedItem;
            Control<TextBox>(window, "IncludeBox").Text = "pending synthetic include";
            Control<TextBox>(window, "SearchBox").Text = "synthetic A";
            Invoke(window, "NavigateVisible", 1, 3, 0);
            Invoke(window, "BookmarkToggle_Click", window, new RoutedEventArgs());
            Invoke(window, "SetTimePoint", true);
            var time = Field<TimeAnchor>(window, "timeA");
            Assert.True((bool)Invoke(window, "ApplyHighlightRules", (object)new HighlightRuleDraft[] { new("synthetic A", 3) })!);
            Invoke(window, "SelectGutterDisplayLine", 1);
            Assert.True((bool)Invoke(window, "ToggleWholeDisplayLine", 4)!);
            Assert.Equal(new[] { 0, 4 }, SelectedRows(window));

            Invoke(window, "CreateBlankSession");
            object blank = tabs.SelectedItem;
            AssertBlank(window, "새 탭 1");
            Assert.Empty(SelectedRows(window));
            Assert.Empty(Field<IEnumerable>(window, "keywordRules").Cast<object>());
            Assert.Empty(Field<BookmarkState>(window, "bookmarks").Items);
            Assert.Null(Field<object?>(window, "timeA"));
            Assert.Equal("", Control<TextBox>(window, "SearchBox").Text);
            await (Task)Invoke(window, "FilterAsync", null, null)!;
            AssertBlank(window, "새 탭 1");

            tabs.SelectedItem = loaded;
            Assert.Same(source, Field<LogData>(window, "data"));
            Assert.Same(view, Field<LogProjection>(window, "projection"));
            Assert.Equal(new[] { 0, 1, 3, 4 }, view.SourceIndexes);
            Assert.Equal(new[] { 0, 4 }, SelectedRows(window));
            Assert.Equal("pending synthetic include", Control<TextBox>(window, "IncludeBox").Text);
            Assert.Equal("synthetic A", Control<TextBox>(window, "SearchBox").Text);
            Assert.Same(time, Field<TimeAnchor>(window, "timeA"));
            Assert.Equal(1, Assert.Single(Field<BookmarkState>(window, "bookmarks").Items).SourceLineIndex);
            object rule = Assert.Single(Field<IEnumerable>(window, "keywordRules").Cast<object>());
            Assert.Equal("synthetic A", Property<string>(rule, "Keyword"));
            tabs.SelectedItem = blank;
            AssertBlank(window, "새 탭 1");
            Assert.Equal(First, await File.ReadAllTextAsync(path));
            Assert.True(Control<TextEditor>(window, "Editor").IsReadOnly);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SyntheticTextPasteFillsActiveBlankInPlaceThenLoadedTabPasteCreatesAnotherTab() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string path = await folder.Write("synthetic-A.log", First);
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await Open(window, path));
            var tabs = Control<ListBox>(window, "SessionTabs");
            object loaded = tabs.SelectedItem;
            var original = Field<LogData>(window, "data");
            Invoke(window, "CreateBlankSession");
            object blank = tabs.SelectedItem;
            var blankSource = Field<LogData>(window, "data");
            Assert.True(await Paste(window, TextData(Second)));
            Assert.Equal(2, tabs.Items.Count);
            Assert.Equal(1, tabs.SelectedIndex);
            Assert.NotSame(blank, tabs.SelectedItem);
            Assert.DoesNotContain(blank, tabs.Items.Cast<object>());
            Assert.False(Property<bool>(tabs.SelectedItem, "IsBlank"));
            Assert.Equal(Second, Control<TextEditor>(window, "Editor").Text);
            Assert.Null(Field<LogData>(window, "data").SourcePath);
            Assert.Empty(blankSource.Lines);
            Assert.Equal("", blankSource.Text);
            Assert.True(Control<TextEditor>(window, "Editor").IsReadOnly);

            Assert.True(await Paste(window, TextData("synthetic short line")));
            Assert.Equal(3, tabs.Items.Count);
            Assert.Equal("synthetic short line", Control<TextEditor>(window, "Editor").Text);
            tabs.SelectedItem = loaded;
            Assert.Same(original, Field<LogData>(window, "data"));
            Assert.Equal(First, Control<TextEditor>(window, "Editor").Text);
            Assert.Equal(First, await File.ReadAllTextAsync(path));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FileBatchFillsBlankAtFirstSuccessfulLoadAndKeepsEverySourceFileUntouched() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string a = await folder.Write("synthetic-A.log", First);
        string b = await folder.Write("synthetic-B.log", Second);
        string c = await folder.Write("synthetic-C.txt", "synthetic C without final newline");
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await Open(window, a));
            var tabs = Control<ListBox>(window, "SessionTabs");
            object loaded = tabs.SelectedItem;
            Invoke(window, "CreateBlankSession");
            object blank = tabs.SelectedItem;
            var files = new DataObject();
            files.SetData(DataFormats.FileDrop, new[] { Path.Combine(folder.Path, "missing-synthetic.log"), b, c });
            Assert.False(await Paste(window, files)); // One failed file does not cancel later successful files.
            Assert.Equal(3, tabs.Items.Count);
            Assert.Same(loaded, tabs.Items[0]);
            Assert.DoesNotContain(blank, tabs.Items.Cast<object>());
            Assert.Equal(b, Property<LogData>(tabs.Items[1], "Source").SourcePath);
            Assert.Equal(c, Property<LogData>(tabs.Items[2], "Source").SourcePath);
            Assert.All(tabs.Items.Cast<object>(), session => Assert.False(Property<bool>(session, "IsBlank")));
            Assert.Equal(First, await File.ReadAllTextAsync(a));
            Assert.Equal(Second, await File.ReadAllTextAsync(b));
            Assert.Equal("synthetic C without final newline", await File.ReadAllTextAsync(c));
            Assert.True(Control<TextEditor>(window, "Editor").IsReadOnly);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ReopeningExistingFileActivatesItAndPreservesTheUnusedBlankTab() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string path = await folder.Write("synthetic-A.log", First);
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await Open(window, path));
            var tabs = Control<ListBox>(window, "SessionTabs");
            var source = Field<LogData>(window, "data");
            object loaded = tabs.SelectedItem;
            Invoke(window, "CreateBlankSession");
            object blank = tabs.SelectedItem;
            Assert.True(await Open(window, Path.Combine(folder.Path, ".", "synthetic-A.log")));
            Assert.Equal(2, tabs.Items.Count);
            Assert.Same(loaded, tabs.SelectedItem);
            Assert.Same(source, Field<LogData>(window, "data"));
            Assert.Contains(blank, tabs.Items.Cast<object>());
            tabs.SelectedItem = blank;
            AssertBlank(window, "새 탭 1");
            Assert.Equal(First, await File.ReadAllTextAsync(path));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FailedOrUnsupportedTransfersAndMissingFilesKeepTheExactBlankSession() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            Invoke(window, "CreateBlankSession");
            var tabs = Control<ListBox>(window, "SessionTabs");
            object blank = tabs.SelectedItem;
            var source = Field<LogData>(window, "data");
            var view = Field<LogProjection>(window, "projection");
            var unsupported = new DataObject(); unsupported.SetData("synthetic unsupported type", "synthetic payload");
            Func<IDataObject?>[] providers = [() => null, () => TextData(""), () => unsupported,
                () => throw new InvalidDataException("synthetic deferred clipboard failure")];
            foreach (var provider in providers)
            {
                Assert.False(await (Task<bool>)Invoke(window, "PasteFromDataObjectAsync", provider)!);
                Assert.Same(blank, tabs.SelectedItem);
                Assert.Same(source, Field<LogData>(window, "data"));
                Assert.Same(view, Field<LogProjection>(window, "projection"));
                AssertBlank(window, "새 탭 1");
            }
            Assert.False(await Open(window, Path.Combine(folder.Path, "missing-synthetic.log")));
            Assert.Single(tabs.Items.Cast<object>());
            Assert.Same(blank, tabs.SelectedItem);
            Assert.Same(source, Field<LogData>(window, "data"));
            AssertBlank(window, "새 탭 1");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ARealEmptyFileReplacesBlankWithAnOrdinaryReadOnlyFileSession() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string empty = await folder.Write("synthetic-empty.log", "");
        var window = new MainWindow(folder.Path, false);
        try
        {
            Invoke(window, "CreateBlankSession");
            var tabs = Control<ListBox>(window, "SessionTabs");
            object blank = tabs.SelectedItem;
            Assert.True(await Open(window, empty));
            Assert.Single(tabs.Items.Cast<object>());
            Assert.NotSame(blank, tabs.SelectedItem);
            Assert.False(Property<bool>(tabs.SelectedItem, "IsBlank"));
            Assert.Equal(empty, Field<LogData>(window, "data").SourcePath);
            Assert.Equal("", Control<TextEditor>(window, "Editor").Text);
            Assert.True(Control<TextEditor>(window, "Editor").IsReadOnly);
            Assert.Equal("", await File.ReadAllTextAsync(empty));
            Assert.Contains("빈 파일", Control<TextBlock>(window, "EmptyHint").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CreatingBlankInvalidatesDelayedLoadAndBatchSoLateResultsCannotReplaceIt() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string path = await folder.Write("synthetic-A.log", First);
        var window = new MainWindow(folder.Path, false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? pending = null;
        try
        {
            Assert.True(await Open(window, path));
            var tabs = Control<ListBox>(window, "SessionTabs");
            var original = Field<LogData>(window, "data");
            object loaded = tabs.SelectedItem;
            var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> delayed = async (token, progress) =>
            {
                started.SetResult(token);
                await release.Task;
                progress.Report(new("synthetic obsolete load", 100));
                return LogParser.ParsePastedText(Second);
            };
            pending = (Task<bool>)Invoke(window, "LoadAsync", delayed, "synthetic delayed load", "synthetic failure")!;
            var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            long batch = Field<long>(window, "fileBatchVersion");
            Assert.True(Field<bool>(window, "busy"));
            Assert.True(Control<Button>(window, "NewSessionButton").IsEnabled);
            Control<Button>(window, "NewSessionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            object blank = tabs.SelectedItem;
            var blankSource = Field<LogData>(window, "data");
            Assert.True(token.IsCancellationRequested);
            Assert.True(Field<long>(window, "fileBatchVersion") > batch);
            Assert.False(Field<bool>(window, "busy"));
            release.SetResult();
            Assert.False(await pending);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert.Equal(2, tabs.Items.Count);
            Assert.Same(blank, tabs.SelectedItem);
            Assert.Same(blankSource, Field<LogData>(window, "data"));
            AssertBlank(window, "새 탭 1");
            Assert.DoesNotContain("synthetic obsolete", Control<TextBlock>(window, "OperationStatus").Text);
            tabs.SelectedItem = loaded;
            Assert.Same(original, Field<LogData>(window, "data"));
            Assert.Equal(First, Control<TextEditor>(window, "Editor").Text);
            Assert.Equal(First, await File.ReadAllTextAsync(path));
        }
        finally
        {
            release.TrySetResult();
            if (pending is not null) await pending;
            window.Close();
        }
    });

    [Theory]
    [InlineData(1040, 600, true)]
    [InlineData(1360, 860, false)]
    public Task PlusStaysInsideFixedTabRailWithoutOverlappingManyScrollingTabs(double width, double height, bool dark) => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false) { Width = width, Height = height };
        try
        {
            Control<ComboBox>(window, "ThemeBox").SelectedIndex = dark ? 0 : 1;
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
            for (int index = 0; index < 24; index++) Invoke(window, "CreateBlankSession");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            content.UpdateLayout();
            var tabs = Control<ListBox>(window, "SessionTabs");
            var plus = Control<Button>(window, "NewSessionButton");
            var rail = Control<Border>(window, "SessionTabBar");
            Rect plusBounds = new(plus.TranslatePoint(new Point(), content), plus.RenderSize);
            Rect tabsBounds = new(tabs.TranslatePoint(new Point(), content), tabs.RenderSize);
            Assert.Equal(24, tabs.Items.Count);
            Assert.True(plus.ActualWidth >= 24 && plus.ActualHeight >= 24);
            Assert.InRange(plusBounds.Left, 0, width);
            Assert.InRange(plusBounds.Right, 0, width);
            Assert.InRange(plusBounds.Bottom, 0, height);
            Assert.True(tabsBounds.Right <= plusBounds.Left + 0.5);
            Assert.Equal(Visibility.Visible, rail.Visibility);
            var scroll = Descendant<ScrollViewer>(tabs);
            Assert.NotNull(scroll);
            Assert.True(scroll!.ExtentWidth > scroll.ViewportWidth);
            var viewport = Descendant<ScrollContentPresenter>(scroll);
            Assert.NotNull(viewport);
            var activeTab = Assert.IsType<ListBoxItem>(tabs.ItemContainerGenerator.ContainerFromItem(tabs.SelectedItem));
            Rect activeBounds = new(activeTab.TranslatePoint(new Point(), content), activeTab.RenderSize);
            Rect viewportBounds = new(viewport!.TranslatePoint(new Point(), content), viewport.RenderSize);
            Assert.True(activeBounds.Width > 0);
            Assert.True(activeBounds.Left >= viewportBounds.Left - 0.5, "The active tab starts outside the horizontal viewport.");
            Assert.True(activeBounds.Right <= viewportBounds.Right + 0.5, "The active tab title and close button must remain visible.");
            Assert.True(Control<TextEditor>(window, "Editor").ActualHeight > 250);
            AssertBlank(window, "새 탭 24");
            Assert.False(window.IsVisible);

            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            Assert.True(File.Exists(Path.Combine(root, "ThreadLogViewer.slnx")));
            string outputFolder = Path.Combine(root, "TestResults", "v0.6.3"); Directory.CreateDirectory(outputFolder);
            var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(outputFolder, dark ? "new-session-dark.png" : "new-session-light.png"));
            encoder.Save(output);
        }
        finally { window.Close(); }
    });

    private static void AssertBlank(MainWindow window, string title)
    {
        object session = Field<object>(window, "activeSession");
        var source = Field<LogData>(window, "data");
        var view = Field<LogProjection>(window, "projection");
        var editor = Control<TextEditor>(window, "Editor");
        Assert.True(Property<bool>(session, "IsBlank"));
        Assert.Equal(title, Property<string>(session, "DisplayTitle"));
        Assert.Same(source, Property<LogData>(session, "Source"));
        Assert.Same(source, view.Source);
        Assert.Null(source.SourcePath);
        Assert.Equal("", source.Text); Assert.Empty(source.Lines); Assert.Empty(source.Entries);
        Assert.Equal(0, view.Count); Assert.Empty(view.SourceIndexes); Assert.Equal("", editor.Text);
        Assert.True(editor.IsReadOnly);
        Assert.False(Control<Button>(window, "ExportButton").IsEnabled);
        Assert.False(Control<MenuItem>(window, "AnalysisMenu").IsEnabled);
        Assert.False(Control<MenuItem>(window, "FindMenu").IsEnabled);
        Assert.False(Control<MenuItem>(window, "GoToMenu").IsEnabled);
        Assert.False(Control<MenuItem>(window, "EncodingMenu").IsEnabled);
        Assert.Equal(Visibility.Visible, Control<StackPanel>(window, "EmptyPanel").Visibility);
        string guidance = Control<TextBlock>(window, "EmptyHint").Text + Control<TextBlock>(window, "EmptyDetail").Text;
        Assert.Contains("Ctrl+V", guidance); Assert.Contains("Ctrl+O", guidance);
    }
    private static int[] SelectedRows(MainWindow window) => (int[]?)Invoke(window, "CaptureWholeLineSelection") ?? [];
    private static DataObject TextData(string text) { var data = new DataObject(); data.SetData(DataFormats.UnicodeText, text); return data; }
    private static Task<bool> Paste(MainWindow window, IDataObject data) => (Task<bool>)Invoke(window, "PasteFromDataObjectAsync", (Func<IDataObject?>)(() => data))!;
    private static Task<bool> Open(MainWindow window, string path) => (Task<bool>)Invoke(window, "OpenAsync", path, EncodingMode.Auto)!;
    private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;
    private static T? Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) return found;
            if (Descendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-new-session", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public Task<string> Write(string name, string text) => WriteCore(name, text);
        private async Task<string> WriteCore(string name, string text) { string path = System.IO.Path.Combine(Path, name); await File.WriteAllTextAsync(path, text, new UTF8Encoding(false, true)); return path; }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
