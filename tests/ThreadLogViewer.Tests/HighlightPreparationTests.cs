using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class HighlightPreparationTests
{
    [Fact]
    public Task MixedRulesApplyValidHighlightsAndNameTheSkippedRule() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic alpha-keep beta-keep");
            const string broken = "bad\uD800";
            Assert.True(Apply(window, new HighlightRuleDraft("alpha-keep", 0), new HighlightRuleDraft(broken, 1), new HighlightRuleDraft("beta-keep", 2)));
            await Refresh(window);

            Assert.Equal(new[] { "alpha-keep", "beta-keep" }, HighlightedPhrases(window));
            Assert.Equal(new[] { 0, 2 }, Field<KeywordHighlightRenderer>(window, "keywordRenderer").Index.Spans.Select(span => span.RuleIndex).ToArray());
            Assert.Equal(new[] { 0, 1, 2 }, Field<KeywordHighlightRenderer>(window, "keywordRenderer").RuleColors);
            Assert.Equal(new[] { "alpha-keep", broken, "beta-keep" }, RuleKeywords(window));

            string keyword = Control<System.Windows.Controls.TextBlock>(window, "KeywordStatus").Text;
            string operation = Control<System.Windows.Controls.TextBlock>(window, "OperationStatus").Text;
            Assert.Contains("3/8", keyword, StringComparison.Ordinal);
            Assert.Contains("2곳", keyword, StringComparison.Ordinal);
            Assert.Contains("강조 준비 실패", keyword, StringComparison.Ordinal);
            Assert.Contains("강조 준비 실패", operation, StringComparison.Ordinal);
            Assert.Contains("2번 보라 ‘bad’", operation, StringComparison.Ordinal);
            Assert.Contains("표시 2곳", operation, StringComparison.Ordinal);
            Assert.DoesNotContain("\uD800", operation, StringComparison.Ordinal);
            Assert.DoesNotContain("강조 준비 중", keyword, StringComparison.Ordinal);
            Assert.DoesNotContain("강조 준비 중", operation, StringComparison.Ordinal);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task AllInvalidRulesHighlightNothingAndNameEverySkippedRule() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic alpha-keep");
            const string first = "bad\uD800";
            const string second = "\uDFFFtail";
            Assert.True(Apply(window, new HighlightRuleDraft(first, 0), new HighlightRuleDraft(second, 3)));
            await Refresh(window);

            Assert.Empty(HighlightedPhrases(window));
            Assert.Equal(new[] { first, second }, RuleKeywords(window));
            string keyword = Control<System.Windows.Controls.TextBlock>(window, "KeywordStatus").Text;
            string operation = Control<System.Windows.Controls.TextBlock>(window, "OperationStatus").Text;
            Assert.Equal("강조 준비 실패", keyword);
            Assert.Contains("1번 주황 ‘bad’", operation, StringComparison.Ordinal);
            Assert.Contains("2번 파랑 ‘tail’", operation, StringComparison.Ordinal);
            Assert.Contains("규칙을 확인한 뒤 다시 시도하세요.", operation, StringComparison.Ordinal);
            Assert.DoesNotContain("곳", operation, StringComparison.Ordinal);
            Assert.DoesNotContain("\uD800", operation, StringComparison.Ordinal);
            Assert.DoesNotContain("\uDFFF", operation, StringComparison.Ordinal);
            Assert.DoesNotContain("강조 준비 중", operation, StringComparison.Ordinal);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task AllValidRulesHighlightWithoutAPrepareFailure() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "[000:00:01] [T1] synthetic alpha-keep \U00010400 beta-keep");
            Assert.True(Apply(window,
                new HighlightRuleDraft("alpha-keep", 0),
                new HighlightRuleDraft("\U00010400", 4),
                new HighlightRuleDraft("beta-keep", 2)));
            await Refresh(window);

            Assert.Equal(new[] { "alpha-keep", "\U00010400", "beta-keep" }, HighlightedPhrases(window));
            string keyword = Control<System.Windows.Controls.TextBlock>(window, "KeywordStatus").Text;
            string operation = Control<System.Windows.Controls.TextBlock>(window, "OperationStatus").Text;
            Assert.Contains("3/8", keyword, StringComparison.Ordinal);
            Assert.Contains("3곳", keyword, StringComparison.Ordinal);
            Assert.DoesNotContain("강조 준비 실패", keyword, StringComparison.Ordinal);
            Assert.DoesNotContain("강조 준비 실패", operation, StringComparison.Ordinal);
            Assert.DoesNotContain("강조 준비 중", keyword, StringComparison.Ordinal);
        }
        finally { window.Close(); }
    });

    private static string[] HighlightedPhrases(MainWindow window)
    {
        var renderer = Field<KeywordHighlightRenderer>(window, "keywordRenderer");
        string text = Field<LogProjection>(window, "projection").Text;
        return renderer.Index.Spans.Select(span => text.Substring(span.Offset, span.Length)).ToArray();
    }

    private static string[] RuleKeywords(MainWindow window) =>
        ((IEnumerable)Field<object>(window, "keywordRules")).Cast<object>()
            .Select(rule => (string)rule.GetType().GetProperty("Keyword")!.GetValue(rule)!)
            .ToArray();

    private static bool Apply(MainWindow window, params HighlightRuleDraft[] rules) =>
        (bool)Invoke(window, "ApplyHighlightRules", (object)rules)!;

    private static async Task Refresh(MainWindow window)
    {
        await (Task)Invoke(window, "RefreshKeywordsAsync")!;
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    private static Task Load(MainWindow window, string text)
    {
        Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load = (token, progress) =>
            Task.FromResult(LogParser.ParsePastedText(text, token, progress));
        return (Task)Invoke(window, "LoadAsync", load, "synthetic highlight load", "synthetic highlight failure")!;
    }

    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;

    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-highlight-prep", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
