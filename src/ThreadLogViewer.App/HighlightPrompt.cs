using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ThreadLogViewer.App;

public sealed record HighlightRuleDraft(string Phrase, int ColorIndex, bool Enabled = true);

/// <summary>Dialog-local edits. Applying never consumes the separate new-phrase input.</summary>
public sealed class HighlightManagementEditor
{
    public HighlightManagementEditor(IReadOnlyList<HighlightRuleDraft> rules, string seed)
    {
        if (!HighlightPrompt.IsValid(rules)) throw new ArgumentException("유효하지 않은 강조 규칙입니다.", nameof(rules));
        Rules = new(rules.Select(rule => new EditableHighlightRule(rule)));
        PendingPhrase = seed;
    }
    public ObservableCollection<EditableHighlightRule> Rules { get; }
    public string PendingPhrase { get; set; }
    public int PendingColorIndex { get; set; }
    public bool TryAdd(out string message)
    {
        if (Rules.Count >= HighlightPrompt.MaximumRules) { message = "최대 8개입니다. 기존 문구를 수정하거나 삭제하세요."; return false; }
        if (HighlightPrompt.PhraseError(PendingPhrase) is { } error) { message = error; return false; }
        if (PendingColorIndex is < 0 or >= 6) { message = "강조 색을 선택하세요."; return false; }
        if (Rules.Any(rule => string.Equals(rule.Phrase, PendingPhrase, StringComparison.OrdinalIgnoreCase)))
        { message = "이미 등록된 문구입니다. 기존 행에서 문구·색·사용 여부를 수정하세요."; return false; }
        Rules.Add(new(new(PendingPhrase, PendingColorIndex)));
        PendingPhrase = "";
        message = $"{Rules.Count}/8개 · 수정 사항은 ‘적용’을 누르면 반영됩니다.";
        return true;
    }
    public bool TryApply(out IReadOnlyList<HighlightRuleDraft> result, out string message)
    {
        var snapshot = Rules.Select(rule => new HighlightRuleDraft(rule.Phrase, rule.ColorIndex, rule.Enabled)).ToArray();
        result = Array.AsReadOnly(snapshot);
        if (snapshot.Length > HighlightPrompt.MaximumRules) { message = "강조 문구는 최대 8개입니다."; return false; }
        foreach (var rule in snapshot)
        {
            if (HighlightPrompt.PhraseError(rule.Phrase) is { } error) { message = error; return false; }
            if (rule.ColorIndex is < 0 or >= 6) { message = "각 규칙의 강조 색을 선택하세요."; return false; }
        }
        message = "등록된 규칙의 변경 사항을 적용했습니다.";
        return true;
    }
}

