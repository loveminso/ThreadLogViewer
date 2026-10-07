using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

/// <summary>Synthetic sources and unhosted controls only; no windows or clipboard access.</summary>
public sealed class SessionTabsTests
{
    private const string First = "[000:00:01.000] [T1] synthetic A first keep\r\nA first body\r\n[000:00:02.500] [T2] synthetic A other skip\n[000:00:03.000] [T1] synthetic A last keep\nA last body";
    private const string Second = "[000:00:09.000] [T7] synthetic B first keep\nB first body\n[000:00:10.750] [T8] synthetic B other skip\r\n[000:00:11.000] [T7] synthetic B last keep\r\nB last body";
    private const string Third = "[000:00:20] [T9] synthetic C first\n[000:00:21] [T10] synthetic C last";

    [Fact]
    public Task MultiFileTabsKeepIndependentFiltersSelectionTimeSearchKeywordsAndContext() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string first = await folder.Write("synthetic-A.log", First);
        string second = await folder.Write("synthetic-B.log", Second);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await OpenFiles(window, first, second);
            var tabs = Control<ListBox>(window, "SessionTabs");
            var editor = Control<TextEditor>(window, "Editor");
            Assert.Equal(2, tabs.Items.Count);
            Assert.Equal(second, Field<LogData>(window, "data").SourcePath);

            // Give B a narrow committed view and an independently unfinished filter draft.
            await SetThreads(window, t => t.Id == 7);
            await Filter(window, new EntryFilter(["first"], [], true, true));
            Invoke(window, "NavigateVisible", 1, 3, 0);
            Invoke(window, "SetTimePoint", true);
            Control<TextBox>(window, "KeywordBox").Text = "first";
            Control<ComboBox>(window, "KeywordColorBox").SelectedIndex = 3;
            Invoke(window, "AddKeyword_Click", window, new RoutedEventArgs());
            await (Task)Invoke(window, "RefreshKeywordsAsync")!;
            Control<TextBox>(window, "KeywordBox").Text = "pending B highlight";
            Control<TextBox>(window, "IncludeBox").Text = "pending B include";
            Control<TextBox>(window, "ExcludeBox").Text = "pending B exclude";
            Control<ComboBox>(window, "IncludeModeBox").SelectedIndex = 0;
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "first";
            Control<CheckBox>(window, "SearchCaseBox").IsChecked = true;
            Control<CheckBox>(window, "SearchWordBox").IsChecked = true;
            editor.Select(0, editor.Document.TextLength);
            Invoke(window, "CaptureSearchSelection_Click", window, new RoutedEventArgs());
            await (Task)Invoke(window, "SearchAsync")!;
            var bRanges = Field<IReadOnlyList<SourceTextRange>>(window, "fixedSearchRanges").ToArray();
            var bView = Field<LogProjection>(window, "projection");
            var bSource = Field<LogData>(window, "data");
            var bTime = Field<TimeAnchor>(window, "timeA");
            editor.Select(bView.DisplayOffsets[1] + 2, 5);
            int bStart = editor.SelectionStart, bLength = editor.SelectionLength;
            int? bLine = CurrentLine(window);

