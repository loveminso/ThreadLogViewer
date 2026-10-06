using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class SessionRecoveryTests
{
    [Fact]
    public Task ClosedPastedTabRestoresItsExactSnapshotAndAnalysisState() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            string text = string.Join('\n', Enumerable.Range(0, 60).Select(index =>
                $"[000:00:{index:00}] [T{index % 2 + 1}] synthetic keep target {index}"));
            await Load(window, text);
            await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(item => item.Id == 1))!;
            await (Task)Invoke(window, "FilterAsync", new EntryFilter(["keep"], []), null)!;
            Control<TextBox>(window, "ContextRadiusBox").Text = "20";
            Assert.True(await (Task<bool>)Invoke(window, "ShowContextAsync", 24)!);
            Invoke(window, "NavigateVisible", 24, 5, 0);
            Invoke(window, "BookmarkToggle_Click", window, new RoutedEventArgs());
            Field<BookmarkState>(window, "bookmarks").SetLabel(24, "synthetic checkpoint");
            Invoke(window, "SetTimePoint", true);
            Invoke(window, "NavigateVisible", 26, 3, 0);
            Invoke(window, "SetTimePoint", false);
            Control<TextBox>(window, "KeywordBox").Text = "target";
            Invoke(window, "AddKeyword_Click", window, new RoutedEventArgs());
            Control<TextBox>(window, "KeywordBox").Text = "unfinished highlight";
            Control<TextBox>(window, "IncludeBox").Text = "unfinished include";
            Control<TextBox>(window, "ExcludeBox").Text = "unfinished exclude";
            Control<TextBox>(window, "ThreadSearchBox").Text = "1";
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "target";
            Control<ComboBox>(window, "SearchScopeBox").SelectedIndex = 1;
            await (Task)Invoke(window, "SearchAsync")!;
            await (Task)Invoke(window, "RefreshKeywordsAsync")!;
            var editor = Control<TextEditor>(window, "Editor");
            Layout(editor); editor.ScrollToVerticalOffset(80);
            var view = Field<LogProjection>(window, "projection");
            editor.Select(view.GetDisplayOffset(Field<LogData>(window, "data").GetLineOffset(26))!.Value + 3, 8);
            var source = Field<LogData>(window, "data");
            var document = editor.Document;
            var timeA = Field<TimeAnchor>(window, "timeA");
            var timeB = Field<TimeAnchor>(window, "timeB");
            int start = editor.SelectionStart, length = editor.SelectionLength, caret = editor.TextArea.Caret.Offset;
            double vertical = editor.VerticalOffset;
            string context = Control<TextBlock>(window, "ContextStatus").Text;
            Invoke(window, "CloseActiveSession_Click", window, new RoutedEventArgs());
            Assert.Empty(Control<ListBox>(window, "SessionTabs").Items.Cast<object>());
            Assert.True(PrivateProperty<bool>(window, "CanRestoreClosedSession"));
            Invoke(window, "RestoreClosedSession_Click", window, new RoutedEventArgs());
            await (Task)Invoke(window, "SearchAsync")!;
            await (Task)Invoke(window, "RefreshKeywordsAsync")!;
            Layout(editor);
            Assert.Same(source, Field<LogData>(window, "data"));
            Assert.Same(view, Field<LogProjection>(window, "projection"));
            Assert.Same(document, editor.Document);
            Assert.Equal(text, source.Text);
            Assert.Equal(start, editor.SelectionStart); Assert.Equal(length, editor.SelectionLength);
            Assert.Equal(caret, editor.TextArea.Caret.Offset);
            Assert.True(editor.VerticalOffset >= vertical - 1 && editor.VerticalOffset <= vertical + 1,
                $"Expected vertical offset {vertical:F2} ± 1, actual {editor.VerticalOffset:F2}; " +
                $"editor={editor.ActualWidth:F2}×{editor.ActualHeight:F2}, viewport={editor.ViewportHeight:F2}, " +
                $"extent={editor.ExtentHeight:F2}, caret={editor.TextArea.Caret.Offset}, " +
                $"selection={editor.SelectionStart}+{editor.SelectionLength}, " +
                $"visualLinesValid={editor.TextArea.TextView.VisualLinesValid}");
            Assert.Equal("synthetic checkpoint", Assert.Single(Field<BookmarkState>(window, "bookmarks").Items).Label);
            Assert.Same(timeA, Field<TimeAnchor>(window, "timeA")); Assert.Same(timeB, Field<TimeAnchor>(window, "timeB"));
            Assert.Equal("keep", Assert.Single(Field<EntryFilter>(window, "appliedFilter").Includes));
            Assert.Equal("unfinished include", Control<TextBox>(window, "IncludeBox").Text);
            Assert.Equal("unfinished exclude", Control<TextBox>(window, "ExcludeBox").Text);
            Assert.Equal("unfinished highlight", Control<TextBox>(window, "KeywordBox").Text);
            Assert.Single(Field<IEnumerable>(window, "keywordRules").Cast<object>());
            Assert.Equal("1", Control<TextBox>(window, "ThreadSearchBox").Text);
            Assert.True(Field<bool>(window, "contextActive")); Assert.Equal(context, Control<TextBlock>(window, "ContextStatus").Text);
            Assert.Equal("target", Control<TextBox>(window, "SearchBox").Text);
            Assert.Equal(60, Field<LocatedSearchHit[]>(window, "searchHits").Length);
            Assert.False(PrivateProperty<bool>(window, "CanRestoreClosedSession"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task RecoveryIsNewestFirstAndRestoresOriginalTabIndexesIncludingBlankTabs() => InSta(() =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            for (int index = 0; index < 3; index++) Invoke(window, "CreateBlankSession");
            var tabs = Control<ListBox>(window, "SessionTabs");
            object a = tabs.Items[0], b = tabs.Items[1], c = tabs.Items[2];
            Invoke(window, "CloseSession", b); Invoke(window, "CloseSession", c);
            Invoke(window, "RestoreClosedSession_Click", window, new RoutedEventArgs());
            Assert.Equal(new[] { a, c }, tabs.Items.Cast<object>()); Assert.Same(c, tabs.SelectedItem);
            Invoke(window, "RestoreClosedSession_Click", window, new RoutedEventArgs());
            Assert.Equal(new[] { a, b, c }, tabs.Items.Cast<object>()); Assert.Same(b, tabs.SelectedItem);
            Assert.True(PrivateProperty<bool>(window, "IsBlankSession"));
            Assert.Equal("", Control<TextEditor>(window, "Editor").Text);
            Assert.False(PrivateProperty<bool>(window, "CanRestoreClosedSession"));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RecentRecoveryKeepsOnlyEightTabs() => InSta(() =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            for (int index = 0; index < 10; index++) { Invoke(window, "CreateBlankSession"); Invoke(window, "CloseActiveSession_Click", window, new RoutedEventArgs()); }
            for (int index = 10; index >= 3; index--)
            {
                Assert.True(PrivateProperty<bool>(window, "CanRestoreClosedSession"));
                Invoke(window, "RestoreClosedSession_Click", window, new RoutedEventArgs());
                Assert.Equal("새 탭 " + index, Control<TextBlock>(window, "FileLabel").Text);
            }
            Assert.False(PrivateProperty<bool>(window, "CanRestoreClosedSession"));
            Assert.Equal(8, Control<ListBox>(window, "SessionTabs").Items.Count);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void RetentionBudgetCountsSharedSourcesOnceAndExcludesOpenOwnership()
    {
        object source = new();
        var a = new RetentionSample(source, 90, new(), 10);
        var b = new RetentionSample(source, 90, new(), 10);
        var cache = new ClosedSessionRetention<RetentionSample>(Resources, maximumBytes: 100);
        Assert.True(cache.Add(a, 0, [b], out _)); Assert.Equal(10, cache.EstimatedBytes);
        Assert.True(cache.Add(b, 1, [], out int evicted)); Assert.Equal(1, evicted);
        Assert.Equal(100, cache.EstimatedBytes); Assert.True(cache.TryPop(out var restored, out int index));
        Assert.Same(b, restored); Assert.Equal(1, index);
        var shared = new ClosedSessionRetention<RetentionSample>(Resources, maximumBytes: 200);
        Assert.True(shared.Add(a, 0, [], out _)); Assert.True(shared.Add(b, 1, [], out _));
        Assert.Equal(110, shared.EstimatedBytes);
        Assert.False(cache.Add(new(new(), 101, new(), 1), 0, [], out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public Task RestoringClosedTabInvalidatesDelayedLoadPublication() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pending = null;
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic recovered");
            var recovered = Field<LogData>(window, "data");
            Invoke(window, "CloseActiveSession_Click", window, new RoutedEventArgs());
            await Load(window, "[000:00:02] [T2] synthetic current");
            var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> delayed = async (token, _) =>
            { started.SetResult(token); await release.Task; return LogParser.ParsePastedText("[000:00:03] [T3] synthetic obsolete"); };
            pending = (Task)Invoke(window, "LoadAsync", delayed, "synthetic pending", "synthetic failure")!;
            var token = await started.Task;
            Invoke(window, "RestoreClosedSession_Click", window, new RoutedEventArgs());
            Assert.True(token.IsCancellationRequested);
            release.SetResult(); await pending;
            Assert.Same(recovered, Field<LogData>(window, "data"));
            Assert.DoesNotContain("obsolete", Control<TextEditor>(window, "Editor").Text);
        }
        finally { release.TrySetResult(); if (pending is not null) await pending; window.Close(); }
    });

    private sealed record RetentionSample(object Source, long SourceBytes, object Document, long DocumentBytes);
    private static IEnumerable<RetainedResource> Resources(RetentionSample sample) =>
        [new(sample.Source, sample.SourceBytes), new(sample.Document, sample.DocumentBytes)];
    private static Task Load(MainWindow window, string text)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic recovery", "synthetic failure")!;
    }
    private static void Layout(TextEditor editor)
    {
        editor.Measure(new Size(800, 250)); editor.Arrange(new Rect(0, 0, 800, 250)); editor.UpdateLayout();
        editor.TextArea.TextView.EnsureVisualLines();
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static T PrivateProperty<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-session-recovery", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
