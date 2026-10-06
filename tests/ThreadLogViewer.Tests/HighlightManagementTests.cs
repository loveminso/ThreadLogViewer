using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ThreadLogViewer.App;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class HighlightManagementTests
{
    [Fact]
    public Task ApplyingDisabledRuleDoesNotReaddItsSeedAndCancelLeavesOriginalRulesUntouched() => InSta(() =>
    {
        var original = new[] { new HighlightRuleDraft("synthetic timeout", 0) };
        var editor = new HighlightManagementEditor(original, "synthetic timeout");
        IReadOnlyList<HighlightRuleDraft>? applied = null;
        bool cancelled = false;
        var view = HighlightPrompt.CreateView(editor, rules => applied = rules, () => cancelled = true);
        editor.Rules[0].Enabled = false;
        Click(view, "HighlightApplyButton");
        Assert.False(Assert.Single(applied!).Enabled);
        Assert.Equal("synthetic timeout", Named<TextBox>(view, "HighlightPendingBox").Text);
        Assert.True(original[0].Enabled);
        editor.Rules[0].Phrase = "synthetic changed";
        Click(view, "HighlightCancelButton");
        Assert.True(cancelled);
        Assert.Equal("synthetic timeout", original[0].Phrase);
        Assert.Equal("synthetic timeout", applied![0].Phrase);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DeletingRegisteredSeedThenApplyingLeavesNoRules() => InSta(() =>
    {
        var editor = new HighlightManagementEditor([new("synthetic timeout", 0)], "synthetic timeout");
        IReadOnlyList<HighlightRuleDraft>? applied = null;
        var view = HighlightPrompt.CreateView(editor, rules => applied = rules, () => { });
        var row = Assert.IsType<Grid>(Named<StackPanel>(view, "HighlightRows").Children[0]);
        row.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click(view, "HighlightApplyButton");
        Assert.Empty(applied!);
        Assert.Equal("synthetic timeout", editor.PendingPhrase);
        return Task.CompletedTask;
    });

    [Fact]
    public Task FullCapacityCanEditPhraseColorAndEnabledWithoutClearingNewInput() => InSta(async () =>
    {
        var initial = Enumerable.Range(0, 8).Select(i => new HighlightRuleDraft("synthetic rule" + i, i % 6)).ToArray();
        var editor = new HighlightManagementEditor(initial, "synthetic pending");
        IReadOnlyList<HighlightRuleDraft>? applied = null;
        var view = HighlightPrompt.CreateView(editor, rules => applied = rules, () => { });
        view.Measure(new Size(640, 450)); view.Arrange(new Rect(0, 0, 640, 450)); view.UpdateLayout();
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var row = Assert.IsType<Grid>(Named<StackPanel>(view, "HighlightRows").Children[0]);
        row.Children.OfType<TextBox>().Single().Text = "synthetic edited";
        row.Children.OfType<ComboBox>().Single().SelectedIndex = 4;
        row.Children.OfType<CheckBox>().Single().IsChecked = false;
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Assert.False(Named<Button>(view, "HighlightAddButton").IsEnabled);
        Click(view, "HighlightApplyButton");
        Assert.Equal(8, applied!.Count);
        Assert.Equal(new HighlightRuleDraft("synthetic edited", 4, false), applied[0]);
        Assert.Equal("synthetic pending", Named<TextBox>(view, "HighlightPendingBox").Text);
        Assert.Equal(new HighlightRuleDraft("synthetic rule0", 0), initial[0]);
    });

    [Fact]
    public Task AddButtonAloneRegistersPendingPhraseAndRejectsCaseInsensitiveDuplicates() => InSta(() =>
    {
        var editor = new HighlightManagementEditor([new("synthetic first", 0)], "synthetic second");
        IReadOnlyList<HighlightRuleDraft>? applied = null;
        var view = HighlightPrompt.CreateView(editor, rules => applied = rules, () => { });
        Click(view, "HighlightApplyButton");
        Assert.Single(applied!);
        Named<ComboBox>(view, "HighlightPendingColor").SelectedIndex = 3;
        Click(view, "HighlightAddButton");
        Assert.Equal(2, editor.Rules.Count);
        Assert.Equal("", Named<TextBox>(view, "HighlightPendingBox").Text);
        Named<TextBox>(view, "HighlightPendingBox").Text = "SYNTHETIC SECOND";
        Click(view, "HighlightAddButton");
        Assert.Equal(2, editor.Rules.Count);
        Assert.Contains("이미 등록", Named<TextBlock>(view, "HighlightStatus").Text);
        Click(view, "HighlightApplyButton");
        Assert.Equal(new HighlightRuleDraft("synthetic second", 3), applied![1]);
        return Task.CompletedTask;
    });

    [Fact]
    public Task InvalidEditedPhraseCannotPublishAndOverlongNewInputExplainsLimit() => InSta(() =>
    {
        var editor = new HighlightManagementEditor([new("synthetic valid", 0)], "");
        int publishes = 0;
        var view = HighlightPrompt.CreateView(editor, _ => publishes++, () => { });
        Named<TextBox>(view, "HighlightPendingBox").Text = new string('x', 4097);
        Assert.False(Named<Button>(view, "HighlightAddButton").IsEnabled);
        Assert.Contains("4,096", Named<TextBlock>(view, "HighlightStatus").Text);
        editor.Rules[0].Phrase = new string('y', 4097);
        Click(view, "HighlightApplyButton");
        Assert.Equal(0, publishes);
        Assert.Contains("4,096", Named<TextBlock>(view, "HighlightStatus").Text);
        editor.Rules[0].Phrase = "   ";
        Click(view, "HighlightApplyButton");
        Assert.Equal(0, publishes);
        Assert.Contains("공백", Named<TextBlock>(view, "HighlightStatus").Text);
        return Task.CompletedTask;
    });

    [Fact]
    public void RuleValidationRejectsOverCapacityInvalidColorsAndAccepts4096Characters()
    {
        Assert.True(HighlightPrompt.IsValid([new(new string('x', 4096), 5)]));
        Assert.False(HighlightPrompt.IsValid([new(new string('x', 4097), 0)]));
        Assert.False(HighlightPrompt.IsValid([new("synthetic", 6)]));
        var editor = new HighlightManagementEditor([], "synthetic");
        for (int i = 0; i < 9; i++) editor.Rules.Add(new(new("synthetic " + i, 0)));
        Assert.False(editor.TryApply(out _, out string message));
        Assert.Contains("8개", message);
    }

    private static T Named<T>(FrameworkElement view, string name) where T : class => (T)view.FindName(name);
    private static void Click(FrameworkElement view, string name) => Named<Button>(view, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [action])!;
}