            // A keeps a different filter, both time points, and an active all-thread context.
            tabs.SelectedIndex = 0;
            await SetThreads(window, t => t.Id == 1);
            await Filter(window, new EntryFilter(["keep"], [], true));
            Invoke(window, "NavigateVisible", 1, 4, 0);
            Invoke(window, "SetTimePoint", true);
            Invoke(window, "NavigateVisible", 4, 3, 0);
            Invoke(window, "SetTimePoint", false);
            int? normalLine = CurrentLine(window);
            int normalColumn = editor.TextArea.Caret.Column;
            Control<TextBox>(window, "ContextRadiusBox").Text = "1";
            Assert.True(await (Task<bool>)Invoke(window, "ShowContextAsync", 2)!);
            Control<TextBox>(window, "KeywordBox").Text = "synthetic A";
            Control<ComboBox>(window, "KeywordColorBox").SelectedIndex = 2;
            Invoke(window, "AddKeyword_Click", window, new RoutedEventArgs());
            object aRule = Field<IEnumerable>(window, "keywordRules").Cast<object>().Single();
            aRule.GetType().GetProperty("Enabled")!.SetValue(aRule, false);
            Control<TextBox>(window, "KeywordBox").Text = "pending A highlight";
            Control<TextBox>(window, "IncludeBox").Text = "pending A include";
            Control<TextBox>(window, "ExcludeBox").Text = "pending A exclude";
            Control<CheckBox>(window, "FilterCaseBox").IsChecked = true;
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "synthetic A (first|last)";
            Control<CheckBox>(window, "SearchCaseBox").IsChecked = false;
            Control<CheckBox>(window, "SearchWordBox").IsChecked = false;
            Control<CheckBox>(window, "SearchRegexBox").IsChecked = true;
            Control<ComboBox>(window, "SearchScopeBox").SelectedIndex = 1;
            await (Task)Invoke(window, "SearchAsync")!;
            var aSource = Field<LogData>(window, "data");
            var aView = Field<LogProjection>(window, "projection");
            var aTimeA = Field<TimeAnchor>(window, "timeA");
            var aTimeB = Field<TimeAnchor>(window, "timeB");
            editor.Select(aView.DisplayOffsets[2] + 4, 8);
            int aStart = editor.SelectionStart, aLength = editor.SelectionLength;
            int? aLine = CurrentLine(window);
            string aContext = Control<TextBlock>(window, "ContextStatus").Text;

            tabs.SelectedIndex = 1;
            await (Task)Invoke(window, "SearchAsync")!;
            await (Task)Invoke(window, "RefreshKeywordsAsync")!;
            Assert.Same(bSource, Field<LogData>(window, "data"));
            Assert.Same(bView, Field<LogProjection>(window, "projection"));
            Assert.Equal(new[] { 0, 1 }, bView.SourceIndexes);
            Assert.Equal(bLine, CurrentLine(window));
            Assert.Equal(bStart, editor.SelectionStart); Assert.Equal(bLength, editor.SelectionLength);
            Assert.Same(bTime, Field<TimeAnchor>(window, "timeA"));
            Assert.Null(Field<object?>(window, "timeB"));
            Assert.Equal("first", Assert.Single(Field<EntryFilter>(window, "appliedFilter").Includes));
            Assert.Equal("pending B include", Control<TextBox>(window, "IncludeBox").Text);
            Assert.Equal("pending B exclude", Control<TextBox>(window, "ExcludeBox").Text);
            Assert.Equal(0, Control<ComboBox>(window, "IncludeModeBox").SelectedIndex);
            Assert.False(Field<bool>(window, "contextActive"));
            Assert.Equal("first", Control<TextBox>(window, "SearchBox").Text);
            Assert.Equal(2, Control<ComboBox>(window, "SearchScopeBox").SelectedIndex);
            Assert.True(Control<CheckBox>(window, "SearchCaseBox").IsChecked);
            Assert.True(Control<CheckBox>(window, "SearchWordBox").IsChecked);
            Assert.False(Control<CheckBox>(window, "SearchRegexBox").IsChecked);
            Assert.Equal(bRanges, Field<IReadOnlyList<SourceTextRange>>(window, "fixedSearchRanges"));
            AssertRule(window, "first", 3, true);
            Assert.Equal("pending B highlight", Control<TextBox>(window, "KeywordBox").Text);

