using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ThreadLogViewer.App;

public sealed record HighlightRuleDraft(string Phrase, int ColorIndex, bool Enabled = true);

public static class HighlightPrompt
{
    public const int MaximumRules = 8;
    private static readonly string[] ColorNames = ["주황", "보라", "청록", "파랑", "분홍", "초록"];
    private static readonly string[] ColorValues = ["#F7A844", "#B397FF", "#4BD6CC", "#75AEFF", "#FF92BD", "#97D778"];

    public static bool IsValid(IReadOnlyList<HighlightRuleDraft> rules) => rules.Count <= MaximumRules
        && rules.All(rule => rule is { Phrase.Length: > 0, ColorIndex: >= 0 and < 6 });

    public static IReadOnlyList<HighlightRuleDraft>? Ask(Window owner, IReadOnlyList<HighlightRuleDraft> rules, string seed)
    {
        if (!owner.IsVisible || !IsValid(rules)) return null;
        var drafts = new ObservableCollection<Draft>(rules.Select(rule => new Draft(rule)));
        var dialog = new Window
        {
            Owner = owner, Title = "강조 문구 관리", Width = 580, Height = 470,
            MinWidth = 500, MinHeight = 390, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, ResizeMode = ResizeMode.CanResize, FontFamily = new FontFamily("Malgun Gothic"), FontSize = 13
        };
        dialog.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        dialog.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var root = new Grid { Margin = new Thickness(16) };
        for (int i = 0; i < 4; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var guide = new TextBlock { Text = "선택한 문구를 색으로 표시합니다. 최대 8개까지 추가하고, 체크로 켜거나 끌 수 있습니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        root.Children.Add(guide);
        var label = new TextBlock { Text = "강조할 문구", Margin = new Thickness(0, 0, 0, 5) };
        Grid.SetRow(label, 1); root.Children.Add(label);
        var inputRow = new Grid(); inputRow.ColumnDefinitions.Add(new ColumnDefinition());
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var input = new TextBox { Text = seed, MinWidth = 180, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var color = new ComboBox { ItemsSource = ColorNames, SelectedIndex = 0, Width = 78, Margin = new Thickness(0, 0, 8, 0) };
        var add = new Button { Content = "추가", MinWidth = 60 };
        inputRow.Children.Add(input); Grid.SetColumn(color, 1); inputRow.Children.Add(color); Grid.SetColumn(add, 2); inputRow.Children.Add(add);
        Grid.SetRow(inputRow, 2); root.Children.Add(inputRow);
        var status = new TextBlock { Text = "수정 사항은 ‘적용’을 누르면 반영됩니다.", Margin = new Thickness(0, 8, 0, 10), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(status, 3); root.Children.Add(status);
        var rows = new StackPanel();
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 4); root.Children.Add(scroll);
        void RebuildRows()
        {
            rows.Children.Clear();
            foreach (var draft in drafts)
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var remove = new Button { Content = "삭제", Tag = draft, MinWidth = 54 };
                remove.Click += (_, _) => { drafts.Remove(draft); RebuildRows(); };
                DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                var enabled = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                enabled.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(Draft.Enabled)) { Source = draft, Mode = BindingMode.TwoWay });
                DockPanel.SetDock(enabled, Dock.Left); row.Children.Add(enabled);
                var swatch = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorValues[draft.ColorIndex])), StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                swatch.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
                DockPanel.SetDock(swatch, Dock.Left); row.Children.Add(swatch);
                var sample = new TextBlock { Text = ColorNames[draft.ColorIndex] + " · " + draft.Phrase, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                sample.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                row.Children.Add(sample); rows.Children.Add(row);
            }
            if (drafts.Count == 0) rows.Children.Add(new TextBlock { Text = "등록된 강조 문구가 없습니다.", Margin = new Thickness(0, 8, 0, 0) });
            add.IsEnabled = input.Text.Length > 0 && drafts.Count < MaximumRules;
        }
        add.Click += (_, _) =>
        {
            if (input.Text.Length == 0 || drafts.Count >= MaximumRules) return;
            drafts.Add(new(new(input.Text, Math.Clamp(color.SelectedIndex, 0, 5))));
            input.Clear(); status.Text = $"{drafts.Count}/{MaximumRules}개 · ‘적용’을 누르면 반영됩니다."; RebuildRows(); input.Focus();
        };
        input.TextChanged += (_, _) => add.IsEnabled = input.Text.Length > 0 && drafts.Count < MaximumRules;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var accept = new Button { Content = "적용", IsDefault = true, MinWidth = 78 };
        var cancel = new Button { Content = "취소", IsCancel = true, MinWidth = 78, Margin = new Thickness(8, 0, 0, 0) };
        IReadOnlyList<HighlightRuleDraft>? result = null;
        accept.Click += (_, _) =>
        {
            if (input.Text.Length > 0)
            {
                if (drafts.Count >= MaximumRules) { status.Text = "최대 8개입니다. 기존 문구를 삭제하거나 입력 칸을 비운 뒤 적용하세요."; return; }
                drafts.Add(new(new(input.Text, Math.Clamp(color.SelectedIndex, 0, 5))));
            }
            result = Array.AsReadOnly(drafts.Select(draft => new HighlightRuleDraft(draft.Phrase, draft.ColorIndex, draft.Enabled)).ToArray());
            dialog.DialogResult = true;
        };
        footer.Children.Add(accept); footer.Children.Add(cancel); Grid.SetRow(footer, 5); root.Children.Add(footer);
        dialog.Content = root; RebuildRows();
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? result : null;
    }

    private sealed class Draft(HighlightRuleDraft source)
    {
        public string Phrase { get; } = source.Phrase;
        public int ColorIndex { get; } = source.ColorIndex;
        public bool Enabled { get; set; } = source.Enabled;
    }
}
