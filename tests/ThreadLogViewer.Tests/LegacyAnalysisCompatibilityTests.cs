using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class LegacyAnalysisCompatibilityTests
{
    private const string Text = "[000:00:01] [T1] synthetic alpha\r\nsynthetic body\n[000:00:02] [T2] synthetic beta\r[000:00:03] [T1] synthetic alpha finish";

    [Theory]
    [InlineData("[{\"Line\":1,\"Label\":\"synthetic checkpoint\"}]")]
    [InlineData("null")]
    [InlineData("\"obsolete content\"")]
    public async Task SchemaOneIgnoresRemovedFieldsAndNewSaveOmitsThem(string legacyBookmarks)
    {
        using var folder = new Folder();
        var source = LogParser.ParsePastedText(Text);
        var expected = new SavedAnalysis
        {
            Source = AnalysisFiles.Stamp(source), PastedText = Text, Title = "synthetic legacy analysis",
            Preset = new() { Filter = new(["alpha"], ["heartbeat"], true, true), Highlights = [new("alpha", 4, false)] },
            Query = "synthetic query", Position = new(3, 2, 1, 0, 10), TimeA = 1, TimeB = 3, AnalysisTool = 1,
            Back = [new(new(1, 1, 1, 0, 0), null)]
        };
        string legacyPath = folder.File("legacy.json"), newPath = folder.File("new.json");
        string originalJson = LegacyJson(expected, legacyBookmarks);
        await File.WriteAllTextAsync(legacyPath, originalJson);
        byte[] originalBytes = await File.ReadAllBytesAsync(legacyPath);

        var actual = await AnalysisFiles.ReadAnalysisAsync(legacyPath);
        Assert.Equal(expected.Source, actual.Source); Assert.Equal(Text, actual.PastedText);
        Assert.Equal(expected.Title, actual.Title); Assert.Equal(expected.Query, actual.Query);
        Assert.Equal(expected.Preset.Filter.Includes, actual.Preset.Filter.Includes);
        Assert.Equal(expected.Preset.Filter.Excludes, actual.Preset.Filter.Excludes);
        Assert.Equal(expected.Preset.Highlights, actual.Preset.Highlights);
        Assert.Equal(expected.Position, actual.Position); Assert.Equal(expected.TimeA, actual.TimeA); Assert.Equal(expected.TimeB, actual.TimeB);
        Assert.Equal(expected.AnalysisTool, actual.AnalysisTool);
        Assert.Equal(expected.Back, actual.Back);

        await AnalysisFiles.SaveAnalysisAsync(actual, newPath);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(legacyPath));
        AssertNoRemovedFields(await File.ReadAllTextAsync(newPath));
        Assert.Null(typeof(SavedAnalysis).GetProperty("Bookmarks"));
        Assert.Null(typeof(SavedAnalysis).GetProperty("BookmarksExpanded"));
    }

    [Fact]
    public Task LegacyFileRestoresScopedAnalysisAndLeavesSourceAndSavedJsonUnchanged() => InSta(async () =>
    {
        using var folder = new Folder();
        string sourcePath = folder.File("synthetic-source.log"), legacyPath = folder.File("legacy.json"), newPath = folder.File("new.json");
        await File.WriteAllTextAsync(sourcePath, Text);
        var source = await LogFileReader.ReadAsync(sourcePath);
        int offset = Text.LastIndexOf("alpha", StringComparison.Ordinal);
        // CapturePosition saves the caret at the selection's endpoint, not before the selected text.
        int selectionEndColumn = offset - source.GetLineOffset(3) + 6;
        var expected = new SavedAnalysis
        {
            Source = AnalysisFiles.Stamp(source), SourcePath = sourcePath, Title = "synthetic scoped analysis", SourceTitle = "synthetic source",
            Scope = new(1, 3), Preset = new() { Filter = new(["alpha"], ["heartbeat"], true, true), AllThreads = false, Threads = [1], Highlights = [new("alpha", 3)] },
            ContextLine = 2, ContextRadius = 1, NormalPosition = new(1, 1, 1, 0, 0), Position = new(3, selectionEndColumn, 1, 0, 0),
            Selection = [new(offset, 5)], TimeA = 1, TimeB = 3, SeparationStart = 1,
            Query = "alpha", SearchCase = true, SearchWord = true, SearchScope = 2, SearchVisible = true, SearchRanges = [new(offset, 5)],
            LastHit = new(offset, 5, 3, offset - source.GetLineOffset(3) + 1, source.Lines[3].EntryIndex),
            IncludesDraft = "synthetic unapplied include", ExcludesDraft = "synthetic unapplied exclude", RequireAllDraft = true, FilterCaseDraft = true,
            KeywordDraft = "synthetic keyword draft", KeywordColor = 2, FilterExpanded = true, AnalysisTool = 1,
            Back = [new(new(1, 1, 1, 0, 0), null)], Forward = [new(new(3, 1, 1, 0, 0), 3)]
        };
        await File.WriteAllTextAsync(legacyPath, LegacyJson(expected, "[{\"Line\":1,\"Label\":\"synthetic obsolete marker\"}]"));
        byte[] sourceBytes = await File.ReadAllBytesAsync(sourcePath), legacyBytes = await File.ReadAllBytesAsync(legacyPath);
        var window = new MainWindow(folder.Path, false);
        // Unhosted windows share a null PresentationSource on the test dispatcher. Give this
        // fixture the radio-group isolation that separate hosted application windows have.
        string analysisGroup = "SyntheticLegacyAnalysis_" + Guid.NewGuid().ToString("N");
        foreach (string name in new[] { "ContextToolButton", "TimeToolButton", "HighlightToolButton" })
            Control<RadioButton>(window, name).GroupName = analysisGroup;
        try
        {
            await (Task)Invoke(window, "LoadAnalysisFileAsync", legacyPath)!;
            Assert.Contains("복원 완료", Control<TextBlock>(window, "OperationStatus").Text);
            Assert.True(Control<RadioButton>(window, "TimeToolButton").IsChecked);
            Assert.Equal("alpha", Control<TextEditor>(window, "Editor").SelectedText);
            await (Task)Invoke(window, "SearchAsync")!;
            Assert.Equal("alpha", Control<TextEditor>(window, "Editor").SelectedText);
            Assert.Equal(Text, Field<LogData>(window, "data").Text);
            Assert.Equal(sourcePath, Field<LogData>(window, "data").SourcePath);
            Assert.All(Field<LogProjection>(window, "projection").SourceIndexes, line => Assert.InRange(line, 1, 3));
            Assert.True(Field<bool>(window, "contextActive"));
            Assert.Equal(new int?[] { 1 }, Field<IReadOnlySet<int?>>(window, "normalSelectedThreads"));
            Assert.Equal(1, Field<TimeAnchor>(window, "timeA").SourceLineIndex);
            Assert.Equal(3, Field<TimeAnchor>(window, "timeB").SourceLineIndex);
            Invoke(window, "CaptureActiveSession");
            var actual = (SavedAnalysis)Invoke(window, "CaptureAnalysis", Field<object>(window, "activeSession"))!;
            Assert.Equal(expected.Scope, actual.Scope);
            Assert.Equal(expected.Title, actual.Title); Assert.Equal(expected.SourceTitle, actual.SourceTitle);
            Assert.Equal(expected.Preset.Filter.Includes, actual.Preset.Filter.Includes); Assert.Equal(expected.Preset.Filter.Excludes, actual.Preset.Filter.Excludes);
            Assert.True(actual.Preset.Filter.RequireAll); Assert.True(actual.Preset.Filter.MatchCase);
            Assert.Equal(expected.Preset.Highlights, actual.Preset.Highlights);
            Assert.Equal(expected.Selection, actual.Selection); Assert.Equal(expected.Query, actual.Query);
            Assert.True(actual.SearchCase); Assert.True(actual.SearchWord); Assert.False(actual.SearchRegex);
            Assert.Equal(2, actual.SearchScope); Assert.Equal(expected.SearchRanges, actual.SearchRanges); Assert.Equal(expected.LastHit, actual.LastHit);
            Assert.Equal(expected.Back, actual.Back); Assert.Equal(expected.Forward, actual.Forward);
            Assert.Equal(expected.NormalPosition, actual.NormalPosition); Assert.Equal(expected.ContextLine, actual.ContextLine);
            Assert.Equal(expected.ContextRadius, actual.ContextRadius); Assert.Equal(expected.SeparationStart, actual.SeparationStart);
            Assert.Equal(expected.IncludesDraft, actual.IncludesDraft); Assert.Equal(expected.ExcludesDraft, actual.ExcludesDraft);
            Assert.True(actual.RequireAllDraft); Assert.True(actual.FilterCaseDraft); Assert.True(actual.FilterExpanded);
            Assert.Equal(expected.KeywordDraft, actual.KeywordDraft); Assert.Equal(expected.KeywordColor, actual.KeywordColor);
            Assert.Equal(expected.AnalysisTool, actual.AnalysisTool);

            await (Task)Invoke(window, "SaveAnalysisFileAsync", newPath)!;
            AssertNoRemovedFields(await File.ReadAllTextAsync(newPath));
            Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(sourcePath)); Assert.Equal(legacyBytes, await File.ReadAllBytesAsync(legacyPath));
        }
        finally { window.Close(); }
    });

    private static string LegacyJson(SavedAnalysis state, string legacyBookmarks)
    {
        var value = JsonSerializer.SerializeToNode(state)!.AsObject();
        value["Bookmarks"] = JsonNode.Parse(legacyBookmarks);
        value["BookmarksExpanded"] = true;
        return new JsonObject { ["Owner"] = "ThreadLogViewer.Analysis", ["Schema"] = 1, ["Kind"] = "state", ["Value"] = value }.ToJsonString();
    }
    private static void AssertNoRemovedFields(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("Schema").GetInt32());
        var value = document.RootElement.GetProperty("Value");
        Assert.False(value.TryGetProperty("Bookmarks", out _)); Assert.False(value.TryGetProperty("BookmarksExpanded", out _));
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-legacy-analysis", Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public string File(string name) => System.IO.Path.Combine(Path, name);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
