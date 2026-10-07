using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

/// <summary>Analysis workflows on the existing unhosted STA dispatcher; never shows or activates a window.</summary>
public sealed class AnalysisUxTests
{
    [Fact]
    public Task ContextDisplaysActualCountsAndExportScopeWhileKeepingThreadListAvailable() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, SixtyRecords());
            var editor = Control<TextEditor>(window, "Editor");
            editor.TextArea.Caret.Line = 16;
            Assert.True(await (Task<bool>)Invoke(window, "ShowContextAsync", 15)!);
            // Dependency-property values are immediate; their bindings drain at dispatcher DataBind priority.
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

            Assert.True(Control<DockPanel>(window, "FilterPanel").IsEnabled);
            Assert.True(Control<ListBox>(window, "ThreadList").IsEnabled);
            Assert.False((bool)typeof(MainWindow).GetProperty("CanChangeFilters")!.GetValue(window)!);
            Assert.False(Control<StackPanel>(window, "ThreadFilterControls").IsEnabled);
            Assert.False(Control<StackPanel>(window, "TextFilterControls").IsEnabled);
            var threadList = Control<ListBox>(window, "ThreadList");
            threadList.Measure(new Size(275, 400));
            threadList.Arrange(new Rect(0, 0, 275, 400));
            threadList.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var row = (ListBoxItem)threadList.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.NotNull(row);
            Assert.True(row.IsEnabled);
            Assert.False(FindVisual<CheckBox>(row)!.IsEnabled);
            Assert.False(FindVisual<Button>(row)!.IsEnabled);

            var projection = Field<LogProjection>(window, "projection");
            Assert.Equal(36, projection.EntryCount);
            Assert.Equal(Enumerable.Range(0, 36), projection.SourceIndexes);
            string summary = Control<TextBlock>(window, "ContextStatus").Text;
            Assert.Contains("원본 16줄", summary);
            Assert.Contains("앞 15기록", summary);
            Assert.Contains("뒤 20기록", summary);
            Assert.Contains("모든 스레드", summary);
            Assert.Equal("주변 로그 내보내기", Control<TextBlock>(window, "ExportLabel").Text);
            Assert.Equal(15, Field<OriginalLineMargin>(window, "margin").ContextLineIndex);
            Assert.Equal(15, Field<ThreadBackgroundRenderer>(window, "threadRenderer").ContextEntryIndex);

            await (Task)Invoke(window, "FilterAsync", null, Field<object?>(window, "normalAnchor"))!;
            Assert.False(Field<bool>(window, "contextActive"));
            Assert.True((bool)typeof(MainWindow).GetProperty("CanChangeFilters")!.GetValue(window)!);
            Assert.Equal("필터 결과 내보내기", Control<TextBlock>(window, "ExportLabel").Text);
            Assert.Equal(60, Field<LogProjection>(window, "projection").EntryCount);
            Assert.Null(Field<OriginalLineMargin>(window, "margin").ContextLineIndex);
            Assert.Null(Field<ThreadBackgroundRenderer>(window, "threadRenderer").ContextEntryIndex);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ContextCountsClipAtEachFileBoundaryAndMarkOwningHeader() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, SixtyRecords());
            Assert.True(await (Task<bool>)Invoke(window, "ShowContextAsync", 0)!);
            Assert.Contains("앞 0기록 / 뒤 20기록", Control<TextBlock>(window, "ContextStatus").Text);
            Assert.Equal(21, Field<LogProjection>(window, "projection").EntryCount);
            Assert.True(await (Task<bool>)Invoke(window, "ShowContextAsync", 59)!);
            Assert.Contains("앞 20기록 / 뒤 0기록", Control<TextBlock>(window, "ContextStatus").Text);
            Assert.Equal(21, Field<LogProjection>(window, "projection").EntryCount);
            string currentContext = Control<TextBlock>(window, "ContextStatus").Text;
            Control<TextBox>(window, "ContextRadiusBox").Text = "-1";
            Assert.False(await (Task<bool>)Invoke(window, "ShowContextAsync", 0)!);
            Assert.Equal(currentContext, Control<TextBlock>(window, "ContextStatus").Text);
            Assert.Contains("0~10,000", Control<TextBlock>(window, "ContextInputStatus").Text);
            Assert.Equal(21, Field<LogProjection>(window, "projection").EntryCount);
            Control<TextBox>(window, "ContextRadiusBox").Text = "20";

            await Load(window, "[000:00:01] [T1] synthetic first\nsynthetic body\n[000:00:02] [T2] synthetic next");
            Assert.True(await (Task<bool>)Invoke(window, "ShowContextAsync", 1)!);
            Assert.Contains("원본 2줄", Control<TextBlock>(window, "ContextStatus").Text);
            Assert.Equal(0, Field<OriginalLineMargin>(window, "margin").ContextLineIndex);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LegacyAnalysisPanelStaysHiddenAndCompactTimeActionsReflectAssignedPoints() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01.000] [T1] synthetic first\nsynthetic body\n[000:00:01.012] [T2] synthetic next\n[000:99:00] [T3] synthetic invalid");
            for (int selected = 0; selected < 3; selected++) Invoke(window, "SetAnalysisTool", selected, false);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert.Equal(Visibility.Collapsed, Control<ScrollViewer>(window, "ToolsScroll").Visibility);
            Assert.Equal(Visibility.Collapsed, Control<Expander>(window, "AnalysisPanel").Visibility);
            Assert.Equal(Visibility.Collapsed, Control<Border>(window, "TimeResultPanel").Visibility);
            Assert.False(Control<Button>(window, "SwapTimeButton").IsEnabled);
            Assert.False(Control<Button>(window, "CopyTimeButton").IsEnabled);
            Assert.False(Control<Button>(window, "ClearTimeButton").IsEnabled);

            Control<TextEditor>(window, "Editor").TextArea.Caret.Line = 2;
            Invoke(window, "UpdatePosition");
            Assert.Contains("원본 2줄", Control<TextBlock>(window, "CurrentRecordStatus").Text);
            Assert.Contains("헤더 1줄", Control<TextBlock>(window, "CurrentRecordStatus").Text);
            Invoke(window, "SetTimePoint", true);
            Assert.True(Control<Button>(window, "ClearTimeButton").IsEnabled);
            Assert.False(Control<Button>(window, "SwapTimeButton").IsEnabled);
            Assert.False(Control<Button>(window, "CopyTimeButton").IsEnabled);
            Control<TextEditor>(window, "Editor").TextArea.Caret.Line = 3;
            Invoke(window, "SetTimePoint", false);
            Assert.True(Control<Button>(window, "SwapTimeButton").IsEnabled);
            Assert.True(Control<Button>(window, "CopyTimeButton").IsEnabled);
            Assert.Contains("0.012", Control<TextBlock>(window, "TimeDifferenceStatus").Text);
            Assert.Equal(Visibility.Visible, Control<Border>(window, "TimeResultPanel").Visibility);
            Assert.Contains("0.012", Control<TextBlock>(window, "TimeSummary").Text);

            Control<TextEditor>(window, "Editor").TextArea.Caret.Line = 4;
            Invoke(window, "SetTimePoint", false);
            Assert.True(Control<Button>(window, "CopyTimeButton").IsEnabled);
            Assert.True(Control<Button>(window, "SwapTimeButton").IsEnabled);
            Assert.True(Control<Button>(window, "ClearTimeButton").IsEnabled);
            Assert.Contains("0.012", Control<TextBlock>(window, "TimeSummary").Text);
            Invoke(window, "ClearTime_Click", window, new RoutedEventArgs());
            Assert.False(Control<Button>(window, "ClearTimeButton").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Control<Border>(window, "TimeResultPanel").Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task KeywordInputEnterAddsRuleAndEmptyStateAndCapacityStayAccurate() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic first second third fourth fifth sixth seventh eighth");
            Invoke(window, "SetAnalysisTool", 2, false);
            Assert.Equal(Visibility.Visible, Control<TextBlock>(window, "KeywordEmptyHint").Visibility);
            Assert.False(Control<Button>(window, "KeywordAddButton").IsEnabled);
            var input = Control<TextBox>(window, "KeywordBox");
            input.Text = "synthetic";
            Assert.True(Control<Button>(window, "KeywordAddButton").IsEnabled);
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, new SyntheticPresentationSource(), 0, Key.Enter)
                { RoutedEvent = Keyboard.KeyDownEvent };
            Invoke(window, "Keyword_KeyDown", input, key);
            Assert.True(key.Handled);
            await (Task)Invoke(window, "RefreshKeywordsAsync")!;
            Assert.Single(Control<ListBox>(window, "KeywordList").Items.Cast<object>());
            Assert.Equal(Visibility.Collapsed, Control<TextBlock>(window, "KeywordEmptyHint").Visibility);
            Assert.Equal("", input.Text);
            Assert.False(Control<Button>(window, "KeywordAddButton").IsEnabled);
            foreach (string keyword in new[] { "first", "second", "third", "fourth", "fifth", "sixth", "seventh" })
            {
                input.Text = keyword;
                Invoke(window, "AddKeyword_Click", window, new RoutedEventArgs());
            }
            input.Text = "ninth";
            Assert.False(Control<Button>(window, "KeywordAddButton").IsEnabled);
            Assert.Equal(8, Control<ListBox>(window, "KeywordList").Items.Count);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DraftFilterIsExplicitAndDoesNotChangeAppliedRecordsUntilApplied() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic Write\nbody\n[000:00:02] [T2] synthetic Read");
            Assert.Contains("현재 적용: 내용 필터 없음", Control<TextBlock>(window, "FilterSummary").Text);
            Control<TextBox>(window, "IncludeBox").Text = "Write";
            Control<ComboBox>(window, "IncludeModeBox").SelectedIndex = 1;
            Assert.Contains("변경 사항 미적용", Control<TextBlock>(window, "FilterDirtyStatus").Text);
            Assert.Equal(2, Field<LogProjection>(window, "projection").EntryCount);
            Assert.Contains("내용 필터 없음", Control<TextBlock>(window, "FilterSummary").Text);
            await (Task)Invoke(window, "FilterAsync", new EntryFilter(["Write"], [], true), null)!;
            Assert.Equal(1, Field<LogProjection>(window, "projection").EntryCount);
            Assert.Contains("모두 포함", Control<TextBlock>(window, "FilterSummary").Text);
            Assert.DoesNotContain("미적용", Control<TextBlock>(window, "FilterDirtyStatus").Text);
            Control<TextBox>(window, "ExcludeBox").Text = "heartbeat";
            Assert.Contains("변경 사항 미적용", Control<TextBlock>(window, "FilterDirtyStatus").Text);
            Assert.Equal(1, Field<LogProjection>(window, "projection").EntryCount);
            Assert.DoesNotContain("제외", Control<TextBlock>(window, "FilterSummary").Text);
        }
        finally { window.Close(); }
    });

    private static string SixtyRecords() => string.Join('\n', Enumerable.Range(0, 60)
        .Select(i => $"[000:00:{i:00}] [T{i % 3}] synthetic record {i}"));
    private static Task Load(MainWindow window, string text)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) =>
            Task.FromResult(LogParser.ParsePastedText(text, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic UX load", "synthetic UX failure")!;
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static T? FindVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            if (FindVisual<T>(child) is { } descendant) return descendant;
        }
        return null;
    }
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
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-analysis-ux", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
