using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class BoundaryRecoveryTests
{
    private const string Text = "[000:00:01] [T1] synthetic alpha\n[000:00:02] [T1] synthetic beta\n[000:00:03] [T2] synthetic gamma";

    [Fact]
    public Task UnfinishedOtherTabNavigationDoesNotSuppressTheCurrentTabsHistory() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        IDisposable? pending = null;
        try
        {
            await Load(window, Text);
            object firstHistory = History(window);
            pending = (IDisposable)Invoke(window, "BeginNavigation")!;
            await Load(window, Text + "\nsynthetic second snapshot");
            Invoke(window, "NavigateVisible", 1, 1, 0);
            Assert.Single(Back(window).Cast<object>());
            firstHistory.GetType().GetProperty("Replaying")!.SetValue(firstHistory, true);
            await (Task)Invoke(window, "NavigateHistoryAsync", false)!;
            Assert.Empty(Back(window).Cast<object>());
            Assert.Equal(0, (int?)Invoke(window, "CurrentSourceLine"));
            pending.Dispose(); pending = null;
            Assert.Equal(0, firstHistory.GetType().GetProperty("Depth")!.GetValue(firstHistory));
        }
        finally { pending?.Dispose(); window.Close(); }
    });

    [Fact]
    public Task TransientSettingsLockKeepsDirtyStateAndClosingRetriesTheSave() => InSta(() =>
    {
        using var folder = new Folder(); var store = new SettingsStore(folder.Path);
        Assert.True(store.Save(UiSettings.Default).Success);
        var window = new MainWindow(folder.Path, true);
        try
        {
            using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                window.FindName("ThreadBackgroundMenu");
                Field<object>(window, "threadRenderer").GetType().GetProperty("Enabled")!.SetValue(Field<object>(window, "threadRenderer"), false);
                Invoke(window, "ScheduleSettingsSave"); Invoke(window, "SaveSettingsNow");
                Assert.True(Field<bool>(window, "settingsDirty"));
                Assert.Contains("저장 안 됨", Control<TextBlock>(window, "OperationStatus").Text);
            }
            window.Close();
            Assert.False(store.Load().Settings.ThreadBackgrounds);
            Assert.False(Field<bool>(window, "settingsDirty"));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task InvalidRegexClearsThePreviousResultPaneAndCanRecover() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text); Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "alpha"; await (Task)Invoke(window, "SearchAsync")!;
            Assert.Equal(Visibility.Visible, Control<Border>(window, "ResultsPanel").Visibility);
            Control<CheckBox>(window, "SearchRegexBox").IsChecked = true;
            Control<TextBox>(window, "SearchBox").Text = "["; await (Task)Invoke(window, "SearchAsync")!;
            Assert.Equal(Visibility.Collapsed, Control<Border>(window, "ResultsPanel").Visibility);
            Assert.Null(Control<ListBox>(window, "ResultsList").ItemsSource);
            Assert.Contains("정규식", Control<TextBlock>(window, "SearchStatus").Text);
            Control<TextBox>(window, "SearchBox").Text = "beta"; await (Task)Invoke(window, "SearchAsync")!;
            Assert.Equal(Visibility.Visible, Control<Border>(window, "ResultsPanel").Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ExplicitCancellationLeavesNoSearchingMessageOrEmptyResultsPane() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text); Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "alpha";
            var op = ((long Version, CancellationToken Token, IProgress<WorkProgress> Progress))Invoke(window, "BeginWork", "synthetic pending file", true)!;
            var pending = (Task)Invoke(window, "SearchAsync")!;
            Invoke(window, "Cancel_Click", window, new RoutedEventArgs()); await pending;
            Invoke(window, "FinishWork", op.Version);
            Assert.Contains("취소", Control<TextBlock>(window, "SearchStatus").Text);
            Assert.Equal(Visibility.Collapsed, Control<Border>(window, "ResultsPanel").Visibility);
            Assert.Same(Field<LogData>(window, "data"), Field<LogProjection>(window, "projection").Source);
        }
        finally { window.Close(); }
    });

    private static object History(MainWindow window) => Field<object>(window, "activeSession").GetType().GetProperty("History")!.GetValue(Field<object>(window, "activeSession"))!;
    private static IList Back(MainWindow window) => (IList)History(window).GetType().GetProperty("Back")!.GetValue(History(window))!;
    private static async Task Load(MainWindow window, string text) => Assert.True(await (Task<bool>)Invoke(window, "LoadAsync",
        (Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>>)((token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress))), "synthetic load", "synthetic failure")!);
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-boundary-recovery", Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
