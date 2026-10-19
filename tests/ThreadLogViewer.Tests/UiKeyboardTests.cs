using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

/// <summary>Synthetic logs only. Menu paste reads an injected data object and never the real clipboard.</summary>
public sealed class UiKeyboardTests
{
    private const string Log =
        "[000:00:01.000] [T1] synthetic target alpha\n" +
        "[000:00:02.000] [T1] synthetic other beta\n" +
        "[000:00:03.000] [T2] synthetic target gamma\n";

    [Fact]
    public void KeyboardAffordancesAreDescribedTheSameWayInXamlAndHelp()
    {
        string xaml = ReadRepoFile("src/ThreadLogViewer.App/MainWindow.xaml");
        Assert.Contains("PreviewKeyDown=\"ResultsList_PreviewKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MouseDoubleClick=\"ResultsList_MouseDoubleClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewKeyDown=\"FilterCriteria_PreviewKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Enter로 필터 적용", xaml, StringComparison.Ordinal);
        Assert.Contains("본문에서는 Ctrl+V와 같이 클립보드 로그 열기", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("로그 열기는 Ctrl+Shift+V", xaml, StringComparison.Ordinal);
        string help = (string)typeof(HelpWindow).GetField("Body", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Assert.Contains("편집 → 붙여넣기", help, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Enter", help, StringComparison.Ordinal);
        Assert.Contains("포커스는 목록에 남습니다", help, StringComparison.Ordinal);
    }

    [Fact]
    public Task ResultListSelectionMovesTheBodyWithoutFocusAndEnterOrDoubleClickRequestsIt() => InSta(async () =>
    {
        using var folder = new Folder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Log);
            Named<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Named<TextBox>(window, "SearchBox").Text = "target";
            await (Task)Invoke(window, "SearchAsync")!;
            var hits = Field<LocatedSearchHit[]>(window, "searchHits");
            Assert.Equal(2, hits.Length);
            var list = Named<ListBox>(window, "ResultsList");
            var editor = Named<TextEditor>(window, "Editor");
            Assert.Equal(0, FocusRequests(window));

            list.SelectedIndex = 0;
            await Until(() => editor.SelectedText == "target" && SourceLine(window) == hits[0].SourceLineIndex);
            int firstOffset = editor.SelectionStart;
            list.SelectedIndex = 1;
            await Until(() => editor.SelectionStart != firstOffset && editor.SelectedText == "target" && SourceLine(window) == hits[1].SourceLineIndex);
            await Idle(window);
            Assert.Equal(0, FocusRequests(window));

            Assert.Equal(ModifierKeys.None, Keyboard.Modifiers);
            using var keys = new HwndSource(new HwndSourceParameters("synthetic-key")
            {
                Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000)
            });
            var enter = new KeyEventArgs(Keyboard.PrimaryDevice, keys, 0, Key.Enter) { RoutedEvent = UIElement.PreviewKeyDownEvent };
            Invoke(window, "ResultsList_PreviewKeyDown", list, enter);
            Assert.True(enter.Handled);
            Assert.Equal(1, FocusRequests(window));

            var item = new ListBoxItem { DataContext = list.SelectedItem };
            var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Control.MouseDoubleClickEvent,
                Source = item
            };
            Invoke(window, "ResultsList_MouseDoubleClick", list, click);
            Assert.Equal(2, FocusRequests(window));

            await (Task)Invoke(window, "NavigateSearchAsync", false)!;
            Assert.Equal(3, FocusRequests(window));
            var searchEnter = new KeyEventArgs(Keyboard.PrimaryDevice, keys, 0, Key.Enter) { RoutedEvent = UIElement.KeyDownEvent };
            Invoke(window, "Search_KeyDown", Named<TextBox>(window, "SearchBox"), searchEnter);
            await Until(() => FocusRequests(window) >= 4);
            Assert.True(searchEnter.Handled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EditMenuPasteOnTheBodyOpensTheClipboardLogAndLeavesTheOriginalTab() => InSta(async () =>
    {
        using var folder = new Folder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Log);
            await Layout(window);
            var editor = Named<TextEditor>(window, "Editor");
            string original = editor.Text;
            int reads = 0;
            var data = new DataObject();
            data.SetText("[000:00:01.000] [T1] synthetic menu paste line\n");
            Set(window, "readClipboard", (Func<IDataObject?>)(() => { reads++; return data; }));
            var executed = new List<ICommand>();
            window.AddHandler(CommandManager.PreviewExecutedEvent, new ExecutedRoutedEventHandler((_, e) =>
            {
                executed.Add(e.Command);
                e.Handled = true;
            }));
            RaiseFocus(editor.TextArea, editor.TextArea);
            Invoke(window, "UpdateEditingMenus");
            var paste = Named<MenuItem>(window, "FocusPasteMenu");
            Assert.True(paste.IsEnabled);
            string tip = paste.ToolTip as string ?? "";
            Assert.Contains("클립보드 로그", tip, StringComparison.Ordinal);
            Assert.DoesNotContain("로그 열기는 Ctrl+Shift+V", tip, StringComparison.Ordinal);
            paste.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => SessionCount(window) == 2 && editor.Text.Contains("synthetic menu paste line", StringComparison.Ordinal));
            Assert.Equal(1, reads);
            Assert.DoesNotContain(ApplicationCommands.Paste, executed);
            Named<ListBox>(window, "SessionTabs").SelectedIndex = 0;
            await Until(() => editor.Text == original);
            Assert.Equal(original, editor.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EditMenuPasteOnAnInputBoxStaysATextPaste() => InSta(async () =>
    {
        using var folder = new Folder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Log);
            await Layout(window);
            Named<Expander>(window, "ContentFilterExpander").IsExpanded = true;
            int reads = 0;
            Set(window, "readClipboard", (Func<IDataObject?>)(() => { reads++; return null; }));
            var executed = new List<(ICommand Command, object Source)>();
            window.AddHandler(CommandManager.PreviewCanExecuteEvent, new CanExecuteRoutedEventHandler((_, e) =>
            { e.CanExecute = true; e.Handled = true; }));
            window.AddHandler(CommandManager.PreviewExecutedEvent, new ExecutedRoutedEventHandler((_, e) =>
            { executed.Add((e.Command, e.OriginalSource)); e.Handled = true; }));
            var input = Named<TextBox>(window, "IncludeBox");
            RaiseFocus(input, input);
            Invoke(window, "UpdateEditingMenus");
            Assert.True(Named<MenuItem>(window, "FocusPasteMenu").IsEnabled);
            Named<MenuItem>(window, "FocusPasteMenu").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(ApplicationCommands.Paste, Assert.Single(executed).Command);
            Assert.Same(input, executed[0].Source);
            Assert.Equal(0, reads);
            Assert.Equal(1, SessionCount(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CtrlEnterAppliesTheFilterAndPlainEnterDoesNot() => InSta(async () =>
    {
        using var folder = new Folder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Log);
            var include = Named<TextBox>(window, "IncludeBox");
            var exclude = Named<TextBox>(window, "ExcludeBox");
            Assert.True(include.AcceptsReturn);
            Assert.True(exclude.AcceptsReturn);
            include.Text = "target";
            Assert.False((bool)Invoke(window, "TryApplyFilterShortcut", Key.Enter, ModifierKeys.None)!);
            Assert.False((bool)Invoke(window, "TryApplyFilterShortcut", Key.Return, ModifierKeys.None)!);
            Assert.False((bool)Invoke(window, "TryApplyFilterShortcut", Key.Enter, ModifierKeys.Control | ModifierKeys.Shift)!);
            Assert.Equal("target", include.Text);
            Assert.Empty(Field<EntryFilter>(window, "appliedFilter").Includes);
            Assert.DoesNotContain("필터 적용 완료", Named<TextBlock>(window, "OperationStatus").Text, StringComparison.Ordinal);
            Assert.True((bool)Invoke(window, "TryApplyFilterShortcut", Key.Enter, ModifierKeys.Control)!);
            Assert.True((bool)Invoke(window, "TryApplyFilterShortcut", Key.Return, ModifierKeys.Control)!);
            await Until(() => Named<TextBlock>(window, "OperationStatus").Text.Contains("필터 적용 완료", StringComparison.Ordinal));
            Assert.Contains("target", Field<EntryFilter>(window, "appliedFilter").Includes);
            Assert.Equal("target", include.Text);
            Assert.Contains("target", editorText(window), StringComparison.Ordinal);
            Assert.DoesNotContain("other beta", editorText(window), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task MissingFilterExpansionStaysCollapsedAndASavedChoiceReopens() => InSta(() =>
    {
        using var folder = new Folder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "user-data"));
        File.WriteAllText(Path.Combine(folder.Path, "user-data", "ui-settings.json"), """
            {
              "Owner": "ThreadLogViewer.UiSettings",
              "Schema": 1,
              "Settings": { "Dark": false, "FontSize": 18 }
            }
            """);
        var legacy = new MainWindow(folder.Path, true);
        try
        {
            Assert.False(Named<Expander>(legacy, "ContentFilterExpander").IsExpanded);
            Assert.Equal(18, Named<TextEditor>(legacy, "Editor").FontSize);
            Assert.Equal(1, Named<ComboBox>(legacy, "ThemeBox").SelectedIndex);
            Assert.False(Field<bool>(legacy, "contentFilterExpandedPreference"));
        }
        finally { legacy.Close(); }

        var chosen = new MainWindow(folder.Path, true);
        try
        {
            var expander = Named<Expander>(chosen, "ContentFilterExpander");
            expander.IsExpanded = true;
            Assert.True(Field<bool>(chosen, "contentFilterExpandedPreference"));
            Invoke(chosen, "SaveSettingsNow");
            string json = File.ReadAllText(new SettingsStore(folder.Path).FilePath);
            Assert.Contains("\"Schema\": 1", json, StringComparison.Ordinal);
            Assert.Contains("\"ContentFilterExpanded\": true", json, StringComparison.Ordinal);
        }
        finally { chosen.Close(); }

        var restarted = new MainWindow(folder.Path, true);
        try
        {
            Assert.True(Named<Expander>(restarted, "ContentFilterExpander").IsExpanded);
            Assert.True(Field<bool>(restarted, "contentFilterExpandedPreference"));
        }
        finally { restarted.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task NewTabsFollowTheSavedExpansionAndExistingTabsKeepTheirOwn() => InSta(async () =>
    {
        using var folder = new Folder();
        var window = new MainWindow(folder.Path, true);
        try
        {
            var expander = Named<Expander>(window, "ContentFilterExpander");
            expander.IsExpanded = true;
            await Load(window, Log);
            Assert.True(expander.IsExpanded);
            Invoke(window, "CreateBlankSession");
            Assert.True(expander.IsExpanded);
            expander.IsExpanded = false;
            Assert.False(Field<bool>(window, "contentFilterExpandedPreference"));
            Invoke(window, "SaveSettingsNow");
            var tabs = Named<ListBox>(window, "SessionTabs");
            tabs.SelectedIndex = 0;
            Assert.True(expander.IsExpanded);
            Assert.False(Field<bool>(window, "contentFilterExpandedPreference"));
            Invoke(window, "CreateBlankSession");
            Assert.False(expander.IsExpanded);
            tabs.SelectedIndex = 0;
            Assert.True(expander.IsExpanded);
            tabs.SelectedIndex = 1;
            Assert.False(expander.IsExpanded);
            Assert.False(Field<bool>(window, "contentFilterExpandedPreference"));
        }
        finally { window.Close(); }

        var restarted = new MainWindow(folder.Path, true);
        try { Assert.False(Named<Expander>(restarted, "ContentFilterExpander").IsExpanded); }
        finally { restarted.Close(); }
    });

    private static string editorText(MainWindow window) => Named<TextEditor>(window, "Editor").Text;
    private static int FocusRequests(MainWindow window) => Field<int>(window, "editorFocusRequests");
    private static int? SourceLine(MainWindow window) => (int?)Invoke(window, "CurrentSourceLine");
    private static int SessionCount(MainWindow window) =>
        ((System.Collections.ICollection)typeof(MainWindow).GetField("sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Count;

    private static string ReadRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThreadLogViewer.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static Task Load(MainWindow window, string text)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) =>
            Task.FromResult(LogParser.ParsePastedText(text, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic load", "synthetic failure")!;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int pass = 0; pass < 400 && !condition(); pass++) await Task.Delay(10);
        Assert.True(condition());
    }

    private static Task Idle(MainWindow window) => window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle).Task;
    private static async Task Layout(MainWindow window)
    {
        window.Width = 1200;
        window.Height = 800;
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1200, 760));
        content.Arrange(new Rect(0, 0, 1200, 760));
        content.UpdateLayout();
        await Idle(window);
    }
    private static void RaiseFocus(UIElement source, IInputElement target) => source.RaiseEvent(
        new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, target) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
    private static T Named<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void Set(MainWindow window, string name, object? value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    private static object? Invoke(MainWindow window, string name, params object?[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;

    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-ui-keyboard", Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
