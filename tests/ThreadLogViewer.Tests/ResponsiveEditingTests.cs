using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class ResponsiveEditingTests
{
    [Theory]
    [InlineData(1920, 1040, 96)]
    [InlineData(1920, 1000, 192)]
    [InlineData(1366, 728, 120)]
    [InlineData(1280, 680, 144)]
    public void WindowBoundsFitPhysicalWorkAreaAtItsDpi(double pixelsWide, double pixelsHigh, double dpi)
    {
        var limits = WindowLayoutPolicy.Calculate(pixelsWide, pixelsHigh, dpi, dpi);
        Assert.InRange(limits.Width * dpi / 96, 1, pixelsWide);
        Assert.InRange(limits.Height * dpi / 96, 1, pixelsHigh);
        Assert.InRange(limits.MinWidth, 1, limits.Width);
        Assert.InRange(limits.MinHeight, 1, limits.Height);
        Assert.InRange(limits.MaxWidth * dpi / 96, 1, pixelsWide);
        Assert.InRange(limits.MaxHeight * dpi / 96, 1, pixelsHigh);
        var larger = WindowLayoutPolicy.Calculate(pixelsWide, pixelsHigh, dpi, dpi, 5000, 5000);
        Assert.InRange(larger.Width, larger.MinWidth, larger.MaxWidth);
        Assert.InRange(larger.Height, larger.MinHeight, larger.MaxHeight);
    }

    [Fact]
    public void TinyWorkAreaAndInvalidBoundsHaveDeterministicLimits()
    {
        var limits = WindowLayoutPolicy.Calculate(600, 360, 192, 192);
        Assert.Equal(300, limits.MinWidth); Assert.Equal(180, limits.MinHeight);
        Assert.Equal(limits.MinWidth, limits.Width); Assert.Equal(limits.MinHeight, limits.Height);
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowLayoutPolicy.Calculate(0, 600));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowLayoutPolicy.Calculate(1000, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowLayoutPolicy.Calculate(1000, 600, 0));
    }

    [Theory]
    [InlineData(1040, 600, true)]
    [InlineData(1040, 600, false)]
    [InlineData(944, 484, true)]
    [InlineData(944, 484, false)]
    public Task ExpandedFiltersAndSearchKeepBodyAndThreadControlsInsideSmallClientArea(int width, int height, bool dark) => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window);
            Control<ComboBox>(window, "ThemeBox").SelectedIndex = dark ? 0 : 1;
            Control<Expander>(window, "ContentFilterExpander").IsExpanded = true;
            var editor = Control<TextEditor>(window, "Editor");
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "target";
            await (Task)Invoke(window, "SearchAsync")!;
            // Simulate a client rectangle smaller than the outer bounds by a title-bar allowance.
            int clientHeight = height - 32;
            await Layout(window, width, height, clientHeight);
            var filters = Control<DockPanel>(window, "FilterPanel");
            var list = Control<ListBox>(window, "ThreadList");
            var settings = Control<ScrollViewer>(window, "SidebarSettingsScroll");
            var controls = Control<ScrollViewer>(window, "LogControlsScroll");
            var grid = Control<Grid>(window, "LogContentGrid");
            var results = Control<Border>(window, "ResultsPanel");
            Assert.True(list.ActualHeight >= 96, $"Thread list height: {list.ActualHeight:0.##}.");
            Assert.True(editor.ActualHeight >= 120, $"Editor height: {editor.ActualHeight:0.##}.");
            AssertContained(Bounds(list, filters), new Rect(filters.RenderSize));
            Assert.True(Bounds(settings, filters).Bottom <= Bounds(list, filters).Top + 1);
            Assert.True(Bounds(controls, grid).Bottom <= Bounds(editor, grid).Top + 1);
            Assert.True(Bounds(editor, grid).Bottom <= Bounds(results, grid).Top + 1);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
            Assert.NotSame(settings, VisualTreeHelper.GetParent(list));
            Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(0));

            // A real routed row action remains usable while content filters are expanded.
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0)!;
            var only = Descendants<Button>(row).Single(button => Equals(button.Content, "단독"));
            Assert.True(only.IsEnabled);
            AssertContained(Bounds(only, list), new Rect(list.RenderSize));
            only.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !Field<bool>(window, "busy"));
            Assert.Single(Field<LogProjection>(window, "projection").SelectedThreads);

            // Widening the sidebar and showing an error must not collapse the search input.
            Control<ColumnDefinition>(window, "ThreadColumn").Width = new GridLength(600);
            Control<CheckBox>(window, "SearchRegexBox").IsChecked = true;
            Control<TextBox>(window, "SearchBox").Text = "[";
            await (Task)Invoke(window, "SearchAsync")!;
            await Layout(window, width, height, clientHeight);
            var search = Control<TextBox>(window, "SearchBox");
            Assert.Contains("정규식", Control<TextBlock>(window, "SearchStatus").Text);
            Assert.True(search.ActualWidth >= 120, $"Search box width: {search.ActualWidth:0.##}.");
            AssertContained(Bounds(search, controls), new Rect(controls.RenderSize));
            Assert.True(editor.ActualHeight >= 120);
            settings.ScrollToEnd(); await Layout(window, width, height, clientHeight);
            Assert.InRange(settings.VerticalOffset, 0, settings.ScrollableHeight);
            AssertContained(Rect.Intersect(Bounds(Control<Expander>(window, "ContentFilterExpander"), settings), new Rect(settings.RenderSize)), new Rect(settings.RenderSize));
            SaveImage(window, width, clientHeight, $"responsive-{width}x{height}-{(dark ? "dark" : "light")}.png");
            Assert.Equal(SyntheticText, Field<LogData>(window, "data").Text);
            Assert.True(editor.IsReadOnly);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EditingMenuRoutesCanExecuteAndExecutionToLastInputWithoutClipboardAccess() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window);
            Control<Expander>(window, "ContentFilterExpander").IsExpanded = true;
            // AvalonEdit's TextArea joins the window's routed-event tree only after its template is laid out.
            await Layout(window, 1040, 600, 600);
            var input = Control<TextBox>(window, "IncludeBox");
            var menu = Control<MenuItem>(window, "FocusCopyMenu");
            var queried = new List<(ICommand Command, object Source)>();
            var executed = new List<(ICommand Command, object Source)>();
            bool allow = true;
            window.AddHandler(CommandManager.PreviewCanExecuteEvent, new CanExecuteRoutedEventHandler((_, e) =>
            { queried.Add((e.Command, e.OriginalSource)); e.CanExecute = allow; e.Handled = true; }));
            window.AddHandler(CommandManager.PreviewExecutedEvent, new ExecutedRoutedEventHandler((_, e) =>
            { executed.Add((e.Command, e.OriginalSource)); e.Handled = true; }));
            RaiseFocus(input, input);
            Assert.Same(input, Field<IInputElement>(window, "lastEditingTarget"));
            RaiseFocus(menu, menu);
            Invoke(window, "UpdateEditingMenus");
            Assert.True(Control<MenuItem>(window, "FocusPasteMenu").IsEnabled);
            foreach (string name in new[] { "FocusCopyMenu", "FocusPasteMenu", "FocusSelectAllMenu" })
                Control<MenuItem>(window, name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(3, executed.Count);
            Assert.All(executed, call => Assert.Same(input, call.Source));
            Assert.Equal(new ICommand[] { ApplicationCommands.Copy, ApplicationCommands.Paste, ApplicationCommands.SelectAll }, executed.Select(call => call.Command));
            Assert.All(queried, call => Assert.Same(input, call.Source));
            allow = false; Invoke(window, "UpdateEditingMenus");
            Assert.False(Control<MenuItem>(window, "FocusCopyMenu").IsEnabled);
            Assert.False(Control<MenuItem>(window, "FocusPasteMenu").IsEnabled);
            Control<MenuItem>(window, "FocusPasteMenu").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(3, executed.Count);
            allow = true;
            var editor = Control<TextEditor>(window, "Editor");
            Assert.Same(window, Window.GetWindow(editor.TextArea));
            RaiseFocus(editor.TextArea, editor.TextArea);
            Assert.Same(editor.TextArea, Field<IInputElement>(window, "lastEditingTarget"));
            Invoke(window, "UpdateEditingMenus");
            Assert.False(Control<MenuItem>(window, "FocusPasteMenu").IsEnabled);
            Control<MenuItem>(window, "FocusCopyMenu").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Same(editor.TextArea, executed.Last().Source);
            Assert.Equal(SyntheticText, Field<LogData>(window, "data").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task StandardSelectAllTargetsTextboxThenReadOnlyDocumentWithoutClipboardAccess() => InSta(async () =>
    {
        using var folder = new Folder(); var window = new MainWindow(folder.Path, false);
        try
        {
            window.AddHandler(CommandManager.PreviewCanExecuteEvent, new CanExecuteRoutedEventHandler((_, e) =>
            { if (e.Command == ApplicationCommands.Paste) { e.CanExecute = false; e.Handled = true; } }));
            await Load(window);
            Control<Expander>(window, "ContentFilterExpander").IsExpanded = true;
            await Layout(window, 1040, 600, 600);
            var input = Control<TextBox>(window, "IncludeBox"); input.Text = "synthetic filter draft";
            RaiseFocus(input, input);
            Assert.Same(input, Field<IInputElement>(window, "lastEditingTarget"));
            Control<MenuItem>(window, "FocusSelectAllMenu").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(input.Text, input.SelectedText);
            var editor = Control<TextEditor>(window, "Editor");
            Assert.Same(window, Window.GetWindow(editor.TextArea));
            RaiseFocus(editor.TextArea, editor.TextArea);
            Assert.Same(editor.TextArea, Field<IInputElement>(window, "lastEditingTarget"));
            Control<MenuItem>(window, "FocusSelectAllMenu").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(SyntheticText, editor.SelectedText);
            Assert.Equal("synthetic filter draft", input.Text);
            Assert.True(editor.IsReadOnly);
        }
        finally { window.Close(); }
    });

    private static readonly string SyntheticText = string.Join("\n", Enumerable.Range(0, 100).Select(index => $"[000:00:01] [T{index % 16 + 1}] synthetic target {index}"));
    private static Task Load(MainWindow window)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) => Task.FromResult(LogParser.ParsePastedText(SyntheticText, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic responsive layout", "synthetic failure")!;
    }
    private static void RaiseFocus(UIElement source, IInputElement target) => source.RaiseEvent(
        new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, target) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static Rect Bounds(FrameworkElement element, Visual ancestor) => element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
    private static void AssertContained(Rect child, Rect viewport) => Assert.True(child.Width > 0 && child.Height > 0 &&
        child.Left >= viewport.Left - 1 && child.Top >= viewport.Top - 1 && child.Right <= viewport.Right + 1 && child.Bottom <= viewport.Bottom + 1,
        $"Child={child}; viewport={viewport}.");
    private static async Task Until(Func<bool> condition)
    { for (int pass = 0; pass < 200 && !condition(); pass++) await Task.Delay(10); Assert.True(condition()); }
    private static async Task Layout(MainWindow window, double width, double height, double clientHeight)
    {
        window.Width = width; window.Height = height;
        var content = (FrameworkElement)window.Content;
        for (int pass = 0; pass < 4; pass++)
        {
            content.Measure(new Size(width, clientHeight)); content.Arrange(new Rect(0, 0, width, clientHeight)); content.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
    }
    private static void SaveImage(MainWindow window, int width, int height, string name)
    {
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string directory = System.IO.Path.Combine(root, "TestResults", "v0.8.1", "responsive-layout"); Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render((FrameworkElement)window.Content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(System.IO.Path.Combine(directory, name)); encoder.Save(output);
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] args) => typeof(MainWindow)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-responsive-editing", Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
