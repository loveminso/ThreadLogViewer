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

public sealed class ConcurrentRefreshTests
{
    private static readonly string Original = string.Join('\n', Enumerable.Range(0, 120).Select(index =>
        $"[000:{index / 60:00}:{index % 60:00}] [T1] synthetic keep target {index}"));
    private const string Prefix = "[000:00:00] [T9] synthetic inserted\n";

    [Fact]
    public Task SuccessfulRefreshKeepsSearchSelectionViewportRulesAndDraftsChangedDuringRead() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pending = null;
        try
        {
            string path = await Prepare(window, folder);
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = async (token, _) =>
            { started.SetResult(); await release.Task; return LogParser.Parse(Prefix + Original, path, cancellationToken: token); };
            pending = (Task)Invoke(window, "RefreshSessionAsync", load)!;
            await started.Task;
            object expectedPosition = ChangeInputs(window, "target", 75);
            release.SetResult(); await pending;
            await Settle(window);
            AssertLatestInputs(window, "target", 76);
            object actualPosition = Invoke(window, "CapturePosition")!;
            Assert.Equal(Property<int>(expectedPosition, "SourceLine") + 1, Property<int>(actualPosition, "SourceLine"));
            Assert.Equal(Property<int>(expectedPosition, "Column"), Property<int>(actualPosition, "Column"));
            Assert.Equal(Property<int>(expectedPosition, "TopSourceLine") + 1, Property<int>(actualPosition, "TopSourceLine"));
            Assert.InRange(Property<double>(actualPosition, "TopDelta"), Property<double>(expectedPosition, "TopDelta") - 1, Property<double>(expectedPosition, "TopDelta") + 1);
            Assert.Equal(new UTF8Encoding(false).GetBytes(Prefix + Original), File.ReadAllBytes(path));
        }
        finally { release.TrySetResult(); if (pending is not null) await pending; window.Close(); }
    });

    [Fact]
    public Task InputChangedDuringBackgroundMappingIsRebasedBeforePublication() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pending = null;
        try
        {
            string path = await Prepare(window, folder);
            int checkpoints = 0;
            Set(window, "refreshMapCheckpoint", (Func<CancellationToken, Task>)(async _ =>
            { if (Interlocked.Increment(ref checkpoints) == 1) started.SetResult(); await release.Task; }));
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, _) =>
                Task.FromResult(LogParser.Parse(Prefix + Original, path, cancellationToken: token));
            pending = (Task)Invoke(window, "RefreshSessionAsync", load)!;
            await started.Task;
            ChangeInputs(window, "target", 85);
            release.SetResult(); await pending;
            await Settle(window);
            AssertLatestInputs(window, "target", 86);
            Assert.True(checkpoints >= 2, "A state changed after capture must be mapped again before replacing its original snapshot.");
        }
        finally { release.TrySetResult(); if (pending is not null) await pending; window.Close(); }
    });

    [Fact]
    public Task CancelDuringBackgroundMappingKeepsTheOldSourceAndLatestInputs() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pending = null;
        try
        {
            string path = await Prepare(window, folder);
            var source = Field<LogData>(window, "data");
            var document = Control<TextEditor>(window, "Editor").Document;
            Set(window, "refreshMapCheckpoint", (Func<CancellationToken, Task>)(async token =>
            { started.SetResult(token); await release.Task; }));
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, _) =>
                Task.FromResult(LogParser.Parse(Prefix + Original, path, cancellationToken: token));
            pending = (Task)Invoke(window, "RefreshSessionAsync", load)!;
            var token = await started.Task;
            ChangeInputs(window, "target", 75);
            Invoke(window, "Cancel_Click", window, new RoutedEventArgs());
            release.SetResult(); await pending;
            await Settle(window);
            Assert.True(token.IsCancellationRequested);
            Assert.Same(source, Field<LogData>(window, "data")); Assert.Same(document, Control<TextEditor>(window, "Editor").Document);
            AssertLatestInputs(window, "target", 75);
        }
        finally { release.TrySetResult(); if (pending is not null) await pending; window.Close(); }
    });

    [Fact]
    public Task ReadFailureKeepsTheOldSourceAndInputsEditedWhileWaiting() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pending = null;
        try
        {
            await Prepare(window, folder);
            var source = Field<LogData>(window, "data");
            var document = Control<TextEditor>(window, "Editor").Document;
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = async (_, _) =>
            { started.SetResult(); await release.Task; throw new IOException("Synthetic read failure"); };
            pending = (Task)Invoke(window, "RefreshSessionAsync", load)!;
            await started.Task;
            ChangeInputs(window, "target", 75);
            release.SetResult(); await pending;
            await Settle(window);
            Assert.Same(source, Field<LogData>(window, "data")); Assert.Same(document, Control<TextEditor>(window, "Editor").Document);
            AssertLatestInputs(window, "target", 75);
        }
        finally { release.TrySetResult(); if (pending is not null) await pending; window.Close(); }
    });

    private static async Task<string> Prepare(MainWindow window, SyntheticFolder folder)
    {
        string path = folder.Write("synthetic-concurrent-refresh.log", Original);
        Assert.True(await (Task<bool>)Invoke(window, "OpenAsync", path, EncodingMode.Auto)!);
        Control<TextBox>(window, "KeywordBox").Text = "keep";
        Invoke(window, "AddKeyword_Click", window, new RoutedEventArgs());
        Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
        Control<TextBox>(window, "SearchBox").Text = "keep";
        await Settle(window);
        File.WriteAllText(path, Prefix + Original, new UTF8Encoding(false));
        return path;
    }

    private static object ChangeInputs(MainWindow window, string query, int selectedLine)
    {
        Invoke(window, "NavigateVisible", 80, 1, 0);
        Control<TextBox>(window, "SearchBox").Text = query;
        Control<CheckBox>(window, "SearchCaseBox").IsChecked = true;
        Control<CheckBox>(window, "SearchWordBox").IsChecked = true;
        Control<CheckBox>(window, "SearchRegexBox").IsChecked = true;
        Control<TextBox>(window, "ThreadSearchBox").Text = "T1";
        object rule = Assert.Single(Field<IEnumerable>(window, "keywordRules").Cast<object>());
        rule.GetType().GetProperty("Enabled")!.SetValue(rule, false);
        Control<TextBox>(window, "KeywordBox").Text = "latest unfinished highlight";
        Control<ComboBox>(window, "KeywordColorBox").SelectedIndex = 4;
        var editor = Control<TextEditor>(window, "Editor");
        var source = Field<LogData>(window, "data");
        int offset = source.GetLineOffset(selectedLine) + source.Lines[selectedLine].RawText.Span.IndexOf("target", StringComparison.Ordinal);
        editor.Select(offset, 6); editor.ScrollToVerticalOffset(400);
        editor.UpdateLayout(); editor.TextArea.TextView.EnsureVisualLines();
        Invoke(window, "CaptureSearchSelection_Click", window, new RoutedEventArgs());
        return Invoke(window, "CapturePosition")!;
    }

    private static void AssertLatestInputs(MainWindow window, string query, int selectedLine)
    {
        Assert.Equal(query, Control<TextBox>(window, "SearchBox").Text);
        Assert.True(Control<CheckBox>(window, "SearchCaseBox").IsChecked);
        Assert.True(Control<CheckBox>(window, "SearchWordBox").IsChecked);
        Assert.True(Control<CheckBox>(window, "SearchRegexBox").IsChecked);
        Assert.Equal(2, Control<ComboBox>(window, "SearchScopeBox").SelectedIndex);
        Assert.Equal("T1", Control<TextBox>(window, "ThreadSearchBox").Text);
        Assert.Equal("latest unfinished highlight", Control<TextBox>(window, "KeywordBox").Text);
        Assert.Equal(4, Control<ComboBox>(window, "KeywordColorBox").SelectedIndex);
        object rule = Assert.Single(Field<IEnumerable>(window, "keywordRules").Cast<object>());
        Assert.Equal("keep", Property<string>(rule, "Keyword")); Assert.False(Property<bool>(rule, "Enabled"));
        var editor = Control<TextEditor>(window, "Editor");
        Assert.Equal("target", editor.SelectedText);
        Assert.Equal(selectedLine, Field<LogData>(window, "data").GetLineIndexAtOffset(editor.SelectionStart));
        var range = Assert.Single(Field<IReadOnlyList<SourceTextRange>>(window, "fixedSearchRanges"));
        Assert.Equal(editor.SelectionStart, range.Offset); Assert.Equal(6, range.Length);
    }

    private static async Task Settle(MainWindow window)
    {
        await (Task)Invoke(window, "SearchAsync")!; await (Task)Invoke(window, "RefreshKeywordsAsync")!;
        var editor = Control<TextEditor>(window, "Editor");
        editor.Measure(new Size(800, 250)); editor.Arrange(new Rect(0, 0, 800, 250)); editor.UpdateLayout();
        editor.TextArea.TextView.EnsureVisualLines();
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static T Property<T>(object item, string name) => (T)item.GetType().GetProperty(name)!.GetValue(item)!;
    private static void Set(MainWindow window, string name, object value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-concurrent-refresh", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public string Write(string name, string text) { string path = System.IO.Path.Combine(Path, name); File.WriteAllText(path, text, new UTF8Encoding(false)); return path; }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
