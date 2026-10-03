using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

/// <summary>Actual WPF wiring on a background STA dispatcher. Windows are never shown or activated.</summary>
public sealed class WindowFeatureTests
{
    [Fact]
    public Task SuccessfulDocumentReplacementClearsSessionStateWhileCancelledLoadKeepsIt() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            const string text = "[000:00:01] [T1] synthetic first\r\nbody\r\n[000:00:02] [T2] synthetic second";
            await Load(window, text);
            var editor = Control<TextEditor>(window, "Editor");
            editor.TextArea.Caret.Line = 2;
            Invoke(window, "BookmarkToggle_Click", window, new RoutedEventArgs());
            Invoke(window, "SetTimePoint", true);
            Set(window, "fixedSearchRanges", new SourceTextRange[] { new(0, 5) });
            var bookmarks = Field<BookmarkState>(window, "bookmarks");
            var previousData = Field<LogData>(window, "data");
            var previousProjection = Field<LogProjection>(window, "projection");
            var previousTime = Field<TimeAnchor>(window, "timeA");
            var previousSelection = Field<IReadOnlyList<SourceTextRange>>(window, "fixedSearchRanges");
            Assert.Single(bookmarks.Items);
            Assert.Equal(1, previousTime.SourceLineIndex);

            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> cancel = (_, _) =>
                Task.FromCanceled<LogData>(new CancellationToken(canceled: true));
            await (Task)Invoke(window, "LoadAsync", cancel, "synthetic cancelled load", "synthetic failure")!;
            Assert.Same(previousData, Field<LogData>(window, "data"));
            Assert.Same(previousProjection, Field<LogProjection>(window, "projection"));
            Assert.Same(previousTime, Field<TimeAnchor>(window, "timeA"));
            Assert.Same(previousSelection, Field<IReadOnlyList<SourceTextRange>>(window, "fixedSearchRanges"));
            Assert.Single(bookmarks.Items);
            Assert.Equal(text, editor.Text);

