using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

/// <summary>Menu wiring and themed layout, using unshown WPF windows only.</summary>
public sealed class MenuUxTests
{
    [Fact]
    public Task MenusGroupActionsAndKeepEncodingInsideFileMenuWithShortcutHints() => InSta(() =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            var menu = Control<Menu>(window, "MainMenu");
            var groups = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(new[] { "파일(_F)", "편집(_E)", "보기(_V)", "분석(_A)", "도움말(_H)" }, groups.Select(g => g.Header));
            var reload = Control<MenuItem>(window, "ReloadButton");
            var encoding = groups[0].Items.OfType<MenuItem>().Single(i => i.Header.ToString() == "인코딩으로 다시 읽기");
            Assert.Contains(reload, encoding.Items.Cast<object>());
            Assert.Equal("한국어 (CP949)", reload.Header);
            Assert.DoesNotContain(reload, groups.Cast<object>());
            Assert.False(encoding.IsEnabled);
            typeof(MainWindow).GetField("requestedPath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, "synthetic.log");
            reload.IsEnabled = true;
            Invoke(window, "UpdateMenus");
            Assert.True(encoding.IsEnabled);
            Assert.True(reload.IsEnabled);
            Assert.Equal("Ctrl+O", groups[0].Items.OfType<MenuItem>().Single(i => i.Header.ToString() == "파일 열기…").InputGestureText);
            Assert.Equal("Ctrl+V", groups[1].Items.OfType<MenuItem>().Single(i => i.Header.ToString() == "붙여넣기").InputGestureText);
            Assert.Equal("Ctrl+F", Control<MenuItem>(window, "FindMenu").InputGestureText);
            Assert.Equal("Ctrl+Tab", Control<MenuItem>(window, "NextSessionMenu").InputGestureText);
            Assert.True(Control<TextEditor>(window, "Editor").IsReadOnly);
            Assert.Null(window.FindName("DraftEditor"));
            Assert.Equal(Visibility.Collapsed, Control<ComboBox>(window, "ThemeBox").Parent is UIElement panel ? panel.Visibility : Visibility.Visible);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task AppearanceMenuChangesReachEditorAndCheckedStateWithoutWritingSettings() => InSta(() =>
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Assert.True(store.Save(new UiSettings { FontSize = 18, WordWrap = true }).Success);
        byte[] original = File.ReadAllBytes(store.FilePath);
        var window = new MainWindow(folder.Path, false);
        try
        {
            var editor = Control<TextEditor>(window, "Editor");
            Invoke(window, "UpdateMenus");
            Assert.True(Control<MenuItem>(window, "WrapMenu").IsChecked);
            Assert.True(Appearance(window, "FontMenu", "font:18").IsChecked);

            ClickAppearance(window, "FontMenu", "font:24");
            Assert.Equal(24, editor.FontSize);
            Assert.True(Appearance(window, "FontMenu", "font:24").IsChecked);
            Assert.False(Appearance(window, "FontMenu", "font:18").IsChecked);
            ClickAppearance(window, "DensityMenu", "density:1");
            Assert.Equal(LogTypography.Create(true).Source, editor.FontFamily.Source);
            ClickAppearance(window, "ThemeMenu", "theme:1");
            Assert.Equal(Colors.White, ((SolidColorBrush)window.Resources["BackgroundBrush"]).Color);
            Control<MenuItem>(window, "WrapMenu").IsChecked = false;
            Invoke(window, "MenuWrap_Click", Control<MenuItem>(window, "WrapMenu"), new RoutedEventArgs());
            Assert.False(editor.WordWrap);
            Invoke(window, "ZoomReset_Click", window, new RoutedEventArgs());
            Assert.Equal(14, editor.FontSize);
            Invoke(window, "ZoomOut_Click", window, new RoutedEventArgs());
            Assert.Equal(13, editor.FontSize);
            Invoke(window, "SaveSettingsNow");
            Assert.Equal(original, File.ReadAllBytes(store.FilePath));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DropdownRowsDisplayMutedShortcutTextAndUseCurrentThemePopupSurface() => InSta(() =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            var item = Control<MenuItem>(window, "FindMenu");
            item.Measure(new Size(350, 40));
            item.Arrange(new Rect(0, 0, 350, 40));
            item.ApplyTemplate();
            var gesture = Assert.IsType<TextBlock>(item.Template.FindName("GestureText", item));
            Assert.Equal("Ctrl+F", gesture.Text);
            Assert.Equal(11, gesture.FontSize);
            Assert.Same(window.FindResource("MutedBrush"), gesture.Foreground);
            var popup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(item.Template.FindName("PART_Popup", item));
            var surface = Assert.IsType<Border>(popup.Child);
            Assert.Same(window.FindResource("PanelBrush"), surface.Background);
            ClickAppearance(window, "ThemeMenu", "theme:1");
            Assert.Same(window.FindResource("PanelBrush"), surface.Background);
            Assert.Same(window.FindResource("MutedBrush"), gesture.Foreground);
            Assert.False(popup.IsOpen);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private static void ClickAppearance(MainWindow window, string group, string tag) =>
        Invoke(window, "MenuAppearance_Click", Appearance(window, group, tag), new RoutedEventArgs());
    private static MenuItem Appearance(MainWindow window, string group, string tag) =>
        Control<MenuItem>(window, group).Items.OfType<MenuItem>().Single(i => Equals(i.Tag, tag));
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-menu-ux", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
