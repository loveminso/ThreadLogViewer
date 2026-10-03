using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class ThemeContextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DisabledListKeepsThemeBackgroundAndReadableLabels(bool dark) => InSta(() =>
    {
        var theme = new WorkbenchTheme(dark);
        var styles = new ResourceDictionary { Source = new Uri("/ThreadLogViewer;component/Styles/Controls.xaml", UriKind.Relative) };
        theme.Apply(styles);
        var check = new CheckBox { Content = "Synthetic thread", IsChecked = true };
        var list = new ListBox { Width = 260, Height = 100, IsEnabled = false };
        list.Resources.MergedDictionaries.Add(styles);
        list.Items.Add(check);
        Layout(list, 260, 100);
        var surface = Assert.IsType<Border>(list.Template.FindName("Surface", list));
        Assert.Equal(((SolidColorBrush)theme.Panel).Color, ((SolidColorBrush)surface.Background).Color);
        Assert.Equal(((SolidColorBrush)theme.Text).Color, ((SolidColorBrush)list.Foreground).Color);
        Assert.Equal(((SolidColorBrush)theme.Text).Color, ((SolidColorBrush)check.Foreground).Color);
        Assert.Equal(1, list.Opacity);
        Assert.Equal(1, check.Opacity);
        var box = Assert.IsType<Border>(check.Template.FindName("Box", check));
        var glyph = Assert.IsType<TextBlock>(check.Template.FindName("Check", check));
        Assert.Equal(((SolidColorBrush)theme.Chrome).Color, ((SolidColorBrush)box.Background).Color);
        Assert.Equal(((SolidColorBrush)theme.Muted).Color, ((SolidColorBrush)glyph.Foreground).Color);
        Assert.Equal(Visibility.Visible, glyph.Visibility);

        // Transparent thread lists must also retain their explicitly requested background.
        list.Background = Brushes.Transparent;
        list.UpdateLayout();
        Assert.Same(Brushes.Transparent, surface.Background);
    });

    [Fact]
    public Task ThemedListRetainsVirtualizationDuringWorkLock() => InSta(() =>
    {
        var styles = new ResourceDictionary { Source = new Uri("/ThreadLogViewer;component/Styles/Controls.xaml", UriKind.Relative) };
        new WorkbenchTheme(true).Apply(styles);
        var list = new ListBox { Width = 260, Height = 180, IsEnabled = false, ItemsSource = Enumerable.Range(0, 10000) };
        list.Resources.MergedDictionaries.Add(styles);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        Layout(list, 260, 180);
        var host = FindChild<VirtualizingStackPanel>(list);
        Assert.NotNull(host);
        Assert.InRange(host.Children.Count, 1, 99);
    });

    [Fact]
    public Task ContextMarkerCoversReferenceHeaderAndBodyOnlyWithoutChangingSource() => InSta(() =>
    {
        const string text = "[000:00:01] [T1] synthetic first\n[000:00:02] [T2] synthetic reference\nbody one\nbody two\n[000:00:03] [T1] synthetic last";
        var source = LogParser.Parse(text, null, "synthetic UTF-8");
        var projection = LogProjection.Create(source, [1, 2]);
        var editor = new TextEditor { Text = projection.Text, FontFamily = LogTypography.Create(false), FontSize = 14 };
        editor.TextArea.TextView.SetValue(TextBlock.FontFamilyProperty, editor.FontFamily);
        editor.TextArea.TextView.SetValue(TextBlock.FontSizeProperty, editor.FontSize);
        Layout(editor, 800, 400);
        // The editor is unhosted, so establish the TextView's viewport explicitly.
        // Constructing individual visual lines alone does not populate its visible collection.
        Layout(editor.TextArea.TextView, 800, 400);
        editor.TextArea.TextView.EnsureVisualLines();
        Assert.True(editor.TextArea.TextView.VisualLinesValid);
        Assert.Equal(5, editor.TextArea.TextView.VisualLines.Count);
        var renderer = new ThreadBackgroundRenderer { Projection = projection, ContextEntryIndex = 1 };
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) renderer.Draw(editor.TextArea.TextView, drawing);
        var rectangles = visual.Drawing.Children.OfType<GeometryDrawing>().Where(x => x.Geometry is RectangleGeometry).ToArray();
        var accents = rectangles.Where(x => ReferenceEquals(x.Brush, renderer.Theme.Accent)).ToArray();
        Assert.Equal(3, accents.Length);
        Assert.All(accents, x => Assert.Equal(4, ((RectangleGeometry)x.Geometry).Rect.Width));
        var expected = editor.TextArea.TextView.VisualLines.Skip(1).Take(3).Select(x => x.VisualTop).ToArray();
        Assert.Equal(expected, accents.Select(x => ((RectangleGeometry)x.Geometry).Rect.Top));
        Assert.Equal(text, source.Text);
        Assert.Equal(text, editor.Text);

        renderer.ContextEntryIndex = null;
        using (var drawing = visual.RenderOpen()) renderer.Draw(editor.TextArea.TextView, drawing);
        Assert.DoesNotContain(visual.Drawing.Children.OfType<GeometryDrawing>(), x => ReferenceEquals(x.Brush, renderer.Theme.Accent));
    });

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static Task InSta(Action action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, [new Func<Task>(() => { action(); return Task.CompletedTask; })])!;
}