public sealed class EditableHighlightRule(HighlightRuleDraft source) : INotifyPropertyChanged
{
    private string phrase = source.Phrase;
    private int colorIndex = source.ColorIndex;
    private bool enabled = source.Enabled;
    public string Phrase { get => phrase; set => Set(ref phrase, value); }
    public int ColorIndex { get => colorIndex; set => Set(ref colorIndex, value); }
    public bool Enabled { get => enabled; set => Set(ref enabled, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; PropertyChanged?.Invoke(this, new(name));
    }
}

public static class HighlightPrompt
{
    public const int MaximumRules = 8, MaximumPhraseLength = 4096;
    private static readonly string[] ColorNames = ["주황", "보라", "청록", "파랑", "분홍", "초록"];
    public static string? PhraseError(string? phrase) => string.IsNullOrWhiteSpace(phrase) ? "강조할 문구를 입력하세요. 공백만으로는 등록할 수 없습니다." :
        phrase.Length > MaximumPhraseLength ? "강조할 문구는 4,096자 이내로 입력하세요." : null;
    public static bool IsValid(IReadOnlyList<HighlightRuleDraft> rules) => rules.Count <= MaximumRules &&
        rules.All(rule => rule is { ColorIndex: >= 0 and < 6 } && PhraseError(rule.Phrase) is null);
    public static IReadOnlyList<HighlightRuleDraft>? Ask(Window owner, IReadOnlyList<HighlightRuleDraft> rules, string seed)
    {
        if (!owner.IsVisible || !IsValid(rules)) return null;
        var editor = new HighlightManagementEditor(rules, seed);
        var dialog = new Window
        {
            Owner = owner, Title = "강조 문구 관리", Width = 650, Height = 500,
            MinWidth = 530, MinHeight = 390, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, ResizeMode = ResizeMode.CanResize, FontFamily = new FontFamily("Segoe UI, Malgun Gothic"), FontSize = 13
        };
        dialog.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        dialog.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        IReadOnlyList<HighlightRuleDraft>? result = null;
        var view = CreateView(editor, applied => { result = applied; dialog.DialogResult = true; }, () => dialog.DialogResult = false);
        dialog.Content = view;
        dialog.Loaded += (_, _) => { var input = (TextBox)view.FindName("HighlightPendingBox"); input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? result : null;
    }

    /// <summary>Builds the same dialog content without showing a window, for synthetic UI checks.</summary>
    public static Grid CreateView(HighlightManagementEditor editor, Action<IReadOnlyList<HighlightRuleDraft>> apply, Action cancel)
    {
        var root = new Grid { Margin = new Thickness(16), DataContext = editor };
        NameScope.SetNameScope(root, new NameScope());
        for (int i = 0; i < 4; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = "새 문구는 ‘추가’로 등록합니다. 기존 문구·색·사용 여부를 행에서 수정한 뒤 ‘적용’을 누르세요. 최대 8개, 문구당 4,096자입니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var label = new TextBlock { Text = "새 강조 문구", Margin = new Thickness(0, 0, 0, 5) };
        Grid.SetRow(label, 1); root.Children.Add(label);
        var inputRow = new Grid(); inputRow.ColumnDefinitions.Add(new ColumnDefinition());
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var input = new TextBox { Text = editor.PendingPhrase, MinWidth = 150, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(input, "새 강조 문구");
        var color = new ComboBox { ItemsSource = ColorNames, SelectedIndex = editor.PendingColorIndex, Width = 78, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(color, "새 문구 강조 색상");
        var add = new Button { Content = "추가", MinWidth = 60 };
        inputRow.Children.Add(input); Grid.SetColumn(color, 1); inputRow.Children.Add(color); Grid.SetColumn(add, 2); inputRow.Children.Add(add);
        Grid.SetRow(inputRow, 2); root.Children.Add(inputRow);
        var status = new TextBlock { Text = "입력칸의 문구는 ‘추가’를 눌러야 등록됩니다. ‘적용’은 등록된 규칙만 반영합니다.", Margin = new Thickness(0, 8, 0, 10), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(status, 3); root.Children.Add(status);
        var rows = new StackPanel();
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 4); root.Children.Add(scroll);
        void UpdateAddState() => add.IsEnabled = PhraseError(input.Text) is null && editor.Rules.Count < MaximumRules;
        void RebuildRows()
        {
            rows.Children.Clear();
            foreach (var draft in editor.Rules)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3), DataContext = draft };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var enabled = new CheckBox { Content = "사용", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                enabled.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(EditableHighlightRule.Enabled)) { Mode = BindingMode.TwoWay });
                var phrase = new TextBox { Margin = new Thickness(0, 0, 8, 0), MinWidth = 80 };
                phrase.SetBinding(TextBox.TextProperty, new Binding(nameof(EditableHighlightRule.Phrase)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                AutomationProperties.SetName(phrase, "등록된 강조 문구 편집");
                var ruleColor = new ComboBox { ItemsSource = ColorNames, Width = 78, Margin = new Thickness(0, 0, 8, 0) };
                ruleColor.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty, new Binding(nameof(EditableHighlightRule.ColorIndex)) { Mode = BindingMode.TwoWay });
                AutomationProperties.SetName(ruleColor, "등록된 문구 강조 색상");
                var remove = new Button { Content = "삭제", MinWidth = 54 };
                remove.Click += (_, _) => { editor.Rules.Remove(draft); RebuildRows(); status.Text = $"{editor.Rules.Count}/8개 · 삭제는 ‘적용’을 누르면 반영됩니다."; };
                row.Children.Add(enabled); Grid.SetColumn(phrase, 1); row.Children.Add(phrase);
                Grid.SetColumn(ruleColor, 2); row.Children.Add(ruleColor); Grid.SetColumn(remove, 3); row.Children.Add(remove); rows.Children.Add(row);
            }
            if (editor.Rules.Count == 0) rows.Children.Add(new TextBlock { Text = "등록된 강조 문구가 없습니다.", Margin = new Thickness(0, 8, 0, 0) });
            UpdateAddState();
        }
        add.Click += (_, _) =>
        {
            editor.PendingPhrase = input.Text; editor.PendingColorIndex = color.SelectedIndex;
            if (!editor.TryAdd(out string message)) { status.Text = message; return; }
            input.Clear(); status.Text = message; RebuildRows(); input.Focus();
        };
        input.TextChanged += (_, _) =>
        {
            editor.PendingPhrase = input.Text;
            UpdateAddState();
            status.Text = input.Text.Length > 0 && PhraseError(input.Text) is { } error ? error :
                editor.Rules.Count >= MaximumRules ? "8/8개 · 새 문구를 추가하려면 기존 행을 삭제하세요. 기존 문구·색·사용 여부는 그대로 수정할 수 있습니다." :
                "입력칸의 문구는 ‘추가’를 눌러야 등록됩니다. ‘적용’은 등록된 규칙만 반영합니다.";
        };
        color.SelectionChanged += (_, _) => editor.PendingColorIndex = color.SelectedIndex;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var accept = new Button { Content = "적용", IsDefault = true, MinWidth = 78 };
        var reject = new Button { Content = "취소", IsCancel = true, MinWidth = 78, Margin = new Thickness(8, 0, 0, 0) };
        accept.Click += (_, _) => { if (editor.TryApply(out var applied, out string message)) apply(applied); else status.Text = message; };
        reject.Click += (_, _) => cancel();
        footer.Children.Add(accept); footer.Children.Add(reject); Grid.SetRow(footer, 5); root.Children.Add(footer);
        root.RegisterName("HighlightPendingBox", input); root.RegisterName("HighlightPendingColor", color);
        root.RegisterName("HighlightAddButton", add); root.RegisterName("HighlightApplyButton", accept);
        root.RegisterName("HighlightCancelButton", reject); root.RegisterName("HighlightRows", rows); root.RegisterName("HighlightStatus", status);
        RebuildRows();
        if (input.Text.Length > 0 && PhraseError(input.Text) is { } seedError) status.Text = seedError;
        else if (editor.Rules.Count >= MaximumRules) status.Text = "8/8개 · 새 문구를 추가하려면 기존 행을 삭제하세요. 기존 문구·색·사용 여부는 그대로 수정할 수 있습니다.";
        return root;
    }
}
