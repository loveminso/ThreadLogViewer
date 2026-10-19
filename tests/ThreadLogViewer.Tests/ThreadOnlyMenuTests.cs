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

public sealed class ThreadOnlyMenuTests
{
    private const string Mixed =
        "dump mentions [T3] but has no prefix\n" +
        "[10:03:12] [T3] header\n" +
        "[T7] looks like seven\n" +
        "[10:00:00] [T9999999999999999] broken\n";

    [Fact]
    public Task ClickedLineUsesOwningThreadIdAndLeavesNullOrOverflowDisabled() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Mixed);
            var lines = Field<LogData>(window, "data").Lines;
            Assert.Null(lines[0].ThreadId);
            Assert.Equal(3, lines[1].ThreadId);
            Assert.Equal(3, lines[2].ThreadId);
            Assert.Equal(ParseQuality.Continuation, lines[2].Quality);
            Assert.Null(lines[3].ThreadId);

            var missing = Open(window, 1);
            Assert.Equal("이 스레드만 보기", Header(missing.only));
            Assert.False(missing.only.IsEnabled);
            Assert.False(missing.clear.IsEnabled);
            int entries = Field<LogProjection>(window, "projection").EntryCount;
            Invoke(window, "ThreadOnly_Click", window, new RoutedEventArgs());
            await Task.Delay(400);
            Assert.False(Field<bool>(window, "busy"));
            Assert.Equal(entries, Field<LogProjection>(window, "projection").EntryCount);

            var header = Open(window, 2);
            Assert.Equal("이 스레드만 보기(스레드 3)", Header(header.only));
            Assert.True(header.only.IsEnabled);
            Assert.False(header.clear.IsEnabled);

            var continuation = Open(window, 3);
            Assert.Equal("이 스레드만 보기(스레드 3)", Header(continuation.only));
            Assert.DoesNotContain("7", Header(continuation.only));
            Assert.True(continuation.only.IsEnabled);

            var overflow = Open(window, 4);
            Assert.Equal("이 스레드만 보기", Header(overflow.only));
            Assert.False(overflow.only.IsEnabled);
            Assert.False(overflow.clear.IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task MenuEnablementFollowsFilterLockBusyAndUncheckedThreads() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[10:03:12] [T3] header\n[10:03:13] [T4] other\n");
            Assert.True(window.CanChangeFilters);
            var ready = Open(window, 1);
            Assert.Equal("이 스레드만 보기(스레드 3)", Header(ready.only));
            Assert.True(ready.only.IsEnabled);
            Assert.False(ready.clear.IsEnabled);

            window.SetValue(MainWindow.CanChangeFiltersProperty, false);
            var locked = Open(window, 1);
            Assert.Equal("이 스레드만 보기", Header(locked.only));
            Assert.False(locked.only.IsEnabled);
            Assert.False(locked.clear.IsEnabled);

            window.SetValue(MainWindow.CanChangeFiltersProperty, true);
            Set(window, "busy", true);
            var working = Open(window, 1);
            Assert.Equal("이 스레드만 보기", Header(working.only));
            Assert.False(working.only.IsEnabled);
            Assert.False(working.clear.IsEnabled);

            Set(window, "busy", false);
            SetSelected(window, 4, false);
            var partial = Open(window, 1);
            Assert.Equal("이 스레드만 보기(스레드 3)", Header(partial.only));
            Assert.True(partial.only.IsEnabled);
            Assert.True(partial.clear.IsEnabled);

            Set(window, "busy", true);
            var partialBusy = Open(window, 1);
            Assert.Equal("이 스레드만 보기", Header(partialBusy.only));
            Assert.False(partialBusy.only.IsEnabled);
            Assert.False(partialBusy.clear.IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FilterSummaryNamesAPartialThreadSelection() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[10:03:12] [T3] header\n[T7] looks like seven\n[10:03:13] [T4] other\n");
            Assert.Equal("현재 적용: 내용 필터 없음", Summary(window));
            await SetThreads(window, thread => thread.Id == 3);
            Assert.Equal($"현재 적용: 내용 필터 없음 · 스레드 {1:N0}개 선택", Summary(window));
            await (Task)Invoke(window, "FilterAsync", new EntryFilter(["header"], []), null)!;
            Assert.Equal($"현재 적용: 포함 1개 중 하나라도 포함 · 스레드 {1:N0}개 선택", Summary(window));
            await SetThreads(window, _ => false);
            Assert.Equal($"현재 적용: 포함 1개 중 하나라도 포함 · 스레드 {0:N0}개 선택", Summary(window));
            await (Task)Invoke(window, "ResetAllFiltersAsync")!;
            Assert.Equal("현재 적용: 내용 필터 없음", Summary(window));
            Assert.DoesNotContain("스레드", Summary(window));
            Assert.Equal(2, Field<LogProjection>(window, "projection").EntryCount);
            Assert.All(Field<List<ThreadItem>>(window, "threadItems"), thread => Assert.True(thread.IsSelected));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ThreadOnlyClickKeepsTheOwningThreadAndClearRestoresEveryThread() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[10:03:12] [T3] header\n[T7] looks like seven\n[10:03:13] [T4] other\n");
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 2, 1)!);
            Invoke(window, "LineContext_Opened", Menu(window), new RoutedEventArgs());
            Invoke(window, "ThreadOnly_Click", window, new RoutedEventArgs());
            await Until(() => !Field<bool>(window, "busy") && Field<LogProjection>(window, "projection").EntryCount == 1, "thread-only click");
            var view = Field<LogProjection>(window, "projection");
            Assert.Contains("header", view.Text);
            Assert.Contains("looks like seven", view.Text);
            Assert.DoesNotContain("other", view.Text);
            var selectedThread = Assert.Single(Field<List<ThreadItem>>(window, "threadItems"), thread => thread.IsSelected);
            Assert.True(selectedThread.Id == 3);
            Assert.Equal($"현재 적용: 내용 필터 없음 · 스레드 {1:N0}개 선택", Summary(window));

            Invoke(window, "ThreadClear_Click", window, new RoutedEventArgs());
            await Until(() => !Field<bool>(window, "busy") && Field<LogProjection>(window, "projection").EntryCount == 2, "thread clear click");
            Assert.Contains("other", Field<LogProjection>(window, "projection").Text);
            Assert.Equal("현재 적용: 내용 필터 없음", Summary(window));
            Assert.All(Field<List<ThreadItem>>(window, "threadItems"), thread => Assert.True(thread.IsSelected));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ThreadOnlySelectionSurvivesRefreshAndShowsAppendedLinesOfThatThread() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        const string original = "[000:00:01] [T1] synthetic keep alpha\n[000:00:02] [T2] synthetic hide beta\n";
        const string appended = "[000:00:03] [T1] synthetic keep appended\nappended body stays\n[000:00:04] [T9] synthetic new thread\n";
        string path = folder.Write("synthetic-thread-only.log", original);
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await OpenFile(window, path));
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 1, 1)!);
            Invoke(window, "LineContext_Opened", Menu(window), new RoutedEventArgs());
            Invoke(window, "ThreadOnly_Click", window, new RoutedEventArgs());
            await Until(() => !Field<bool>(window, "busy") && Field<LogProjection>(window, "projection").EntryCount == 1, "thread-only before refresh");
            File.AppendAllText(path, appended, new UTF8Encoding(false, true));
            byte[] expected = File.ReadAllBytes(path);
            await (Task)Invoke(window, "RefreshCurrentSessionAsync")!;
            Assert.Equal(expected, File.ReadAllBytes(path));
            var source = Field<LogData>(window, "data");
            var view = Field<LogProjection>(window, "projection");
            int appendedHeader = IndexOf(source, "synthetic keep appended");
            int appendedBody = IndexOf(source, "appended body stays");
            Assert.Equal(1, source.Lines[appendedHeader].ThreadId);
            Assert.Equal(1, source.Lines[appendedBody].ThreadId);
            Assert.Equal(ParseQuality.Continuation, source.Lines[appendedBody].Quality);
            Assert.Contains(appendedHeader, view.SourceIndexes);
            Assert.Contains(appendedBody, view.SourceIndexes);
            Assert.Contains("synthetic keep alpha", view.Text);
            Assert.Contains("synthetic keep appended", view.Text);
            Assert.Contains("appended body stays", view.Text);
            Assert.DoesNotContain("synthetic hide beta", view.Text);
            Assert.DoesNotContain("synthetic new thread", view.Text);
            var threads = Field<List<ThreadItem>>(window, "threadItems");
            Assert.True(threads.Single(thread => thread.Id == 1).IsSelected);
            Assert.False(threads.Single(thread => thread.Id == 2).IsSelected);
            Assert.False(threads.Single(thread => thread.Id == 9).IsSelected);
            Assert.Equal($"현재 적용: 내용 필터 없음 · 스레드 {1:N0}개 선택", Summary(window));
            Assert.Contains("파일 새로 고침 완료", Control<TextBlock>(window, "OperationStatus").Text);

            await (Task)Invoke(window, "ResetAllFiltersAsync")!;
            Assert.Equal(4, Field<LogProjection>(window, "projection").EntryCount);
            Assert.Contains("synthetic new thread", Field<LogProjection>(window, "projection").Text);
            Assert.Equal("현재 적용: 내용 필터 없음", Summary(window));
            Assert.All(Field<List<ThreadItem>>(window, "threadItems"), thread => Assert.True(thread.IsSelected));
            Assert.Equal(expected, File.ReadAllBytes(path));
        }
        finally { window.Close(); }
    });

    private static int IndexOf(LogData source, string text)
    {
        for (int index = 0; index < source.Lines.Count; index++)
            if (source.Lines[index].RawText.Span.Contains(text, StringComparison.Ordinal)) return index;
        throw new Xunit.Sdk.XunitException("Missing synthetic line: " + text);
    }

    private static (MenuItem only, MenuItem clear) Open(MainWindow window, int displayLine)
    {
        Assert.True((bool)Invoke(window, "CaptureLineActionTarget", displayLine, 1)!);
        var menu = Menu(window);
        Invoke(window, "LineContext_Opened", menu, new RoutedEventArgs());
        return (Item(menu, "thread-only"), Item(menu, "thread-clear"));
    }

    private static string Header(MenuItem item) => Assert.IsType<string>(item.Header);
    private static ContextMenu Menu(MainWindow window) => Control<TextEditor>(window, "Editor").ContextMenu!;
    private static MenuItem Item(ContextMenu menu, string tag) => menu.Items.OfType<MenuItem>().Single(item => (item.Tag as string) == tag);
    private static string Summary(MainWindow window) => Control<TextBlock>(window, "FilterSummary").Text;
    private static async Task Until(Func<bool> ready, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(40);
        Assert.True(ready(), message);
    }
    private static void SetSelected(MainWindow window, int? id, bool selected)
    {
        Set(window, "suppressFilters", true);
        Field<List<ThreadItem>>(window, "threadItems").Single(thread => thread.Id == id).IsSelected = selected;
        Set(window, "suppressFilters", false);
    }
    private static Task SetThreads(MainWindow window, Func<ThreadItem, bool> predicate) => (Task)Invoke(window, "SetThreadsAsync", predicate)!;
    private static Task Load(MainWindow window, string text)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic load", "synthetic failure")!;
    }
    private static Task<bool> OpenFile(MainWindow window, string path) => (Task<bool>)Invoke(window, "OpenAsync", path, EncodingMode.Auto)!;
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    private static object? Invoke(MainWindow window, string name, params object?[] values) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, values);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [action])!;

    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-thread-only", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public string Write(string name, string text)
        {
            string path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, text, new UTF8Encoding(false, true));
            return path;
        }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
