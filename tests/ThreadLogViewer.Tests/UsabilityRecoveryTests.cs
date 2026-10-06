using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class UsabilityRecoveryTests
{
    private const string Text = "[000:00:01] [T1] synthetic alpha\n[000:00:02] [T2] synthetic beta\n[000:00:03] [T1] synthetic gamma";

    [Fact]
    public Task CompletionAndFailureRemainOutsideHiddenProgressPanel() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text); await Layout(window);
            Assert.Equal(Visibility.Collapsed, Control<Border>(window, "WorkPanel").Visibility);
            var status = Control<TextBlock>(window, "OperationStatus");
            Assert.Contains("완료", status.Text);
            for (DependencyObject? parent = status; parent is not null && parent != window; parent = VisualTreeHelper.GetParent(parent))
            {
                Assert.NotSame(Control<Border>(window, "WorkPanel"), parent);
                if (parent is UIElement element) Assert.Equal(Visibility.Visible, element.Visibility);
            }
            Invoke(window, "ShowError", "합성 파일 읽기 실패", new IOException("합성 경로 없음"));
            Assert.Contains("파일 위치", status.Text);
            Assert.Contains("이전 화면 유지", status.Text);
            Assert.Equal(Visibility.Collapsed, Control<Border>(window, "WorkPanel").Visibility);
            Assert.Equal(Text, Control<TextEditor>(window, "Editor").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ProgressCannotOverwriteCompletedResultOrLeakFromObsoleteOperation() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            var status = Control<TextBlock>(window, "OperationStatus");
            string completed = status.Text;
            var first = ((long Version, CancellationToken Token, IProgress<WorkProgress> Progress))Invoke(window, "BeginWork", "합성 처리", true)!;
            first.Progress.Report(new("합성 진행", 35)); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert.Equal(completed, status.Text);
            Assert.Contains("35", Control<TextBlock>(window, "WorkStatus").Text);
            var second = ((long Version, CancellationToken Token, IProgress<WorkProgress> Progress))Invoke(window, "BeginWork", "합성 새 처리", true)!;
            status.Text = "합성 성공 결과";
            Invoke(window, "FinishWork", second.Version);
            first.Progress.Report(new("합성 오래된 진행", 99));
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert.Equal("합성 성공 결과", status.Text);
            Assert.Equal(Visibility.Collapsed, Control<Border>(window, "WorkPanel").Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ZeroContentMatchesExplainCauseAndClearingContentKeepsThreads() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(t => t.Id == 1))!;
            Control<TextBox>(window, "IncludeBox").Text = "synthetic impossible phrase";
            await Filter(window, new(["synthetic impossible phrase"], []));
            Assert.Equal(0, Field<LogProjection>(window, "projection").Count);
            Assert.Contains("내용 필터", Control<TextBlock>(window, "EmptyHint").Text);
            Assert.Equal(Visibility.Visible, Control<Button>(window, "EmptyClearContentButton").Visibility);
            Assert.Contains("적용 중", Control<TextBlock>(window, "ContentFilterHeader").Text);
            Control<Button>(window, "EmptyClearContentButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Until(() => !Field<bool>(window, "busy"));
            Assert.Equal(2, Field<LogProjection>(window, "projection").Count);
            Assert.Equal(new int?[] { 1 }, Field<LogProjection>(window, "projection").SelectedThreads);
            Assert.Equal(Visibility.Collapsed, Control<FrameworkElement>(window, "EmptyPanel").Visibility);
            Assert.Contains("없음", Control<TextBlock>(window, "ContentFilterHeader").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CombinedEmptyFiltersCanRecoverAllRecordsWithOneAction() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text); await Filter(window, new(["impossible"], []));
            await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(_ => false))!;
            Assert.Contains("선택한 스레드", Control<TextBlock>(window, "EmptyHint").Text);
            Assert.Contains("함께 적용", Control<TextBlock>(window, "EmptyDetail").Text);
            Control<Button>(window, "EmptyClearAllButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Until(() => !Field<bool>(window, "busy"));
            Assert.Equal(3, Field<LogProjection>(window, "projection").Count);
            Assert.Empty(Field<EntryFilter>(window, "appliedFilter").Includes);
            Assert.All(Field<List<ThreadItem>>(window, "threadItems"), t => Assert.True(t.IsSelected));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CollapsedFilterHeaderDistinguishesAppliedConditionAndUnappliedDraft() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            Control<Expander>(window, "ContentFilterExpander").IsExpanded = false;
            Control<TextBox>(window, "IncludeBox").Text = "alpha";
            Assert.Contains("미적용 변경", Control<TextBlock>(window, "ContentFilterHeader").Text);
            await Filter(window, new(["alpha"], []));
            Assert.Contains("적용 중", Control<TextBlock>(window, "ContentFilterHeader").Text);
            Assert.DoesNotContain("미적용 변경", Control<TextBlock>(window, "ContentFilterHeader").Text);
            Assert.False(Control<Expander>(window, "ContentFilterExpander").IsExpanded);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task NavigationBackForwardTracksJumpsAndNewJumpDiscardsForwardBranch() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text); await Layout(window);
            Invoke(window, "NavigateVisible", 2, 8, 0);
            await History(window, false); Assert.Equal(0, Current(window));
            Assert.True(Control<MenuItem>(window, "ForwardNavigationMenu").IsEnabled);
            await History(window, true); Assert.Equal(2, Current(window));
            await History(window, false);
            Invoke(window, "NavigateVisible", 1, 4, 0);
            Assert.False(Control<MenuItem>(window, "ForwardNavigationMenu").IsEnabled);
            await History(window, false); Assert.Equal(0, Current(window));
            await History(window, true); Assert.Equal(1, Current(window));
            Assert.Equal(4, Control<TextEditor>(window, "Editor").TextArea.Caret.Column);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task HiddenSearchJumpRecordsOneNavigationAndBackRestoresNormalFilter() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text); await Layout(window);
            await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(t => t.Id == 1))!;
            var source = Field<LogData>(window, "data");
            var hit = LogSearch.Find(source, Enumerable.Range(0, source.Entries.Count), "beta", new()).Hits.Single();
            await (Task)Invoke(window, "NavigateHitAsync", hit)!;
            Assert.True(Field<bool>(window, "contextActive")); Assert.Equal(1, Current(window));
            await History(window, false);
            Assert.False(Field<bool>(window, "contextActive")); Assert.Equal(0, Current(window));
            Assert.Equal(2, Field<LogProjection>(window, "projection").Count);
            Assert.False(Control<MenuItem>(window, "BackNavigationMenu").IsEnabled);
            await History(window, true);
            Assert.True(Field<bool>(window, "contextActive")); Assert.Equal(1, Current(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ThreadBackgroundSwitchPersistsWithoutChangingLogOrOtherHighlights() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, true);
        try
        {
            await Load(window, Text);
            var document = Control<TextEditor>(window, "Editor").Document;
            var menu = Control<MenuItem>(window, "ThreadBackgroundMenu");
            menu.IsChecked = false; menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(Field<ThreadBackgroundRenderer>(window, "threadRenderer").Enabled);
            Assert.Same(document, Control<TextEditor>(window, "Editor").Document);
            Invoke(window, "SaveSettingsNow");
            Assert.False(new SettingsStore(folder.Path).Load().Settings.ThreadBackgrounds);
        }
        finally { window.Close(); }
        var restored = new MainWindow(folder.Path, false);
        try { Assert.False(Field<ThreadBackgroundRenderer>(restored, "threadRenderer").Enabled); Assert.False(Control<MenuItem>(restored, "ThreadBackgroundMenu").IsChecked); }
        finally { restored.Close(); }
    });

    [Fact]
    public void OlderDisplaySettingsDefaultToEnabledThreadBackgrounds()
    {
        using var folder = new Folder(); Directory.CreateDirectory(System.IO.Path.Combine(folder.Path, "user-data"));
        File.WriteAllText(new SettingsStore(folder.Path).FilePath, "{\"Owner\":\"ThreadLogViewer.UiSettings\",\"Schema\":1,\"Settings\":{\"Dark\":false}}");
        Assert.True(new SettingsStore(folder.Path).Load().Settings.ThreadBackgrounds);
    }

    private static int? Current(MainWindow window) => (int?)Invoke(window, "CurrentSourceLine");
    private static Task History(MainWindow window, bool forward) => (Task)Invoke(window, "NavigateHistoryAsync", forward)!;
    private static Task Filter(MainWindow window, EntryFilter filter) => (Task)Invoke(window, "FilterAsync", filter, null)!;
    private static Task Load(MainWindow window, string text) => (Task)Invoke(window, "LoadAsync",
        (Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>>)((token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress))), "synthetic usability", "synthetic failure")!;
    private static async Task Until(Func<bool> condition)
    {
        for (int pass = 0; pass < 200 && !condition(); pass++) await Task.Delay(10);
        Assert.True(condition());
    }
    private static async Task Layout(MainWindow window)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1040, 600)); content.Arrange(new Rect(0, 0, 1040, 600)); content.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Control<TextEditor>(window, "Editor").TextArea.TextView.EnsureVisualLines();
    }
    private static T Control<T>(MainWindow window, string name) => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string method, params object?[] args) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-usability", Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
