using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

/// <summary>Bookmark removal on a synthetic, unshown WPF window; no native input or clipboard access.</summary>
public sealed class BookmarkRemovalTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task MenusSidebarAndGutterHaveNoBookmarkControlsInEitherTheme(bool dark) => InSta(async () =>
    {
        using var folder = new Folder();
        var window = new MainWindow(folder.Path, false) { Width = 944, Height = 484 };
        try
        {
            const string text = "[000:00:01] [T1] synthetic target one\r\nbody\n[000:00:02] [T2] synthetic target two";
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) =>
                Task.FromResult(LogParser.ParsePastedText(text, token, progress));
            await (Task)Invoke(window, "LoadAsync", load, "synthetic bookmark removal", "synthetic failure")!;
            Control<ComboBox>(window, "ThemeBox").SelectedIndex = dark ? 0 : 1;
            Control<Expander>(window, "ContentFilterExpander").IsExpanded = true;
            Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
            Control<TextBox>(window, "SearchBox").Text = "target";
            await (Task)Invoke(window, "SearchAsync")!;
            var content = (FrameworkElement)window.Content;
            for (int pass = 0; pass < 4; pass++)
            {
                content.Measure(new Size(944, 452)); content.Arrange(new Rect(0, 0, 944, 452)); content.UpdateLayout();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            }

            Assert.Null(window.FindName("BookmarkPanel"));
            Assert.Null(window.FindName("BookmarkList"));
            Assert.Null(typeof(MainWindow).GetField("bookmarks", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.Null(typeof(OriginalLineMargin).GetProperty("Bookmarks"));
            Assert.DoesNotContain(typeof(MainWindow).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic), method => method.Name.Contains("Bookmark", StringComparison.Ordinal));
            var editor = Control<TextEditor>(window, "Editor");
            var menus = MenuItems(Control<Menu>(window, "MainMenu")).Concat(MenuItems(editor.ContextMenu)).ToArray();
            Assert.DoesNotContain(menus, item => (item.Header?.ToString() ?? "").Contains("북마크", StringComparison.Ordinal));
            Assert.DoesNotContain(menus, item => item.InputGestureText.Contains("F2", StringComparison.Ordinal));
            Assert.Contains(menus, item => item.InputGestureText == "F3");
            Assert.Contains(menus, item => item.InputGestureText == "F4");
            Assert.Contains(menus, item => item.InputGestureText == "Ctrl+G");
            Assert.True(editor.ActualHeight >= 120);
            Assert.True(Control<ListBox>(window, "ThreadList").ActualHeight >= 96);
            Assert.Equal(text, editor.Text);
            Assert.True(editor.IsReadOnly);
            Assert.False(window.IsVisible);
            SaveImage(content, dark);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(Key.F2, ModifierKeys.None, false, false)]
    [InlineData(Key.F2, ModifierKeys.Control, false, false)]
    [InlineData(Key.F2, ModifierKeys.Shift, false, false)]
    [InlineData(Key.F3, ModifierKeys.None, true, false)]
    [InlineData(Key.F3, ModifierKeys.Shift, true, true)]
    [InlineData(Key.F4, ModifierKeys.None, true, true)]
    public Task RemovedShortcutsStayUnhandledWhileSearchShortcutsKeepTheirDirection(Key key, ModifierKeys modifiers, bool handled, bool backwards) => InSta(() =>
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new SyntheticPresentationSource(), 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        object?[] arguments = [args, modifiers, false];
        bool result = (bool)typeof(MainWindow).GetMethod("TryHandleSearchNavigationShortcut", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments)!;
        Assert.Equal(handled, result);
        Assert.Equal(handled, args.Handled);
        Assert.Equal(backwards, (bool)arguments[2]!);
        return Task.CompletedTask;
    });

    private static IEnumerable<MenuItem> MenuItems(ItemsControl parent)
    {
        foreach (var item in parent.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in MenuItems(item)) yield return child;
        }
    }
    private static void SaveImage(FrameworkElement content, bool dark)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string directory = Path.Combine(root, "TestResults", "v0.8.1", "bookmark-removal"); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(944, 452, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, dark ? "small-window-dark.png" : "small-window-light.png")); encoder.Save(output);
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) => typeof(MainWindow)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class SyntheticPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = new Canvas();
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-bookmark-removal", Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
