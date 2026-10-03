using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class QuickHighlightTests
{
    [Theory]
    [InlineData("status=OK,", 2, "status")]
    [InlineData("status=OK,", 7, "OK")]
    [InlineData("status=OK,", 9, "OK")]
    [InlineData("one.two", 4, "two")]
    [InlineData("[한글_123]", 4, "한글_123")]
    [InlineData("cafe\u0301", 4, "cafe\u0301")]
    [InlineData("\U00010400value", 1, "\U00010400value")]
    [InlineData("/abc/", 0, "")]
    [InlineData("left   right", 6, "")]
    [InlineData("left", 4, "left")]
    [InlineData("left", -1, "")]
    [InlineData("left", 5, "")]
    [InlineData("", 0, "")]
    [InlineData("\uD800x", 0, "")]
    public void CaretWordsUseUnicodeBoundariesAndExcludeSurroundingPunctuation(string text, int offset, string expected)
    {
        Assert.Equal(expected, Extract(text, offset));
    }

    [Fact]
    public void OverlongWordsAreRejectedWholeRatherThanTruncated()
    {
        string text = new('x', 4097);
        Assert.Equal("", Extract(text, 0));
        Assert.Equal("", Extract(text, 4097));
        Assert.Equal(new string('x', 4096), Extract(new string('x', 4096), 4096));
    }

    [Fact]
    public Task SelectedExactPhraseTogglesWithoutADialogEvenWhenCaretIsOutsideSelection() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            const string source = "[000:00:01] [T1] synthetic  exact phrase\n";
            await Load(window, source);
            var editor = Control<TextEditor>(window, "Editor");
            const string phrase = "synthetic  exact phrase";
            int start = editor.Text.IndexOf(phrase, StringComparison.Ordinal);
            editor.Select(start, phrase.Length); editor.TextArea.Caret.Offset = 0;
            RejectPrompt(window);
            Assert.True(Toggle(window));
            var rule = Assert.Single(Rules(window));
            Assert.Equal(phrase, rule.Phrase); Assert.Equal(0, rule.ColorIndex);
            Assert.Equal(phrase, editor.SelectedText);
            Assert.Equal(0, editor.TextArea.Caret.Offset);
            Assert.True(Toggle(window));
            Assert.Empty(Rules(window));
            Assert.Equal(source, Field<LogData>(window, "data").Text);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SamePhraseRemovesCaseVariantsIncludingDisabledRules() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic\n");
            Apply(window, [new("SYNTHETIC", 2, false), new("Synthetic", 3), new("other", 1)]);
            Select(window, "synthetic"); RejectPrompt(window);
            Assert.True(Toggle(window));
            Assert.Equal("other", Assert.Single(Rules(window)).Phrase);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task NewRulesUseDifferentFreeColorsAndReuseAColorAfterRemoval() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] alpha beta gamma delta epsilon zeta eta theta\n");
            foreach (string phrase in new[] { "alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta" })
            { Select(window, phrase); Assert.True(Toggle(window)); }
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 0, 1 }, Rules(window).Select(rule => rule.ColorIndex));
            Select(window, "gamma"); Assert.True(Toggle(window));
            Select(window, "gamma"); Assert.True(Toggle(window));
            Assert.Equal(2, Rules(window).Last().ColorIndex);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FullRulesRejectANewPhraseButAllowExistingRuleRemoval() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] rule0 newphrase\n");
            Apply(window, Enumerable.Range(0, 8).Select(i => new HighlightRuleDraft("rule" + i, i % 6)).ToArray());
            Select(window, "newphrase"); RejectPrompt(window);
            Assert.False(Toggle(window)); Assert.Equal(8, Rules(window).Length);
            Assert.Contains("최대 8개", Control<TextBlock>(window, "OperationStatus").Text);
            Select(window, "rule0"); Assert.True(Toggle(window)); Assert.Equal(7, Rules(window).Length);
            Select(window, "newphrase"); Assert.True(Toggle(window)); Assert.Equal(8, Rules(window).Length);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task OverlongSelectionIsRejectedWithoutFallingBackToItsCaretWord() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            string phrase = new('x', 4097);
            await Load(window, "[000:00:01] [T1] " + phrase + "\n");
            Select(window, phrase); RejectPrompt(window);
            Assert.False(Toggle(window)); Assert.Empty(Rules(window));
            Assert.Contains("4,096", Control<TextBlock>(window, "OperationStatus").Text);
            Assert.Equal(phrase, Control<TextEditor>(window, "Editor").SelectedText);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task WhitespaceOnlySelectionDoesNotCreateANewlineHighlight() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] alpha\r\n \r\n");
            Select(window, " \r\n"); RejectPrompt(window);
            Assert.False(Toggle(window)); Assert.Empty(Rules(window));
            Assert.Equal(" \r\n", Control<TextEditor>(window, "Editor").SelectedText);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ProjectionChangeRejectsOldContextTargetBeforeHighlightingNewView() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] alpha\n[000:00:02] [T2] beta\n");
            Select(window, "beta");
            var editor = Control<TextEditor>(window, "Editor");
            Assert.True((bool)Invoke(window, "CaptureLineActionTarget", 2, editor.TextArea.Caret.Column - 1)!);
            Func<ThreadItem, bool> onlyFirst = item => item.Id == 1;
            await (Task)Invoke(window, "SetThreadsAsync", onlyFirst)!;
            RejectPrompt(window);
            Assert.False(Toggle(window)); Assert.Empty(Rules(window));
            Assert.Contains("줄 대상", Control<TextBlock>(window, "OperationStatus").Text);
        }
        finally { window.Close(); }
    });

    private static string Extract(string text, int offset) => (string)typeof(MainWindow)
        .GetMethod("ExtractHighlightWord", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [text, offset])!;
    private static bool Toggle(MainWindow window) => (bool)Invoke(window, "ToggleHighlightAtSelection")!;
    private static void Apply(MainWindow window, HighlightRuleDraft[] rules) => Assert.True((bool)Invoke(window, "ApplyHighlightRules", (object)rules)!);
    private static HighlightRuleDraft[] Rules(MainWindow window) => ((IEnumerable)Field<object>(window, "keywordRules"))
        .Cast<object>().Select(rule => new HighlightRuleDraft(
            (string)rule.GetType().GetProperty("Keyword")!.GetValue(rule)!,
            (int)rule.GetType().GetProperty("ColorIndex")!.GetValue(rule)!,
            (bool)rule.GetType().GetProperty("Enabled")!.GetValue(rule)!)).ToArray();
    private static void Select(MainWindow window, string phrase)
    {
        var editor = Control<TextEditor>(window, "Editor");
        int offset = editor.Text.IndexOf(phrase, StringComparison.Ordinal); Assert.True(offset >= 0);
        editor.Select(offset, phrase.Length);
    }
    private static void RejectPrompt(MainWindow window)
    {
        Func<IReadOnlyList<HighlightRuleDraft>, string, IReadOnlyList<HighlightRuleDraft>?> reject = (_, _) => throw new InvalidOperationException("Quick highlight must not open a dialog.");
        typeof(MainWindow).GetField("highlightPromptOverride", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, reject);
    }
    private static Task Load(MainWindow window, string text)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic load", "synthetic failure")!;
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] values) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, values);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [action])!;
    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-quick-highlight", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
