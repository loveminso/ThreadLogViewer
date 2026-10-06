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

public sealed class ThreadAppearanceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StrongerThreadRowsRemainDistinctAndTextContrastStaysReadable(bool dark)
    {
        var theme = new WorkbenchTheme(dark);
        var background = ((SolidColorBrush)theme.Background).Color;
        var colors = Enumerable.Range(0, 12).Select(id => ((SolidColorBrush)theme.ThreadBackground(id)).Color).ToArray();
        Assert.Equal(12, colors.Distinct().Count());
        foreach (var color in colors.Append(((SolidColorBrush)theme.ThreadBackground(null)).Color))
        {
            int difference = Math.Abs(color.R - background.R) + Math.Abs(color.G - background.G) + Math.Abs(color.B - background.B);
            Assert.True(difference >= 35, $"Thread tint is too close to the page background: {difference}.");
            Assert.True(Contrast(((SolidColorBrush)theme.Text).Color, color) >= 4.5);
        }
    }

    [Fact]
    public Task DisablingThreadBackgroundKeepsMarkersContextAndActionLayersAndSourceText() => InSta(() =>
    {
        const string text = "[000:00:01] [T1] synthetic first\n[000:00:02] [T2] synthetic context\nsynthetic body\n[000:00:03] [T3] synthetic last";
        var source = LogParser.Parse(text, null, "synthetic UTF-8");
        var projection = LogProjection.Create(source, [1, 2, 3]);
        var editor = new TextEditor { Text = projection.Text, FontFamily = LogTypography.Create(false), FontSize = 14 };
        editor.TextArea.TextView.SetValue(TextBlock.FontFamilyProperty, editor.FontFamily);
        editor.TextArea.TextView.SetValue(TextBlock.FontSizeProperty, editor.FontSize);
        Layout(editor, 800, 400); Layout(editor.TextArea.TextView, 800, 400);
        editor.TextArea.TextView.EnsureVisualLines();
        var renderer = new ThreadBackgroundRenderer { Projection = projection, ContextEntryIndex = 1, ActionLineIndex = 2 };
        Assert.True(renderer.Enabled);
        var on = Draw(renderer, editor);
        renderer.Enabled = false;
        var off = Draw(renderer, editor);
        int lines = editor.TextArea.TextView.VisualLines.Count;
        Assert.Equal(lines, on.Length - off.Length);
        var rowBrushes = new[] { 1, 2, 3 }.Select(id => renderer.Theme.ThreadBackground(id)).ToArray();
        Assert.DoesNotContain(off, drawing => rowBrushes.Contains(drawing.Brush));
        Assert.Equal(on.Count(drawing => ReferenceEquals(drawing.Brush, renderer.Theme.Accent)),
            off.Count(drawing => ReferenceEquals(drawing.Brush, renderer.Theme.Accent)));
        Assert.Equal(on.Count(drawing => drawing.Pen is not null), off.Count(drawing => drawing.Pen is not null));
        Assert.Equal(text, source.Text);
        Assert.Equal(text, editor.Text);
        return Task.CompletedTask;
    });

    [Fact]
    public Task FindingThreadIdsAndUnclassifiedOnlyNarrowsListAndClearRestoresAllItems() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            const string text = "synthetic unclassified\n[000:00:01] [T101] synthetic first\n[000:00:02] [T204] synthetic next\n[000:00:03] [T309] synthetic third";
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress));
            await (Task)Invoke(window, "LoadAsync", load, "synthetic thread appearance", "synthetic failure")!;
            var threads = Field<List<ThreadItem>>(window, "threadItems");
            var selected = threads.Select(thread => thread.IsSelected).ToArray();
            var projection = Field<LogProjection>(window, "projection");
            var search = Control<TextBox>(window, "ThreadSearchBox");
            var list = Control<ListBox>(window, "ThreadList");
            search.Text = "T101";
            Assert.Equal(101, Assert.Single(list.Items.Cast<ThreadItem>()).Id);
            search.Text = "미분류";
            Assert.Null(Assert.Single(list.Items.Cast<ThreadItem>()).Id);
            search.Text = "synthetic missing thread";
            Assert.Empty(list.Items.Cast<ThreadItem>());
            Assert.Contains("일치하는 스레드", Control<TextBlock>(window, "ThreadSearchStatus").Text);
            Control<Button>(window, "ThreadSearchClearButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(threads.Count, list.Items.Count);
            Assert.Equal("", search.Text);
            Assert.Equal(selected, threads.Select(thread => thread.IsSelected));
            Assert.Same(projection, Field<LogProjection>(window, "projection"));
            Assert.Equal(text, Field<LogData>(window, "data").Text);
            Assert.True(Control<TextEditor>(window, "Editor").IsReadOnly);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    private static GeometryDrawing[] Draw(ThreadBackgroundRenderer renderer, TextEditor editor)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) renderer.Draw(editor.TextArea.TextView, drawing);
        return visual.Drawing.Children.OfType<GeometryDrawing>().ToArray();
    }
    private static void Layout(FrameworkElement element, double width, double height)
    { element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); }
    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value) { double v = value / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) => typeof(MainWindow)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [action])!;
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-thread-appearance", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
