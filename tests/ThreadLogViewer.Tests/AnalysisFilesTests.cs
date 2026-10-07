using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class AnalysisFilesTests
{
    private const string Text = "[000:00:01] [T1] synthetic alpha\r\nsynthetic body\n[000:00:02] [T2] synthetic beta\r[000:00:03] [T1] synthetic alpha finish";
    [Fact]
    public async Task PresetRoundTripKeepsTermsThreadSelectionAndDisabledColors()
    {
        using var folder = new Folder(); string path = folder.File("preset.json");
        var preset = new AnalysisPreset { Filter = new([" alpha"], ["heartbeat"], true, true), AllThreads = false, Threads = [1, null], Highlights = [new("alpha", 3, false)] };
        await AnalysisFiles.SavePresetAsync(preset, path); var loaded = await AnalysisFiles.ReadPresetAsync(path);
        Assert.Equal(preset.Filter.Includes, loaded.Filter.Includes); Assert.Equal(preset.Filter.Excludes, loaded.Filter.Excludes);
        Assert.True(loaded.Filter.RequireAll); Assert.True(loaded.Filter.MatchCase); Assert.False(loaded.AllThreads);
        Assert.Equal(preset.Threads, loaded.Threads); Assert.Equal(preset.Highlights, loaded.Highlights);
    }
    [Fact]
    public async Task AnalysisSaveProtectsBothSourceAndExistingFilesAndCancelledSaveCreatesNothing()
    {
        using var folder = new Folder(); string sourcePath = folder.File("synthetic-source.log");
        await File.WriteAllTextAsync(sourcePath, Text); var source = await LogFileReader.ReadAsync(sourcePath);
        var state = new SavedAnalysis { Source = AnalysisFiles.Stamp(source), SourcePath = sourcePath };
        await Assert.ThrowsAsync<IOException>(() => AnalysisFiles.SaveAnalysisAsync(state, sourcePath));
        string existing = folder.File("existing.json"); await File.WriteAllTextAsync(existing, "synthetic existing file");
        await Assert.ThrowsAsync<IOException>(() => AnalysisFiles.SaveAnalysisAsync(state, existing));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); string target = folder.File("cancelled.json");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AnalysisFiles.SaveAnalysisAsync(state, target, cancelled.Token));
        Assert.False(File.Exists(target)); Assert.Equal(Text, await File.ReadAllTextAsync(sourcePath));
        Assert.Equal("synthetic existing file", await File.ReadAllTextAsync(existing)); Assert.Empty(Directory.GetFiles(folder.Path, ".threadlog-analysis-*.tmp"));
    }
    [Fact]
    public async Task WrongOwnerFutureSchemaCrossKindAndInvalidValuesAreRejected()
    {
        using var folder = new Folder(); string path = folder.File("invalid.json");
        foreach (string json in new[] { "{}", "{\"Owner\":\"ThreadLogViewer.Analysis\",\"Schema\":2,\"Kind\":\"preset\",\"Value\":{}}", "{\"Owner\":\"another-app\",\"Schema\":1,\"Kind\":\"preset\",\"Value\":{}}" })
        { await File.WriteAllTextAsync(path, json); await Assert.ThrowsAsync<InvalidDataException>(() => AnalysisFiles.ReadPresetAsync(path)); }
        await File.WriteAllTextAsync(path, "{\"Owner\":\"ThreadLogViewer.Analysis\",\"Schema\":1,\"Kind\":\"preset\",\"Value\":{\"Highlights\":[{\"Phrase\":\"alpha\",\"Color\":99}]}}");
        await Assert.ThrowsAsync<InvalidDataException>(() => AnalysisFiles.ReadPresetAsync(path));
        string valid = folder.File("valid.json"); await AnalysisFiles.SavePresetAsync(new(), valid);
        await Assert.ThrowsAsync<InvalidDataException>(() => AnalysisFiles.ReadAnalysisAsync(valid));
    }
    [Fact]
    public void SourceFingerprintIncludesOriginalLineEndingsAndStateRejectsInvalidAnchors()
    {
        var source = LogParser.ParsePastedText(Text); var other = LogParser.ParsePastedText(Text.Replace("\r\n", "\n"));
        Assert.NotEqual(AnalysisFiles.Stamp(source), AnalysisFiles.Stamp(other));
        var valid = new SavedAnalysis { Source = AnalysisFiles.Stamp(source), PastedText = Text };
        Assert.Throws<InvalidDataException>(() => AnalysisFiles.Validate(valid with { TimeA = source.Lines.Count }));
        Assert.Throws<InvalidDataException>(() => AnalysisFiles.Validate(valid with { Selection = [new(0, source.Text.Length + 1)] }));
        Assert.Throws<InvalidDataException>(() => AnalysisFiles.Validate(valid with { Position = new(0, 1, 0, 0, double.NaN) }));
    }
    [Fact]
    public Task PastedAnalysisRestoresAfterClosingIncludingFilterHighlightsAndSelection() => InSta(async () =>
    {
        using var folder = new Folder(); string statePath = folder.File("state.json");
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text); await (Task)Invoke(window, "SetThreadsAsync", (Func<ThreadItem, bool>)(thread => thread.Id == 1))!;
            await (Task)Invoke(window, "FilterAsync", new EntryFilter(["alpha"], []), null)!;
            var editor = Control<TextEditor>(window, "Editor"); editor.Select(editor.Text.IndexOf("alpha", StringComparison.Ordinal), 5);
            Control<TextBox>(window, "KeywordBox").Text = "alpha"; Invoke(window, "AddKeyword_Click", window, new RoutedEventArgs());
            Control<TextBox>(window, "IncludeBox").Text = "unapplied synthetic draft";
            await (Task)Invoke(window, "SaveAnalysisFileAsync", statePath)!;
            Assert.True(File.Exists(statePath)); window.Close();
        }
        finally { window.Close(); }
        var restored = new MainWindow(folder.Path, false);
        try
        {
            await (Task)Invoke(restored, "LoadAnalysisFileAsync", statePath)!;
            Assert.Equal(Text, Field<LogData>(restored, "data").Text);
            Assert.Equal(new int?[] { 1 }, Field<LogProjection>(restored, "projection").SelectedThreads);
            Assert.Equal("unapplied synthetic draft", Control<TextBox>(restored, "IncludeBox").Text);
            Assert.Equal("alpha", Control<TextEditor>(restored, "Editor").SelectedText);
            Assert.Contains("복원 완료", Control<TextBlock>(restored, "OperationStatus").Text);
        }
        finally { restored.Close(); }
    });
    [Fact]
    public Task LoadingPresetAppliesOnlyItsConditionsAndKeepsSourceAndSelection() => InSta(async () =>
    {
        using var folder = new Folder(); string path = folder.File("preset.json");
        await AnalysisFiles.SavePresetAsync(new() { Filter = new(["alpha"], []), AllThreads = false, Threads = [1], Highlights = [new("alpha", 4)] }, path);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, Text);
            var editor = Control<TextEditor>(window, "Editor"); editor.Select(Text.IndexOf("alpha", StringComparison.Ordinal), 5);
            await (Task)Invoke(window, "LoadPresetFileAsync", path)!;
            Assert.Equal("alpha", editor.SelectedText);
            Assert.Equal(new int?[] { 1 }, Field<LogProjection>(window, "projection").SelectedThreads);
            Assert.Equal(new[] { "alpha" }, Field<EntryFilter>(window, "appliedFilter").Includes);
            Assert.Equal("alpha", Control<TextBox>(window, "IncludeBox").Text);
            Assert.Equal(Text, Field<LogData>(window, "data").Text);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task ScopedContextAnalysisRestoresNormalThreadsTimeAnchorsAndNavigation() => InSta(async () =>
    {
        using var folder = new Folder(); string path = folder.File("scoped.json");
        var source = LogParser.ParsePastedText(Text);
        var saved = new SavedAnalysis
        {
            Source = AnalysisFiles.Stamp(source), PastedText = Text, Scope = new(1, 3),
            Preset = new() { AllThreads = false, Threads = [1] }, ContextLine = 2, ContextRadius = 1,
            Position = new(2, 1, 1, 0, 0), NormalPosition = new(1, 1, 1, 0, 0),
            TimeA = 1, TimeB = 3,
            Back = [new(new(1, 1, 1, 0, 0), null)], Forward = [new(new(3, 1, 1, 0, 0), 3)]
        };
        await AnalysisFiles.SaveAnalysisAsync(saved, path);
        var window = new MainWindow(folder.Path, false);
        try
        {
            await (Task)Invoke(window, "LoadAnalysisFileAsync", path)!;
            Assert.True(Field<bool>(window, "contextActive"));
            Assert.Equal(new int?[] { 1 }, Field<IReadOnlySet<int?>>(window, "normalSelectedThreads"));
            Assert.Equal(1, Field<TimeAnchor>(window, "timeA").SourceLineIndex);
            Assert.Equal(3, Field<TimeAnchor>(window, "timeB").SourceLineIndex);
            var view = Field<LogProjection>(window, "projection");
            Assert.All(view.SourceIndexes, line => Assert.InRange(line, 1, 3));
            await (Task)Invoke(window, "NavigateHistoryAsync", false)!;
            Assert.False(Field<bool>(window, "contextActive"));
            Assert.Equal(new int?[] { 1 }, Field<LogProjection>(window, "projection").SelectedThreads);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task ApplyingPresetKeepsInputsChangedDuringPreparation() => InSta(async () =>
    {
        using var folder = new Folder(); string path = folder.File("preset.json");
        await AnalysisFiles.SavePresetAsync(new() { Filter = new(["alpha"], []) }, path);
        var window = new MainWindow(folder.Path, false);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pending = null; int checkpoints = 0;
        try
        {
            await Load(window, Text);
            typeof(MainWindow).GetField("presetPrepareCheckpoint", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window,
                (Func<CancellationToken, Task>)(async _ => { if (Interlocked.Increment(ref checkpoints) == 1) started.SetResult(); await release.Task; }));
            pending = (Task)Invoke(window, "LoadPresetFileAsync", path)!; await started.Task;
            Control<TextBox>(window, "SearchBox").Text = "alpha finish";
            var editor = Control<TextEditor>(window, "Editor"); editor.Select(Text.LastIndexOf("alpha", StringComparison.Ordinal), 5);
            release.SetResult(); await pending;
            Assert.True(checkpoints >= 2);
            Assert.Equal("alpha finish", Control<TextBox>(window, "SearchBox").Text);
            Assert.Equal("alpha", editor.SelectedText);
            Assert.Equal(new[] { "alpha" }, Field<EntryFilter>(window, "appliedFilter").Includes);
        }
        finally { release.TrySetResult(); if (pending is not null) await pending; window.Close(); }
    });
    [Fact]
    public Task EqualTextInAnotherFileDoesNotReplaceTheSavedSourceIdentity() => InSta(async () =>
    {
        using var folder = new Folder(); string first = folder.File("synthetic-a.log"), second = folder.File("synthetic-b.log");
        await File.WriteAllTextAsync(first, Text); await File.WriteAllTextAsync(second, Text);
        var sourceA = await LogFileReader.ReadAsync(first); var sourceB = await LogFileReader.ReadAsync(second);
        string fileState = folder.File("file-state.json"), pastedState = folder.File("pasted-state.json");
        await AnalysisFiles.SaveAnalysisAsync(new() { Source = AnalysisFiles.Stamp(sourceA), SourcePath = first }, fileState);
        await AnalysisFiles.SaveAnalysisAsync(new() { Source = AnalysisFiles.Stamp(sourceA), PastedText = Text }, pastedState);
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await (Task<bool>)Invoke(window, "LoadAsync",
                (Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>>)((_, _) => Task.FromResult(sourceB)), "synthetic B", "synthetic failure")!);
            await (Task)Invoke(window, "LoadAnalysisFileAsync", fileState)!;
            Assert.Equal(first, Field<LogData>(window, "data").SourcePath);
            Assert.NotSame(sourceB, Field<LogData>(window, "data"));
            await (Task)Invoke(window, "LoadAnalysisFileAsync", pastedState)!;
            Assert.Null(Field<LogData>(window, "data").SourcePath);
            Assert.Equal(Text, Field<LogData>(window, "data").Text);
            Assert.Equal(Text, await File.ReadAllTextAsync(first)); Assert.Equal(Text, await File.ReadAllTextAsync(second));
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task ChangedSourceAndMalformedStatePreserveTheCurrentTab() => InSta(async () =>
    {
        using var folder = new Folder(); string sourcePath = folder.File("synthetic-source.log"), statePath = folder.File("state.json");
        await File.WriteAllTextAsync(sourcePath, Text); var original = await LogFileReader.ReadAsync(sourcePath);
        await AnalysisFiles.SaveAnalysisAsync(new() { Source = AnalysisFiles.Stamp(original), SourcePath = sourcePath }, statePath);
        await File.WriteAllTextAsync(sourcePath, Text + "\nsynthetic changed source");
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Load(window, "synthetic current log"); var before = Field<LogData>(window, "data");
            await (Task)Invoke(window, "LoadAnalysisFileAsync", statePath)!;
            Assert.Same(before, Field<LogData>(window, "data")); Assert.Contains("원문과 내용이 다릅니다", Control<TextBlock>(window, "OperationStatus").Text);
            string malformed = folder.File("malformed.json"); await File.WriteAllTextAsync(malformed, "{\"Owner\":false}");
            await (Task)Invoke(window, "LoadAnalysisFileAsync", malformed)!;
            Assert.Same(before, Field<LogData>(window, "data"));
        }
        finally { window.Close(); }
    });
    private static async Task Load(MainWindow window, string text) => Assert.True(await (Task<bool>)Invoke(window, "LoadAsync",
        (Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>>)((token, progress) => Task.FromResult(LogParser.ParsePastedText(text, token, progress))), "synthetic load", "synthetic failure")!);
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-analysis-files", Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public string File(string name) => System.IO.Path.Combine(Path, name);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
