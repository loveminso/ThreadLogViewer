using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class FileRefreshTests
{
    private const string Original = "[000:00:01] [T1] synthetic keep first\r\nbody A\r\n[000:00:02] [T2] synthetic keep second\nbody B\n[000:00:03] [T1] synthetic keep last\nbody C";

    [Fact]
    public void RemappingUsesTextAndOwningHeaderAndRejectsAmbiguousDuplicates()
    {
        var original = LogParser.ParsePastedText("[000:00:01] [T1] same\nbody\n[000:00:01] [T1] same\nbody\n[000:00:02] [T2] unique\nunique body");
        var updated = LogParser.ParsePastedText("[000:00:00] [T9] inserted\n" + original.Text);
        var map = SourceLineRemap.Create(original, updated);
        for (int index = 0; index < 4; index++) Assert.Null(map.Map(index));
        Assert.Equal(5, map.Map(4)); Assert.Equal(6, map.Map(5));
        Assert.Null(map.MapRange(new(original.GetLineOffset(1), 4)));
        var changedOwner = LogParser.ParsePastedText("[000:00:99] [T1] changed\nbody\n[000:00:02] [T2] unique\nunique body");
        Assert.Null(SourceLineRemap.Create(original, changedOwner).Map(1));
    }

    [Fact]
    public void ExactAndAppendOnlySnapshotsKeepDuplicatePositionsAndRangesWithOriginalEndings()
    {
        var source = LogParser.ParsePastedText("[000:00:01] [T1] repeated\r\nbody\n[000:00:01] [T1] repeated\r\nbody");
        var same = SourceLineRemap.Create(source, LogParser.ParsePastedText(source.Text));
        var appended = SourceLineRemap.Create(source, LogParser.ParsePastedText(source.Text + "\n[000:00:02] [T2] added\n"));
        for (int index = 0; index < source.Lines.Count; index++)
        { Assert.Equal(index, same.Map(index)); Assert.Equal(index, appended.Map(index)); }
        var range = new SourceTextRange(0, source.Text.Length);
        Assert.Equal(range, appended.MapRange(range));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => SourceLineRemap.Create(source, source, cancel.Token));
    }

    [Fact]
    public void NearestSafePositionUsesForwardTieWithoutMappingDeletedAnchor()
    {
        var old = LogParser.ParsePastedText("[000:00:01] [T1] before\n[000:00:02] [T1] deleted\n[000:00:03] [T1] after");
        var next = LogParser.ParsePastedText("[000:00:01] [T1] before\n[000:00:99] [T1] replacement\n[000:00:03] [T1] after");
        var map = SourceLineRemap.Create(old, next);
        Assert.Null(map.Map(1)); Assert.Equal(2, map.FindNearestMappedLine(1));
    }

    [Fact]
    public Task RefreshPreservesAnalysisAndMapsInsertedLinesWhileClosedSharedSnapshotStaysUnchanged() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string path = folder.Write("synthetic-refresh.log", Original);
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await Open(window, path));
            var oldSource = Field<LogData>(window, "data");
            Assert.True(await (Task<bool>)Invoke(window, "OpenLineSessionAsync", 0, 1)!);
            var split = Control<ListBox>(window, "SessionTabs").SelectedItem;
            Invoke(window, "CloseActiveSession_Click", window, new RoutedEventArgs());
            Assert.Same(oldSource, Field<LogData>(window, "data"));
            await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(thread => thread.Id == 1))!;
            await (Task)Invoke(window, "FilterAsync", new EntryFilter(["keep"], ["absent"]), null)!;
            Invoke(window, "NavigateVisible", 0, 4, 0); Invoke(window, "SetTimePoint", true);
            Invoke(window, "NavigateVisible", 5, 3, 0); Invoke(window, "SetTimePoint", false);
            Control<TextBox>(window, "KeywordBox").Text = "keep";
            Invoke(window, "AddKeyword_Click", window, new RoutedEventArgs());
            Control<TextBox>(window, "KeywordBox").Text = "pending highlight";
            Control<TextBox>(window, "IncludeBox").Text = "pending include";
            Control<TextBox>(window, "ExcludeBox").Text = "pending exclude";
            Control<TextBox>(window, "ThreadSearchBox").Text = "1";
            Set(window, "fixedSearchRanges", new SourceTextRange[] { new(oldSource.GetLineOffset(1), 6) });
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "body A";
            Control<ComboBox>(window, "SearchScopeBox").SelectedIndex = 2;
            await (Task)Invoke(window, "SearchAsync")!;
            var editor = Control<TextEditor>(window, "Editor");
            var oldView = Field<LogProjection>(window, "projection");
            editor.Select(oldView.GetDisplayOffset(oldSource.GetLineOffset(5))!.Value, 6);
            const string prefix = "[000:00:00] [T9] synthetic keep inserted\r\ninserted body\r\n";
            string replacement = prefix + Original;
            File.WriteAllText(path, replacement, new UTF8Encoding(false, true));
            byte[] expectedBytes = File.ReadAllBytes(path);
            await Refresh(window);
            await (Task)Invoke(window, "SearchAsync")!;
            await (Task)Invoke(window, "RefreshKeywordsAsync")!;
            var source = Field<LogData>(window, "data");
            Assert.NotSame(oldSource, source); Assert.Equal(replacement, source.Text);
            Assert.Equal(new[] { 2, 3, 6, 7 }, Field<LogProjection>(window, "projection").SourceIndexes);
            Assert.Equal(2, Field<TimeAnchor>(window, "timeA").SourceLineIndex);
            Assert.Equal(7, Field<TimeAnchor>(window, "timeB").SourceLineIndex);
            Assert.Equal("body C", editor.SelectedText);
            Assert.Equal("pending include", Control<TextBox>(window, "IncludeBox").Text);
            Assert.Equal("pending exclude", Control<TextBox>(window, "ExcludeBox").Text);
            Assert.Equal("pending highlight", Control<TextBox>(window, "KeywordBox").Text);
            Assert.Single(Field<IEnumerable>(window, "keywordRules").Cast<object>());
            Assert.Equal("1", Control<TextBox>(window, "ThreadSearchBox").Text);
            Assert.Equal("body A", Control<TextBox>(window, "SearchBox").Text);
            Assert.Equal(3, Assert.Single(Field<LocatedSearchHit[]>(window, "searchHits")).SourceLineIndex);
            Assert.Equal(new SourceTextRange(source.GetLineOffset(3), 6), Assert.Single(Field<IReadOnlyList<SourceTextRange>>(window, "fixedSearchRanges")));
            Assert.Equal(expectedBytes, File.ReadAllBytes(path));
            Invoke(window, "RestoreClosedSession_Click", window, new RoutedEventArgs());
            Assert.Same(split, Control<ListBox>(window, "SessionTabs").SelectedItem);
            Assert.Same(oldSource, Field<LogData>(window, "data")); Assert.Equal(Original, oldSource.Text);
            Assert.Equal(expectedBytes, File.ReadAllBytes(path));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task NewThreadsFollowWhetherAllOldThreadsWereSelected(bool allSelected) => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string path = folder.Write("synthetic-threads.log", "[000:00:01] [T1] first\n[000:00:02] [T2] second\n");
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await Open(window, path));
            if (!allSelected) await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(thread => thread.Id == 1))!;
            File.AppendAllText(path, "[000:00:03] [T3] appended\n", new UTF8Encoding(false, true));
            await Refresh(window);
            var threads = Field<List<ThreadItem>>(window, "threadItems");
            Assert.Equal(allSelected, threads.Single(thread => thread.Id == 3).IsSelected);
            Assert.Equal(allSelected ? 3 : 1, Field<LogProjection>(window, "projection").EntryCount);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ExplicitCp949EncodingRemainsExplicitDuringRefresh() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string path = folder.Write("synthetic-encoding.log", "[000:00:01] [T1] é\n");
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await Open(window, path, EncodingMode.Cp949));
            string expected = Field<LogData>(window, "data").Text;
            Assert.DoesNotContain("é", expected);
            File.AppendAllText(path, "[000:00:02] [T1] é\n", new UTF8Encoding(false, true));
            byte[] bytes = File.ReadAllBytes(path);
            await Refresh(window);
            Assert.Contains("사용자 지정", Field<LogData>(window, "data").EncodingDescription);
            Assert.StartsWith(expected, Field<LogData>(window, "data").Text);
            Assert.DoesNotContain("é", Field<LogData>(window, "data").Text);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task AmbiguousAnchorsAreRemovedAndCursorMovesToAnExplicitlyReportedSafeNeighbor() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        const string oldText = "[000:00:01] [T1] same\nbody\n[000:00:01] [T1] same\nbody\n[000:00:02] [T2] unique\nunique body";
        string path = folder.Write("synthetic-ambiguity.log", oldText);
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await Open(window, path));
            Invoke(window, "NavigateVisible", 1, 2, 0);
            Invoke(window, "SetTimePoint", true);
            Control<TextBox>(window, "ContextRadiusBox").Text = "20";
            Assert.True(await (Task<bool>)Invoke(window, "ShowContextAsync", 1)!);
            Invoke(window, "NavigateVisible", 1, 2, 0);
            Set(window, "separationStartLine", 1);
            Set(window, "fixedSearchRanges", new SourceTextRange[] { new(Field<LogData>(window, "data").GetLineOffset(1), 4) });
            File.WriteAllText(path, "[000:00:00] [T9] inserted\n" + oldText, new UTF8Encoding(false, true));
            await Refresh(window);
            Assert.Null(Field<object?>(window, "timeA")); Assert.Null(Field<object?>(window, "separationStartLine"));
            Assert.False(Field<bool>(window, "contextActive"));
            Assert.Empty(Field<IReadOnlyList<SourceTextRange>>(window, "fixedSearchRanges"));
            Assert.Equal(5, (int?)Invoke(window, "CurrentSourceLine"));
            Assert.Contains("가까운 확인된", Control<TextBlock>(window, "OperationStatus").Text);
            Assert.Contains("정확히 복원하지", Control<TextBlock>(window, "OperationStatus").Text);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CancelOrTabSwitchDuringRefreshKeepsCommittedSnapshotsAndRejectsLateResults(bool switchTab) => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string path = folder.Write("synthetic-delayed.log", Original);
        var window = new MainWindow(folder.Path, false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pending = null;
        try
        {
            Assert.True(await Open(window, path));
            var original = Field<LogData>(window, "data");
            var document = Control<TextEditor>(window, "Editor").Document;
            Invoke(window, "NavigateVisible", 1, 3, 0);
            var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> delayed = async (token, _) =>
            { started.SetResult(token); await release.Task; return LogParser.Parse("[000:00:99] [T9] obsolete refresh", path); };
            pending = (Task)Invoke(window, "RefreshSessionAsync", delayed)!;
            var token = await started.Task;
            if (switchTab) Invoke(window, "CreateBlankSession"); else Invoke(window, "Cancel_Click", window, new RoutedEventArgs());
            release.SetResult(); await pending;
            Assert.True(token.IsCancellationRequested);
            if (switchTab) Control<ListBox>(window, "SessionTabs").SelectedIndex = 0;
            Assert.Same(original, Field<LogData>(window, "data"));
            Assert.Same(document, Control<TextEditor>(window, "Editor").Document);
            Assert.Equal(1, (int?)Invoke(window, "CurrentSourceLine"));
            Assert.Equal(3, Control<TextEditor>(window, "Editor").TextArea.Caret.Column);
            Assert.Equal(Original, File.ReadAllText(path));
        }
        finally { release.TrySetResult(); if (pending is not null) await pending; window.Close(); }
    });

    [Fact]
    public Task ReadFailureKeepsSnapshotAndRefreshIsUnavailableForPastedAndScopedTabs() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string path = folder.Write("synthetic-locked.log", Original);
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await Open(window, path));
            var source = Field<LogData>(window, "data");
            var document = Control<TextEditor>(window, "Editor").Document;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) await Refresh(window);
            Assert.Same(source, Field<LogData>(window, "data")); Assert.Same(document, Control<TextEditor>(window, "Editor").Document);
            Assert.Equal(Original, File.ReadAllText(path));
            Assert.True(await (Task<bool>)Invoke(window, "OpenLineSessionAsync", 0, 1)!);
            Assert.False(PrivateProperty<bool>(window, "CanRefreshCurrentSession"));
            await Refresh(window); Assert.Same(source, Field<LogData>(window, "data"));
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> pasted = (token, progress) => Task.FromResult(LogParser.ParsePastedText(Original, token, progress));
            await (Task)Invoke(window, "LoadAsync", pasted, "synthetic pasted", "synthetic failure")!;
            Assert.False(PrivateProperty<bool>(window, "CanRefreshCurrentSession"));
            var pastedSource = Field<LogData>(window, "data"); await Refresh(window); Assert.Same(pastedSource, Field<LogData>(window, "data"));
        }
        finally { window.Close(); }
    });

    private static Task Refresh(MainWindow window) => (Task)Invoke(window, "RefreshCurrentSessionAsync")!;
    private static Task<bool> Open(MainWindow window, string path, EncodingMode mode = EncodingMode.Auto) => (Task<bool>)Invoke(window, "OpenAsync", path, mode)!;
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    private static T PrivateProperty<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-file-refresh", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public string Write(string name, string text)
        { string path = System.IO.Path.Combine(Path, name); File.WriteAllText(path, text, new UTF8Encoding(false, true)); return path; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
