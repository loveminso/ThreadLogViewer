using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class LineActionsTests
{
    [Fact]
    public Task CapturedClickedLineControlsTimeRatherThanOldCaret() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] first\r\nbody\r\n[000:00:05] [T2] second\r\nbody two\r\n");
            var editor = Control<TextEditor>(window, "Editor");
            editor.TextArea.Caret.Offset = 0;
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 4, 2)!);
            Assert.Equal(3, (int?)Invoke(window, "LineActionSourceLine"));
            Invoke(window, "TimeA_Click", window, new RoutedEventArgs());
            var basis = Field<TimeAnchor>(window, "timeA");
            Assert.Equal(3, basis.SourceLineIndex);
            Assert.Equal(2, basis.HeaderLineIndex);
            Assert.Equal(Visibility.Visible, Control<Border>(window, "TimeResultPanel").Visibility);
            Assert.Contains("원본 4줄", Control<TextBlock>(window, "TimeSummary").Text);
            Assert.False(Control<Expander>(window, "AnalysisPanel").IsExpanded);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task OpenMenuTargetCannotMutateANewSessionAndClosingRestoresKeyboardTarget() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] first\n[000:00:02] [T2] old second\n");
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 2, 1)!);
            var menu = new ContextMenu();
            Invoke(window, "LineContext_Opened", menu, new RoutedEventArgs());
            await Load(window, "[000:00:03] [T3] new session\n");
            Assert.Null((int?)Invoke(window, "LineActionSourceLine"));
            Invoke(window, "TimeA_Click", window, new RoutedEventArgs());
            Assert.Null(Field<TimeAnchor?>(window, "timeA"));
            Invoke(window, "LineContext_Closed", menu, new RoutedEventArgs());
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal(0, (int?)Invoke(window, "LineActionSourceLine"));
            Invoke(window, "TimeA_Click", window, new RoutedEventArgs());
            Assert.Equal(0, Field<TimeAnchor>(window, "timeA").SourceLineIndex);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task InvalidTimeClickRetainsExistingAnchorAndResult() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] valid\n[bad:00:00] [T2] invalid\n");
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 1, 1)!);
            Invoke(window, "TimeA_Click", window, new RoutedEventArgs());
            var original = Field<TimeAnchor>(window, "timeA");
            string result = Control<TextBlock>(window, "TimeSummary").Text;
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 2, 1)!);
            Invoke(window, "TimeA_Click", window, new RoutedEventArgs());
            Assert.Same(original, Field<TimeAnchor>(window, "timeA"));
            Assert.Equal(result, Control<TextBlock>(window, "TimeSummary").Text);
            Assert.Contains("유지", Control<TextBlock>(window, "OperationStatus").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task HighlightPromptCancelAndConfirmAreAtomicWithSyntheticDialogInjection() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic first second\n");
            Assert.True((bool)Invoke(window, "ApplyHighlightRules", (object)new HighlightRuleDraft[] { new("first", 0) })!);
            var editor = Control<TextEditor>(window, "Editor");
            editor.Select(editor.Text.IndexOf("second", StringComparison.Ordinal), "second".Length);
            string? receivedSeed = null;
            Func<IReadOnlyList<HighlightRuleDraft>, string, IReadOnlyList<HighlightRuleDraft>?> cancel = (before, seed) =>
            {
                receivedSeed = seed; Assert.Single(before); Assert.Equal("first", before[0].Phrase);
                return null;
            };
            Set(window, "highlightPromptOverride", cancel); Invoke(window, "ShowHighlightPrompt");
            Assert.Equal("second", receivedSeed);
            Assert.Equal(new[] { "first" }, Keywords(window));
            Func<IReadOnlyList<HighlightRuleDraft>, string, IReadOnlyList<HighlightRuleDraft>?> accept = (_, _) =>
                new HighlightRuleDraft[] { new("second", 1), new("first", 3, false) };
            Set(window, "highlightPromptOverride", accept); Invoke(window, "ShowHighlightPrompt");
            Assert.Equal(new[] { "second", "first" }, Keywords(window));
            Assert.False(window.IsVisible);
            Assert.False(Control<Expander>(window, "AnalysisPanel").IsExpanded);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task InvalidHighlightDraftCannotReplaceExistingRules() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic\n");
            Assert.True((bool)Invoke(window, "ApplyHighlightRules", (object)new HighlightRuleDraft[] { new("synthetic", 0) })!);
            Assert.False((bool)Invoke(window, "ApplyHighlightRules", (object)Enumerable.Range(0, 9).Select(i => new HighlightRuleDraft("rule" + i, 0)).ToArray())!);
            Assert.False((bool)Invoke(window, "ApplyHighlightRules", (object)new HighlightRuleDraft[] { new("", 0) })!);
            Assert.False((bool)Invoke(window, "ApplyHighlightRules", (object)new HighlightRuleDraft[] { new("other", 6) })!);
            Assert.Equal(new[] { "synthetic" }, Keywords(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task GutterClickMapsItsVisibleRowAndBlankBelowDocumentHasNoTarget() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] first\n[000:00:02] [T2] second");
            var editor = Control<TextEditor>(window, "Editor");
            Layout(editor);
            var view = editor.TextArea.TextView;
            var last = view.VisualLines.Last();
            double rowY = last.VisualTop - view.VerticalOffset + last.Height / 2;
            var editorPoint = view.TranslatePoint(new Point(0, rowY), editor);
            editorPoint.X = 1;
            Assert.True((bool)Invoke(window, "CaptureLineActionTargetAtPoint", editorPoint)!);
            Assert.Equal(1, (int?)Invoke(window, "LineActionSourceLine"));
            var blankPoint = view.TranslatePoint(new Point(30, last.VisualTop - view.VerticalOffset + last.Height + 10), editor);
            Assert.False((bool)Invoke(window, "CaptureLineActionTargetAtPoint", blankPoint)!);
            Assert.Null((int?)Invoke(window, "LineActionSourceLine"));
            Assert.False((bool)Invoke(window, "CaptureLineActionTargetAtPoint", new Point(-1, rowY))!);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FilterProjectionChangeInvalidatesCapturedMenuLine() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] first\n[000:00:02] [T2] second\n");
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 2, 1)!);
            Func<ThreadItem, bool> onlyFirst = item => item.Id == 1;
            await (Task)Invoke(window, "SetThreadsAsync", onlyFirst)!;
            Assert.False((bool)Invoke(window, "IsLineActionTargetCurrent")!);
            Assert.Null((int?)Invoke(window, "LineActionSourceLine"));
            Invoke(window, "TimeA_Click", window, new RoutedEventArgs());
            Assert.Null(Field<TimeAnchor?>(window, "timeA"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task RightClickInsideSelectionPreservesPhraseAndOutsideUsesClickedWord() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] first second\n");
            var editor = Control<TextEditor>(window, "Editor");
            int first = editor.Text.IndexOf("first", StringComparison.Ordinal);
            editor.Select(first, "first".Length);
            string? received = null;
            Func<IReadOnlyList<HighlightRuleDraft>, string, IReadOnlyList<HighlightRuleDraft>?> inspect = (_, seed) => { received = seed; return null; };
            Set(window, "highlightPromptOverride", inspect);
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 1, first + 2)!);
            Assert.Equal("first", editor.SelectedText);
            editor.Select(first + 1, 0); // Simulate a native right-click handler changing selection after preview.
            Invoke(window, "LineContext_Opened", new ContextMenu(), new RoutedEventArgs());
            Assert.Equal("first", editor.SelectedText);
            Invoke(window, "ShowHighlightPrompt"); Assert.Equal("first", received);
            int second = editor.Text.IndexOf("second", StringComparison.Ordinal);
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 1, second + 1)!);
            Assert.Equal(0, editor.SelectionLength);
            Invoke(window, "ShowHighlightPrompt"); Assert.Equal("second", received);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task HighlightManagerWorksWithEmptyFilterWithoutChangingThatFilter() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] first second\n");
            Assert.True((bool)Invoke(window, "ApplyHighlightRules", (object)new HighlightRuleDraft[] { new("first", 0) })!);
            Func<ThreadItem, bool> none = _ => false;
            await (Task)Invoke(window, "SetThreadsAsync", none)!;
            var empty = Field<LogProjection>(window, "projection"); Assert.Equal(0, empty.Count);
            Func<IReadOnlyList<HighlightRuleDraft>, string, IReadOnlyList<HighlightRuleDraft>?> replace = (_, _) => new HighlightRuleDraft[] { new("second", 1) };
            Set(window, "highlightPromptOverride", replace); Invoke(window, "ShowHighlightPrompt");
            Assert.Equal(new[] { "second" }, Keywords(window));
            Assert.Same(empty, Field<LogProjection>(window, "projection"));
            Func<ThreadItem, bool> all = _ => true;
            await (Task)Invoke(window, "SetThreadsAsync", all)!;
            Assert.Equal(1, Field<LogProjection>(window, "projection").Count);
            Assert.Equal(new[] { "second" }, Keywords(window));
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task UnusedPointerCaptureReleasesAfterButtonUpWhileOpenMenuAndNewerCaptureRemainValid() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] first\n[000:00:02] [T2] second\n");
            Set(window, "lineActionRightButtonHeld", true);
            Assert.False((bool)Invoke(window, "CaptureLineActionTarget", 0, 1)!);
            Invoke(window, "ScheduleUnusedLineActionTargetCleanup");
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(Field<bool>(window, "lineActionPointerPending"));
            Set(window, "lineActionRightButtonHeld", false);
            Invoke(window, "ScheduleUnusedLineActionTargetCleanup");
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.False(Field<bool>(window, "lineActionPointerPending"));
            Assert.Equal(0, (int?)Invoke(window, "LineActionSourceLine"));

            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 1, 1)!);
            Invoke(window, "ScheduleUnusedLineActionTargetCleanup");
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 2, 1)!);
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(Field<bool>(window, "lineActionPointerPending")); // Earlier queued cleanup cannot erase the new capture.
            var menu = new ContextMenu(); Invoke(window, "LineContext_Opened", menu, new RoutedEventArgs());
            Control<TextEditor>(window, "Editor").TextArea.Caret.Offset = 0;
            Invoke(window, "ScheduleUnusedLineActionTargetCleanup");
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal(1, (int?)Invoke(window, "LineActionSourceLine"));
            Invoke(window, "LineContext_Closed", menu, new RoutedEventArgs());
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal(0, (int?)Invoke(window, "LineActionSourceLine"));

            // Releasing outside the editor can omit its mouse-up; the next key observes release.
            Set(window, "lineActionRightButtonHeld", true);
            Assert.False((bool)Invoke(window, "CaptureLineActionTarget", 0, 1)!);
            Invoke(window, "ReleaseUnusedLineActionCaptureForKeyboard", false);
            Assert.True(Field<bool>(window, "lineActionPointerPending"));
            Invoke(window, "ReleaseUnusedLineActionCaptureForKeyboard", true);
            Assert.False(Field<bool>(window, "lineActionPointerPending"));
            Assert.Equal(0, (int?)Invoke(window, "LineActionSourceLine"));
            Assert.False((bool)Invoke(window, "CaptureLineActionTarget", 0, 1)!);
            Invoke(window, "ReleaseUnusedLineActionCapture"); // Same cleanup used by a fresh left click.
            Assert.False(Field<bool>(window, "lineActionPointerPending"));
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    private static string[] Keywords(MainWindow window) => ((IEnumerable)Field<object>(window, "keywordRules"))
        .Cast<object>().Select(rule => (string)rule.GetType().GetProperty("Keyword")!.GetValue(rule)!).ToArray();
    private static Task Load(MainWindow window, string text)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic load", "synthetic failure")!;
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    private static object? Invoke(MainWindow window, string name, params object?[] values) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, values);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [action])!;
    private static void Layout(TextEditor editor)
    {
        editor.TextArea.TextView.SetValue(TextBlock.FontFamilyProperty, editor.FontFamily);
        editor.TextArea.TextView.SetValue(TextBlock.FontSizeProperty, editor.FontSize);
        editor.Measure(new Size(800, 400)); editor.Arrange(new Rect(0, 0, 800, 400)); editor.UpdateLayout();
        editor.TextArea.TextView.EnsureVisualLines();
    }
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-line-actions", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
