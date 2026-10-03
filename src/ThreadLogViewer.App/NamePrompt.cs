using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ThreadLogViewer.App;

internal static class NamePrompt
{
    public static string? Ask(Window owner, string title, string initial)
    {
        var input = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 12), MaxLength = 120 };
        var ok = new Button { Content = "확인", IsDefault = true, MinWidth = 70 };
        var cancel = new Button { Content = "취소", IsCancel = true, MinWidth = 70 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var content = new StackPanel { Margin = new Thickness(16) }; content.Children.Add(input); content.Children.Add(buttons);
        var window = new Window { Owner = owner, Title = title, Content = content, Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text : null;
    }
}