            tabs.SelectedIndex = 0;
            await (Task)Invoke(window, "SearchAsync")!;
            Assert.Same(aSource, Field<LogData>(window, "data"));
            Assert.Same(aView, Field<LogProjection>(window, "projection"));
            Assert.Equal(aLine, CurrentLine(window));
            Assert.Equal(aStart, editor.SelectionStart); Assert.Equal(aLength, editor.SelectionLength);
            Assert.Same(aTimeA, Field<TimeAnchor>(window, "timeA"));
            Assert.Same(aTimeB, Field<TimeAnchor>(window, "timeB"));
            Assert.Equal("pending A include", Control<TextBox>(window, "IncludeBox").Text);
            Assert.Equal("pending A exclude", Control<TextBox>(window, "ExcludeBox").Text);
            Assert.True(Control<CheckBox>(window, "FilterCaseBox").IsChecked);
            Assert.True(Field<bool>(window, "contextActive"));
            Assert.Equal(aContext, Control<TextBlock>(window, "ContextStatus").Text);
            Assert.Equal(2, Field<OriginalLineMargin>(window, "margin").ContextLineIndex);
            Assert.Equal(1, Field<ThreadBackgroundRenderer>(window, "threadRenderer").ContextEntryIndex);
            Assert.Equal("synthetic A (first|last)", Control<TextBox>(window, "SearchBox").Text);
            Assert.Equal(1, Control<ComboBox>(window, "SearchScopeBox").SelectedIndex);
            Assert.True(Control<CheckBox>(window, "SearchRegexBox").IsChecked);
            Assert.Null(Field<object?>(window, "fixedSearchRanges"));
            AssertRule(window, "synthetic A", 2, false);
            Assert.Equal("pending A highlight", Control<TextBox>(window, "KeywordBox").Text);

            Invoke(window, "ReturnContext_Click", window, new RoutedEventArgs());
            await WaitFor(window, () => !Field<bool>(window, "busy"));
            Assert.False(Field<bool>(window, "contextActive"));
            Assert.Equal(new[] { 0, 1, 3, 4 }, Field<LogProjection>(window, "projection").SourceIndexes);
            Assert.Equal(normalLine, CurrentLine(window));
            Assert.Equal(normalColumn, editor.TextArea.Caret.Column);
            Assert.Equal("pending A include", Control<TextBox>(window, "IncludeBox").Text);
            Assert.Contains("변경 사항 미적용", Control<TextBlock>(window, "FilterDirtyStatus").Text);
            Assert.Equal(First, await File.ReadAllTextAsync(first));
            Assert.Equal(Second, await File.ReadAllTextAsync(second));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ReopeningEquivalentPathActivatesExistingTabWithoutReplacingItsState() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string first = await folder.Write("synthetic-A.log", First);
        string second = await folder.Write("synthetic-B.log", Second);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await OpenFiles(window, first, second);
            var tabs = Control<ListBox>(window, "SessionTabs");
            tabs.SelectedIndex = 0;
            await SetThreads(window, t => t.Id == 1);
            Invoke(window, "NavigateVisible", 4, 4, 0);
            var source = Field<LogData>(window, "data");
            var view = Field<LogProjection>(window, "projection");
            tabs.SelectedIndex = 1;
            await OpenFiles(window, Path.Combine(folder.Path, ".", "synthetic-A.log"), first.ToUpperInvariant());
            Assert.Equal(2, tabs.Items.Count);
            Assert.Equal(0, tabs.SelectedIndex);
            Assert.Same(source, Field<LogData>(window, "data"));
            Assert.Same(view, Field<LogProjection>(window, "projection"));
            Assert.Equal(4, CurrentLine(window));
            Assert.Equal(First, await File.ReadAllTextAsync(first));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task TabButtonsAndCycleHandlersCloseMiddleThenLastAndLeaveAnEmptyWorkspace() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string first = await folder.Write("synthetic-A.log", First);
        string second = await folder.Write("synthetic-B.log", Second);
        string third = await folder.Write("synthetic-C.log", Third);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await OpenFiles(window, first, second, third);
            var tabs = Control<ListBox>(window, "SessionTabs");
            Assert.Equal(2, tabs.SelectedIndex);
            Invoke(window, "NextSession_Click", window, new RoutedEventArgs());
            Assert.Equal(0, tabs.SelectedIndex);
            Invoke(window, "PreviousSession_Click", window, new RoutedEventArgs());
            Assert.Equal(2, tabs.SelectedIndex);
            Assert.Equal("Ctrl+Tab", Control<MenuItem>(window, "NextSessionMenu").InputGestureText);
            Assert.Equal("Ctrl+Shift+Tab", Control<MenuItem>(window, "PreviousSessionMenu").InputGestureText);

