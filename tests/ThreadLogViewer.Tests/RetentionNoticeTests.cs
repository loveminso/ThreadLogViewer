using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class RetentionNoticeTests
{
    [Fact]
    public void TrimReportsEvictionsWhenSharedOwnershipDisappears()
    {
        object source = new();
        var owner = new RetentionSample(source, new());
        var closed = new RetentionSample(source, new());
        var cache = new ClosedSessionRetention<RetentionSample>(item => [new(item.Source, 90), new(item.Document, 20)], maximumBytes: 100);
        Assert.True(cache.Add(closed, 0, [owner], out int evicted)); Assert.Equal(0, evicted);
        Assert.Equal(20, cache.EstimatedBytes);
        Assert.Equal(0, cache.Trim([owner]));
        Assert.Equal(1, cache.Trim([]));
        Assert.Equal(0, cache.Count); Assert.Equal(0, cache.EstimatedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RefreshAndEncodingRereadReportTabsEvictedAfterReplacingTheirSharedSource(bool encodingReread) => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string original = string.Join('\n', Enumerable.Range(0, 40).Select(index =>
            $"[000:00:{index:00}] [T1] synthetic retention record {index} {new string('x', 100)}"));
        string path = folder.Write("synthetic-retention-source.log", original);
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await Open(window, path));
            object owner = Control<ListBox>(window, "SessionTabs").SelectedItem;
            var source = Field<LogData>(window, "data");
            Assert.True(await (Task<bool>)Invoke(window, "OpenLineSessionAsync", 0, 0)!);
            object split = Control<ListBox>(window, "SessionTabs").SelectedItem;
            ConfigureSmallSyntheticBudget(window, owner, split, source);
            Invoke(window, "CloseActiveSession_Click", window, new RoutedEventArgs());
            Assert.True(PrivateProperty<bool>(window, "CanRestoreClosedSession"));
            if (encodingReread) await (Task)Invoke(window, "ReloadCurrentAsync", EncodingMode.Cp949)!;
            else
            {
                File.WriteAllText(path, "[000:00:00] [T9] synthetic inserted\n" + original, new UTF8Encoding(false));
                await (Task)Invoke(window, "RefreshCurrentSessionAsync")!;
            }
            Assert.NotSame(source, Field<LogData>(window, "data"));
            Assert.False(PrivateProperty<bool>(window, "CanRestoreClosedSession"));
            string status = Control<TextBlock>(window, "OperationStatus").Text;
            Assert.Contains("닫은 탭 1개", status);
            Assert.Contains("추정 보관 한도", status);
            Assert.Contains("다시 여세요", status);
            Assert.Equal(encodingReread ? original : "[000:00:00] [T9] synthetic inserted\n" + original, File.ReadAllText(path));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task BatchSummaryKeepsAnEarlierFailureVisibleAfterTheFinalSuccessfulFile() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string path = folder.Write("synthetic-success.log", "[000:00:01] [T1] synthetic body never shown in batch summary");
        string missing = System.IO.Path.Combine(folder.Path, "synthetic-missing.log");
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.False(await (Task<bool>)Invoke(window, "OpenFilesAsync", (object)new[] { missing, path })!);
            Assert.Equal(path, Field<LogData>(window, "data").SourcePath);
            string status = Control<TextBlock>(window, "OperationStatus").Text;
            Assert.Contains("성공 1개", status); Assert.Contains("실패 1개", status); Assert.Contains("미처리 0개", status);
            Assert.Contains("synthetic-missing.log", status); Assert.Contains("다시 열어 주세요", status);
            Assert.DoesNotContain("synthetic body never shown", status);
            Assert.Equal("[000:00:01] [T1] synthetic body never shown in batch summary", File.ReadAllText(path));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CancelledBatchReportsCommittedSuccessAndCurrentAndRemainingUnprocessedFiles() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string a = folder.Write("synthetic-batch-A.log", "[000:00:01] [T1] synthetic A");
        string b = folder.Write("synthetic-batch-B.log", "[000:00:02] [T2] synthetic B");
        string c = folder.Write("synthetic-batch-C.log", "[000:00:03] [T3] synthetic C");
        var window = new MainWindow(folder.Path, false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? pending = null;
        int attempts = 0;
        try
        {
            Func<string, Task<bool>> open = path =>
            {
                attempts++;
                if (path != b) return Open(window, path);
                Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = async (token, _) =>
                { started.SetResult(token); await release.Task; return LogParser.Parse("[000:00:02] [T2] synthetic B", b, cancellationToken: token); };
                return (Task<bool>)Invoke(window, "LoadAsync", load, "synthetic delayed batch", "synthetic failure")!;
            };
            pending = (Task<bool>)Invoke(window, "OpenFileBatchAsync", new[] { a, b, c }, open)!;
            var token = await started.Task;
            Invoke(window, "Cancel_Click", window, new RoutedEventArgs());
            release.SetResult(); Assert.False(await pending);
            Assert.True(token.IsCancellationRequested); Assert.Equal(2, attempts);
            Assert.Equal(a, Field<LogData>(window, "data").SourcePath);
            string status = Control<TextBlock>(window, "OperationStatus").Text;
            Assert.Contains("파일 묶음 열기 취소", status);
            Assert.Contains("성공 1개", status); Assert.Contains("실패 0개", status); Assert.Contains("미처리 2개", status);
            Assert.Contains("다시 선택해", status);
            Assert.Equal("[000:00:02] [T2] synthetic B", File.ReadAllText(b));
            Assert.Equal("[000:00:03] [T3] synthetic C", File.ReadAllText(c));
        }
        finally { release.TrySetResult(); if (pending is not null) await pending; window.Close(); }
    });

    [Fact]
    public Task ADelayedObsoleteBatchCannotOverwriteANewBlankTabsResult() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string path = folder.Write("synthetic-obsolete-batch.log", "[000:00:01] [T1] synthetic obsolete");
        var window = new MainWindow(folder.Path, false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? pending = null;
        try
        {
            Func<string, Task<bool>> open = ignoredPath =>
            {
                Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = async (token, _) =>
                { started.SetResult(); await release.Task; return LogParser.Parse("[000:00:01] [T1] synthetic obsolete", path, cancellationToken: token); };
                return (Task<bool>)Invoke(window, "LoadAsync", load, "synthetic obsolete batch", "synthetic failure")!;
            };
            pending = (Task<bool>)Invoke(window, "OpenFileBatchAsync", new[] { path }, open)!;
            await started.Task;
            Invoke(window, "CreateBlankSession");
            string latest = Control<TextBlock>(window, "OperationStatus").Text;
            release.SetResult(); Assert.False(await pending);
            Assert.True(PrivateProperty<bool>(window, "IsBlankSession"));
            Assert.Equal(latest, Control<TextBlock>(window, "OperationStatus").Text);
        }
        finally { release.TrySetResult(); if (pending is not null) await pending; window.Close(); }
    });

    [Fact]
    public Task ClosingTheWindowClearsOpenClosedAndRenderedSourceReferences() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string a = folder.Write("synthetic-dispose-A.log", "[000:00:01] [T1] synthetic target A");
        string b = folder.Write("synthetic-dispose-B.log", "[000:00:02] [T2] synthetic target B");
        var window = new MainWindow(folder.Path, false);
        bool closed = false;
        try
        {
            Assert.True(await Open(window, a));
            object first = Control<ListBox>(window, "SessionTabs").SelectedItem;
            Assert.True(await Open(window, b));
            Invoke(window, "CloseSession", first);
            Assert.True(PrivateProperty<bool>(window, "CanRestoreClosedSession"));
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "target";
            await (Task)Invoke(window, "SearchAsync")!;
            Assert.NotNull(Control<ListBox>(window, "ResultsList").ItemsSource);
            window.Close(); closed = true;
            Assert.Null(Field<object?>(window, "activeSession"));
            Assert.Null(Field<object?>(window, "data")); Assert.Null(Field<object?>(window, "projection"));
            Assert.Empty(Field<IEnumerable>(window, "sessions").Cast<object>());
            Assert.False(PrivateProperty<bool>(window, "CanRestoreClosedSession"));
            Assert.Null(Control<ListBox>(window, "ThreadList").ItemsSource);
            Assert.Null(Control<ListBox>(window, "ResultsList").ItemsSource);
            Assert.Equal(0, Control<ICSharpCode.AvalonEdit.TextEditor>(window, "Editor").Document.TextLength);
            Assert.Null(Field<object>(window, "threadRenderer").GetType().GetProperty("Projection")!.GetValue(Field<object>(window, "threadRenderer")));
            Assert.Null(Field<object>(window, "margin").GetType().GetProperty("Projection")!.GetValue(Field<object>(window, "margin")));
        }
        finally { if (!closed) window.Close(); }
    });

    [Fact]
    public Task ARefreshCompletedAfterBatchCancellationKeepsItsOwnResultMessage() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string a = folder.Write("synthetic-newer-work-A.log", "[000:00:01] [T1] synthetic baseline");
        string b = folder.Write("synthetic-newer-work-B.log", "[000:00:02] [T2] synthetic obsolete");
        var window = new MainWindow(folder.Path, false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? pending = null;
        try
        {
            Assert.True(await Open(window, a));
            Func<string, Task<bool>> open = ignoredPath =>
            {
                Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = async (token, _) =>
                { started.SetResult(); await release.Task; return LogParser.Parse("[000:00:02] [T2] synthetic obsolete", b, cancellationToken: token); };
                return (Task<bool>)Invoke(window, "LoadAsync", load, "synthetic obsolete batch", "synthetic failure")!;
            };
            pending = (Task<bool>)Invoke(window, "OpenFileBatchAsync", new[] { b }, open)!;
            await started.Task;
            File.AppendAllText(a, "\n[000:00:03] [T3] synthetic appended", new UTF8Encoding(false));
            await (Task)Invoke(window, "RefreshCurrentSessionAsync")!;
            string latest = Control<TextBlock>(window, "OperationStatus").Text;
            Assert.Contains("파일 새로 고침 완료", latest);
            release.SetResult(); Assert.False(await pending);
            Assert.Equal(a, Field<LogData>(window, "data").SourcePath);
            Assert.Equal(latest, Control<TextBlock>(window, "OperationStatus").Text);
        }
        finally { release.TrySetResult(); if (pending is not null) await pending; window.Close(); }
    });

    private static void ConfigureSmallSyntheticBudget(MainWindow window, object open, object closed, LogData sharedSource)
    {
        var method = typeof(MainWindow).GetMethod("SessionResources", BindingFlags.Static | BindingFlags.NonPublic)!;
        var openIdentities = ((IEnumerable<RetainedResource>)method.Invoke(null, [open])!).Select(resource => resource.Identity).ToHashSet(ReferenceEqualityComparer.Instance);
        var resources = ((IEnumerable<RetainedResource>)method.Invoke(null, [closed])!).ToArray();
        long sourceBytes = resources.Single(resource => ReferenceEquals(resource.Identity, sharedSource)).EstimatedBytes;
        long additional = resources.Where(resource => !openIdentities.Contains(resource.Identity)).GroupBy(resource => resource.Identity, ReferenceEqualityComparer.Instance).Sum(group => group.First().EstimatedBytes);
        long budget = additional + sourceBytes / 2;
        Type sessionType = closed.GetType();
        Type cacheType = typeof(ClosedSessionRetention<>).MakeGenericType(sessionType);
        var extractor = method.CreateDelegate(typeof(Func<,>).MakeGenericType(sessionType, typeof(IEnumerable<RetainedResource>)));
        object cache = Activator.CreateInstance(cacheType, [extractor, 8, budget])!;
        typeof(MainWindow).GetField("closedSessions", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, cache);
    }

    private sealed record RetentionSample(object Source, object Document);
    private static Task<bool> Open(MainWindow window, string path) => (Task<bool>)Invoke(window, "OpenAsync", path, EncodingMode.Auto)!;
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static T PrivateProperty<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-retention-notice", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public string Write(string name, string text) { string path = System.IO.Path.Combine(Path, name); File.WriteAllText(path, text, new UTF8Encoding(false)); return path; }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