            await Load(window, "[000:00:03] [T7] synthetic replacement");
            Assert.Empty(bookmarks.Items);
            Assert.Null(Field<object?>(window, "timeA"));
            Assert.Null(Field<object?>(window, "timeB"));
            Assert.Null(Field<object?>(window, "fixedSearchRanges"));
            Assert.NotSame(previousData, Field<LogData>(window, "data"));
            Assert.Contains("synthetic replacement", editor.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FilterPreservesOriginalCaretAndRestoresEmptyAnchorWithForwardNearestTie() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic first\n[000:00:02] [T2] synthetic middle\n[000:00:03] [T1] synthetic last\n");
            var editor = Control<TextEditor>(window, "Editor");
            Layout(editor);
            editor.TextArea.Caret.Line = 2;
            editor.TextArea.Caret.Column = 5;
            await SetThreads(window, t => t.Id == 1);
            Assert.Equal(2, CurrentSourceLine(window));
            Assert.Equal(2, editor.TextArea.Caret.Line);
            Assert.Equal(5, editor.TextArea.Caret.Column);

            await SetThreads(window, _ => false);
            Assert.Equal(0, Field<LogProjection>(window, "projection").Count);
            Assert.NotNull(Field<object?>(window, "emptyAnchor"));

            await SetThreads(window, _ => true);
            Assert.Equal(2, CurrentSourceLine(window));
            Assert.Equal(3, editor.TextArea.Caret.Line);
            Assert.Equal(5, editor.TextArea.Caret.Column);
            Assert.Null(Field<object?>(window, "emptyAnchor"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task RegexLfInsideCrLfKeepsExactSourceOffsetAndUsesNativeNewlineSelectionBoundary() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            const string text = "[000:00:01] [T1] synthetic first\r\nbody\r\n[000:00:02] [T2] synthetic second";
            await Load(window, text);
            var editor = Control<TextEditor>(window, "Editor");
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<CheckBox>(window, "SearchRegexBox").IsChecked = true;
            Control<TextBox>(window, "SearchBox").Text = "\\n";
            await (Task)Invoke(window, "SearchAsync")!;
            var hits = Field<LocatedSearchHit[]>(window, "searchHits");
            Assert.Equal(2, hits.Length);
            Assert.Equal(text.IndexOf('\n'), hits[0].SourceOffset);
            Assert.Equal(1, hits[0].Length);
            var renderer = Field<SearchHighlightRenderer>(window, "searchRenderer");
            Assert.Equal(new HighlightSpan(hits[0].SourceOffset, 1, 0), renderer.Index.Spans[0]);
            editor.TextArea.Selection = Selection.Create(editor.TextArea, hits[0].SourceOffset, hits[0].SourceOffset + 1);
            int nativeStart = editor.SelectionStart;
            int nativeLength = editor.SelectionLength;
            await (Task)Invoke(window, "NavigateHitAsync", hits[0])!;
            Assert.Equal(nativeStart, editor.SelectionStart);
            Assert.Equal(nativeLength, editor.SelectionLength);
            Assert.Equal(hits[0].SourceOffset - 1, editor.SelectionStart);
            Assert.Equal(2, editor.SelectionLength);
            Assert.Equal("\r\n", editor.SelectedText);
            Assert.Contains("개행 경계", Control<TextBlock>(window, "SearchStatus").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task InitialOwnedSettingsReachActualEditorThemeAndLayoutWithoutSaving() => InSta(() =>
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        var settings = new UiSettings { Dark = false, Compact = true, WordWrap = true, FontSize = 18, PanelWidth = 420 };
        Assert.True(store.Save(settings).Success);
        byte[] original = File.ReadAllBytes(store.FilePath);
        var window = new MainWindow(folder.Path, false);
        try
        {
            var editor = Control<TextEditor>(window, "Editor");
            Assert.Equal(18, editor.FontSize);
            Assert.True(editor.WordWrap);
            Assert.Equal(LogTypography.Create(true).Source, editor.FontFamily.Source);
            Assert.Equal(1, Control<ComboBox>(window, "ThemeBox").SelectedIndex);
            Assert.False(Field<WorkbenchTheme>(window, "theme").IsDark);
            Assert.Equal(420, Control<ColumnDefinition>(window, "ThreadColumn").Width.Value);
            Assert.Equal(Colors.White, ((SolidColorBrush)window.Resources["BackgroundBrush"]).Color);
            Control<ComboBox>(window, "FontSizeBox").SelectedIndex = 6;
            Invoke(window, "SaveSettingsNow");
            Assert.Equal(original, File.ReadAllBytes(store.FilePath));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task MinimumWindowKeepsLogAreaWhenAllToolsEightRulesAndSearchResultsAreExpanded() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false) { Width = 1040, Height = 600 };
        try
        {
            await Load(window, "[123456789:00:00.1234567] [T1] synthetic first second third fourth fifth sixth seventh eighth\n");
            Control<Expander>(window, "AnalysisPanel").IsExpanded = true;
            Control<Border>(window, "GoToPanel").Visibility = Visibility.Visible;
            Control<Border>(window, "ContextPanel").Visibility = Visibility.Visible;
            Control<TextBlock>(window, "ContextStatus").Text = "synthetic context showing all threads";
            Invoke(window, "SetTimePoint", true);
            Invoke(window, "SetTimePoint", false);
            foreach (string keyword in new[] { "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth" })
            {
                Control<TextBox>(window, "KeywordBox").Text = keyword;
                Invoke(window, "AddKeyword_Click", window, new RoutedEventArgs());
            }
            await (Task)Invoke(window, "RefreshKeywordsAsync")!;
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "synthetic";
            await (Task)Invoke(window, "SearchAsync")!;
            Invoke(window, "UpdateLayoutLimits");
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1040, 600));
            content.Arrange(new Rect(0, 0, 1040, 600));
            content.UpdateLayout();
            Assert.Equal(8, Control<ListBox>(window, "KeywordList").Items.Count);
            Assert.Equal(Visibility.Visible, Control<Border>(window, "ResultsPanel").Visibility);
            double logHeight = Control<TextEditor>(window, "Editor").ActualHeight;
            Assert.True(logHeight > 70, $"Minimum window left only {logHeight:0.##} pixels for the log.");
            Assert.True(Control<ScrollViewer>(window, "ToolsScroll").ActualHeight <= 130);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FullSourceNextResultContinuesAcrossHiddenContextTransitions() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            string text = string.Join('\n', Enumerable.Range(0, 40).Select(i =>
                $"[000:00:{i:00}] [T{(i % 2 == 0 ? 1 : 2)}] synthetic {(i is 1 or 35 ? "error" : "normal")}"));
            await Load(window, text);
            await SetThreads(window, t => t.Id == 1);
            Control<TextBox>(window, "ContextRadiusBox").Text = "0";
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<ComboBox>(window, "SearchScopeBox").SelectedIndex = 1;
            Control<TextBox>(window, "SearchBox").Text = "error";
            await (Task)Invoke(window, "SearchAsync")!;
            Assert.Equal(2, Field<LocatedSearchHit[]>(window, "searchHits").Length);
            await (Task)Invoke(window, "NavigateSearchAsync", false)!;
            Assert.Equal(1, CurrentSourceLine(window));
            Assert.True(Field<bool>(window, "contextActive"));
            await (Task)Invoke(window, "SearchAsync")!;
            await (Task)Invoke(window, "NavigateSearchAsync", false)!;
            Assert.Equal(35, CurrentSourceLine(window));
            Assert.Equal(new[] { 35 }, Field<LogProjection>(window, "projection").SourceIndexes);
            await (Task)Invoke(window, "SearchAsync")!;
            await (Task)Invoke(window, "NavigateSearchAsync", false)!;
            Assert.Equal(1, CurrentSourceLine(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ZeroLengthRegexResultsNavigateWithoutDrawingRectangularHighlights() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic first\n[000:00:02] [T2] synthetic second");
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<CheckBox>(window, "SearchRegexBox").IsChecked = true;
            Control<TextBox>(window, "SearchBox").Text = "(?=synthetic)";
            await (Task)Invoke(window, "SearchAsync")!;
            var hits = Field<LocatedSearchHit[]>(window, "searchHits");
            Assert.Equal(2, hits.Length);
            Assert.All(hits, hit => Assert.Equal(0, hit.Length));
            Assert.Equal(0, Field<SearchHighlightRenderer>(window, "searchRenderer").Index.Count);
            await (Task)Invoke(window, "NavigateSearchAsync", false)!;
            var editor = Control<TextEditor>(window, "Editor");
            Assert.Equal(hits[0].SourceOffset, editor.TextArea.Caret.Offset);
            Assert.Equal(0, editor.SelectionLength);
            await (Task)Invoke(window, "NavigateSearchAsync", false)!;
            Assert.Equal(hits[1].SourceOffset, editor.TextArea.Caret.Offset);
        }
        finally { window.Close(); }
    });

    private static Task Load(MainWindow window, string text)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) =>
            Task.FromResult(LogParser.ParsePastedText(text, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic load", "synthetic failure")!;
    }

    private static Task SetThreads(MainWindow window, Func<ThreadItem, bool> predicate) =>
        (Task)Invoke(window, "SetThreadsAsync", predicate)!;
    private static int? CurrentSourceLine(MainWindow window) => (int?)Invoke(window, "CurrentSourceLine");
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void Set(MainWindow window, string name, object value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);

    private static void Layout(TextEditor editor)
    {
        editor.TextArea.TextView.SetValue(TextBlock.FontFamilyProperty, editor.FontFamily);
        editor.TextArea.TextView.SetValue(TextBlock.FontSizeProperty, editor.FontSize);
        editor.Measure(new Size(800, 400));
        editor.Arrange(new Rect(0, 0, 800, 400));
        editor.UpdateLayout();
        editor.TextArea.TextView.EnsureVisualLines();
    }

    private static readonly Lazy<Dispatcher> UiDispatcher = new(CreateDispatcher);
    private static Dispatcher CreateDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/ThreadLogViewer;component/Styles/Controls.xaml", UriKind.Relative)
                });
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true, Name = "Synthetic unhosted WPF test dispatcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
    }

    private static Task InSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UiDispatcher.Value.BeginInvoke(new Action(async () =>
        {
            try { await action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        }));
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-window-features", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