            tabs.SelectedIndex = 1;
            tabs.Measure(new Size(1040, 44)); tabs.Arrange(new Rect(0, 0, 1040, 44)); tabs.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var row = Assert.IsType<ListBoxItem>(tabs.ItemContainerGenerator.ContainerFromIndex(1));
            var close = FindVisual<Button>(row)!;
            Assert.NotNull(close);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, tabs.Items.Count);
            Assert.Equal(third, Field<LogData>(window, "data").SourcePath);
            Invoke(window, "CloseActiveSession_Click", window, new RoutedEventArgs());
            Assert.Single(tabs.Items.Cast<object>());
            Assert.Equal(first, Field<LogData>(window, "data").SourcePath);
            Invoke(window, "CloseActiveSession_Click", window, new RoutedEventArgs());
            Assert.Empty(tabs.Items.Cast<object>());
            Assert.Equal(Visibility.Collapsed, tabs.Visibility);
            Assert.Null(Field<object?>(window, "data"));
            Assert.Null(Field<object?>(window, "projection"));
            Assert.Equal("", Control<TextEditor>(window, "Editor").Text);
            Assert.False(Control<Button>(window, "ExportButton").IsEnabled);
            Assert.False(Control<MenuItem>(window, "CloseSessionMenu").IsEnabled);
            Assert.Equal(First, await File.ReadAllTextAsync(first));
            Assert.Equal(Second, await File.ReadAllTextAsync(second));
            Assert.Equal(Third, await File.ReadAllTextAsync(third));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task NewTabDoesNotInheritSearchHitsAndClosingLastTabClearsFindCommands() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string first = await folder.Write("synthetic-A.log", First);
        string second = await folder.Write("synthetic-B.log", Second);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await OpenFiles(window, first);
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "synthetic A";
            await (Task)Invoke(window, "SearchAsync")!;
            Invoke(window, "UpdateMenus");
            Assert.Equal(3, Field<LocatedSearchHit[]>(window, "searchHits").Length);
            Assert.True(Control<MenuItem>(window, "FindNextMenu").IsEnabled);
            Assert.True(Control<MenuItem>(window, "FindPreviousMenu").IsEnabled);
            Assert.True(Field<SearchHighlightRenderer>(window, "searchRenderer").Index.Count > 0);

            await OpenFiles(window, second);
            Assert.Equal("", Control<TextBox>(window, "SearchBox").Text);
            Assert.Equal(Visibility.Collapsed, Control<Border>(window, "SearchBar").Visibility);
            Assert.Empty(Field<LocatedSearchHit[]>(window, "searchHits"));
            Assert.Equal(0, Field<SearchHighlightRenderer>(window, "searchRenderer").Index.Count);
            Assert.False(Control<MenuItem>(window, "FindNextMenu").IsEnabled);
            Assert.False(Control<MenuItem>(window, "FindPreviousMenu").IsEnabled);

            Invoke(window, "CloseActiveSession_Click", window, new RoutedEventArgs());
            await (Task)Invoke(window, "SearchAsync")!;
            Invoke(window, "UpdateMenus");
            Assert.Equal(3, Field<LocatedSearchHit[]>(window, "searchHits").Length);
            Assert.True(Control<MenuItem>(window, "FindNextMenu").IsEnabled);
            Invoke(window, "CloseActiveSession_Click", window, new RoutedEventArgs());
            Assert.Empty(Field<LocatedSearchHit[]>(window, "searchHits"));
            Assert.Equal(0, Field<SearchHighlightRenderer>(window, "searchRenderer").Index.Count);
            Assert.False(Control<MenuItem>(window, "FindNextMenu").IsEnabled);
            Assert.False(Control<MenuItem>(window, "FindPreviousMenu").IsEnabled);
            Assert.Null(Control<ListBox>(window, "ResultsList").ItemsSource);
            Assert.Equal(Visibility.Visible, Control<StackPanel>(window, "EmptyPanel").Visibility);
            Assert.Equal(Visibility.Collapsed, Control<Border>(window, "ContextPanel").Visibility);
            Assert.False(window.CanChangeFilters);
            Assert.Equal("", Control<TextEditor>(window, "Editor").Text);
            Assert.Null(Field<object?>(window, "data"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CancelledLoadKeepsPreviousTabAndLateLoadCannotPublishAfterTabSelection() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string first = await folder.Write("synthetic-A.log", First);
        string second = await folder.Write("synthetic-B.log", Second);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await OpenFiles(window, first, second);
            var tabs = Control<ListBox>(window, "SessionTabs");
            var previous = Field<LogData>(window, "data");
            var previousView = Field<LogProjection>(window, "projection");
            Invoke(window, "NavigateVisible", 1, 3, 0);
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> cancel = (_, _) =>
                Task.FromCanceled<LogData>(new CancellationToken(true));
            await Load(window, cancel);
            Assert.Equal(2, tabs.Items.Count); Assert.Equal(1, tabs.SelectedIndex);
            Assert.Same(previous, Field<LogData>(window, "data"));
            Assert.Same(previousView, Field<LogProjection>(window, "projection"));
            Assert.Equal(1, CurrentLine(window));
            Assert.Equal(3, Control<TextEditor>(window, "Editor").TextArea.Caret.Column);

            var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<LogData>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> late = async (token, progress) =>
            { started.SetResult(token); var result = await release.Task; progress.Report(new("synthetic late progress", 100)); return result; };
            Task loading = Load(window, late);
            var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(Field<bool>(window, "busy"));
            tabs.SelectedIndex = 0;
            var selected = Field<LogData>(window, "data");
            Assert.True(token.IsCancellationRequested);
            release.SetResult(LogParser.ParsePastedText("[000:00:30] [T99] synthetic late result"));
            await loading;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert.Equal(2, tabs.Items.Count); Assert.Equal(0, tabs.SelectedIndex);
            Assert.Same(selected, Field<LogData>(window, "data"));
            Assert.Equal(first, selected.SourcePath);
            Assert.DoesNotContain("synthetic late", Control<TextEditor>(window, "Editor").Text);
            Assert.DoesNotContain("synthetic late progress", Control<TextBlock>(window, "OperationStatus").Text);
            Assert.False(Field<bool>(window, "busy"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task UnsupportedEncodingAndBothInvalidBytesKeepCurrentTabAndSourceIntact() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string first = await folder.Write("synthetic-A.log", First);
        string second = await folder.Write("synthetic-B.log", Second);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await OpenFiles(window, first, second);
            var tabs = Control<ListBox>(window, "SessionTabs");
            var source = Field<LogData>(window, "data");
            var view = Field<LogProjection>(window, "projection");
            var active = tabs.SelectedItem;
            Invoke(window, "NavigateVisible", 1, 3, 0);
            byte[][] failures = [
                [0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00], // Unsupported UTF-32 LE.
                [0x81], // Invalid in strict UTF-8 and strict CP949.
                [0xEF, 0xBB, 0xBF, 0x80] // Authoritative UTF-8 BOM with malformed payload.
            ];
            for (int i = 0; i < failures.Length; i++)
            {
                string invalid = Path.Combine(folder.Path, $"synthetic-invalid-{i}.log");
                await File.WriteAllBytesAsync(invalid, failures[i]);
                await OpenFiles(window, invalid);
                Assert.Equal(2, tabs.Items.Count);
                Assert.Same(active, tabs.SelectedItem);
                Assert.Same(source, Field<LogData>(window, "data"));
                Assert.Same(view, Field<LogProjection>(window, "projection"));
                Assert.Equal(second, Field<string>(window, "requestedPath"));
                Assert.Equal(1, CurrentLine(window));
                Assert.Contains("이전 화면 유지", Control<TextBlock>(window, "OperationStatus").Text);
                Assert.Equal(failures[i], await File.ReadAllBytesAsync(invalid));
            }
            Assert.Equal(First, await File.ReadAllTextAsync(first));
            Assert.Equal(Second, await File.ReadAllTextAsync(second));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CancelStopsRemainingFilesInAnOpenBatch() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string first = await folder.Write("synthetic-A.log", First);
        string second = await folder.Write("synthetic-B.log", Second);
        string third = await folder.Write("synthetic-C.log", Third);
        var window = new MainWindow(folder.Path, false);
        try
        {
            var tabs = Control<ListBox>(window, "SessionTabs");
            bool cancelled = false;
            tabs.SelectionChanged += (_, e) =>
            {
                if (cancelled || e.AddedItems.Count == 0) return;
                // Stop the batch at the first successful publication, before its next iteration.
                cancelled = true;
                Invoke(window, "Cancel_Click", window, new RoutedEventArgs());
            };
            await OpenFiles(window, first, second, third);
            Assert.True(cancelled);
            Assert.Single(tabs.Items.Cast<object>());
            Assert.Equal(first, Field<LogData>(window, "data").SourcePath);
            Assert.Equal(First, Control<TextEditor>(window, "Editor").Text);
            Assert.False(Field<bool>(window, "busy"));
            Assert.Equal(First, await File.ReadAllTextAsync(first));
            Assert.Equal(Second, await File.ReadAllTextAsync(second));
            Assert.Equal(Third, await File.ReadAllTextAsync(third));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SwitchingTabsCancelsPendingFilterAndRestoresItsLastCommittedThreadChoice() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string first = await folder.Write("synthetic-A.log", First);
        string second = await folder.Write("synthetic-B.log", Second);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await OpenFiles(window, first, second);
            await SetThreads(window, t => t.Id == 7);
            var committed = Field<LogProjection>(window, "projection");
            Task pending = SetThreads(window, t => t.Id == 8);
            Assert.True(Field<bool>(window, "busy"));
            Control<ListBox>(window, "SessionTabs").SelectedIndex = 0;
            await pending;
            Assert.Equal(first, Field<LogData>(window, "data").SourcePath);
            Control<ListBox>(window, "SessionTabs").SelectedIndex = 1;
            Assert.Same(committed, Field<LogProjection>(window, "projection"));
            Assert.Equal(new int?[] { 7 }, Field<List<ThreadItem>>(window, "threadItems").Where(x => x.IsSelected).Select(x => x.Id));
            Assert.Equal(new[] { 0, 1, 3, 4 }, committed.SourceIndexes);
            Assert.False(Field<bool>(window, "busy"));
        }
        finally { window.Close(); }
    });

    private static Task OpenFiles(MainWindow window, params string[] paths) => (Task)Invoke(window, "OpenFilesAsync", (object)paths)!;
    private static Task Load(MainWindow window, Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> loader) =>
        (Task)Invoke(window, "LoadAsync", loader, "synthetic session load", "synthetic session failure")!;
    private static Task SetThreads(MainWindow window, Func<ThreadItem, bool> predicate) => (Task)Invoke(window, "SetThreadsAsync", predicate)!;
    private static Task Filter(MainWindow window, EntryFilter filter) => (Task)Invoke(window, "FilterAsync", filter, null)!;
    private static void AssertRule(MainWindow window, string keyword, int color, bool enabled)
    {
        object rule = Assert.Single(Field<IEnumerable>(window, "keywordRules").Cast<object>());
        Assert.Equal(keyword, rule.GetType().GetProperty("Keyword")!.GetValue(rule));
        Assert.Equal(color, rule.GetType().GetProperty("ColorIndex")!.GetValue(rule));
        Assert.Equal(enabled, rule.GetType().GetProperty("Enabled")!.GetValue(rule));
    }
    private static int? CurrentLine(MainWindow window) => (int?)Invoke(window, "CurrentSourceLine");
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindVisual<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private static async Task WaitFor(MainWindow window, Func<bool> predicate)
    {
        for (int i = 0; i < 500; i++)
        {
            if (predicate()) { await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); return; }
            await Task.Delay(10);
        }
        Assert.Fail("Synthetic session operation did not complete within 5 seconds");
    }
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-session-tabs", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public async Task<string> Write(string name, string text)
        { string path = System.IO.Path.Combine(Path, name); await File.WriteAllTextAsync(path, text, new UTF8Encoding(false, true)); return path; }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
