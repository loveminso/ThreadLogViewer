using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class WorkbenchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LogSearchSelectionAndControlsHaveReadableContrast(bool dark)
    {
        var theme = new WorkbenchTheme(dark);
        var surfaces = Enumerable.Range(0, 64).Select(id => theme.ThreadBackground(id))
            .Append(theme.ThreadBackground(null)).Append(theme.Background).Append(theme.Panel);
        foreach (var background in surfaces) Assert.True(Contrast(theme.Text, background) >= 4.5);
        Assert.True(Contrast(theme.Muted, theme.Panel) >= 4.5);
        Assert.True(Contrast(theme.Muted, theme.Chrome) >= 4.5);
        Assert.True(Contrast(theme.AccentText, theme.Accent) >= 4.5);
        Assert.True(Contrast(theme.SelectionText, theme.Selection) >= 4.5);
        Assert.True(Contrast(theme.Text, theme.Search) >= 4.5);
    }

    [Fact]
    public void DensityChangesActualAvalonLineHeightWithoutChangingTextOrSelection()
    {
        InSta(() =>
        {
            const string original = "[T3] 한글 synthetic\r\n\r\n[T7] other";
            var editor = new TextEditor { Text = original, FontSize = 14, FontFamily = LogTypography.Create(false) };
            var document = editor.Document;
            editor.Select(0, 16);
            void Layout()
            {
                // This unhosted editor has no Window to establish inherited font properties.
                editor.TextArea.TextView.SetValue(System.Windows.Controls.TextBlock.FontFamilyProperty, editor.FontFamily);
                editor.TextArea.TextView.SetValue(System.Windows.Controls.TextBlock.FontSizeProperty, editor.FontSize);
                editor.TextArea.TextView.Redraw();
                editor.Measure(new Size(800, 400));
                editor.Arrange(new Rect(0, 0, 800, 400));
                editor.UpdateLayout();
                editor.TextArea.TextView.EnsureVisualLines();
            }
            Layout();
            double normal = editor.TextArea.TextView.GetOrConstructVisualLine(document.GetLineByNumber(1)).Height;
            double normalBlank = editor.TextArea.TextView.GetOrConstructVisualLine(document.GetLineByNumber(2)).Height;
            editor.FontFamily = LogTypography.Create(true);
            Layout();
            double compact = editor.TextArea.TextView.GetOrConstructVisualLine(document.GetLineByNumber(1)).Height;
            double compactBlank = editor.TextArea.TextView.GetOrConstructVisualLine(document.GetLineByNumber(2)).Height;
            Assert.True(normal > compact + 2, $"Normal {normal}, compact {compact}");
            Assert.True(normalBlank > compactBlank + 2);
            Assert.Same(document, editor.Document);
            Assert.Equal(original, editor.Text);
            Assert.Equal(16, editor.SelectionLength);
            Assert.Equal(3, editor.Document.LineCount);
        });
    }

    private static double Contrast(Brush foreground, Brush background)
    {
        static double Luminance(Brush brush)
        {
            Color c = ((SolidColorBrush)brush).Color;
            static double Linear(byte v) { double s = v / 255d; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }
        double a = Luminance(foreground), b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA rendering test timed out");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
