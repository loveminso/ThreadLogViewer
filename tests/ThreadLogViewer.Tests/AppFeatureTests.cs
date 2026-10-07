using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class AppFeatureTests
{
    [Fact]
    public void SettingsRoundTripPersistsOnlyDisplayPreferencesAndUsesAtomicReplacement()
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        var defaults = store.Load();
        Assert.True(defaults.Success);
        Assert.True(defaults.CanSave);
        Assert.Equal(UiSettings.Default, defaults.Settings);
        var values = new UiSettings { Dark = false, Compact = true, WordWrap = true, FontSize = 18, PanelWidth = 420, ResultsHeight = 360, ResultsCollapsed = true, ThreadBackgrounds = false, LargeLinePreview = false };
        Assert.True(store.Save(values).Success);
        Assert.Equal(values, store.Load().Settings);
        Assert.True(store.Save(values with { FontSize = 20 }).Success);
        Assert.Equal(20, store.Load().Settings.FontSize);
        using var json = JsonDocument.Parse(File.ReadAllBytes(store.FilePath));
        Assert.Equal(SettingsStore.OwnerMarker, json.RootElement.GetProperty("Owner").GetString());
        Assert.Equal(SettingsStore.SchemaVersion, json.RootElement.GetProperty("Schema").GetInt32());
        Assert.Equal(9, json.RootElement.GetProperty("Settings").EnumerateObject().Count());
        Assert.Empty(Directory.EnumerateFiles(System.IO.Path.GetDirectoryName(store.FilePath)!, "*.tmp"));
    }

    [Theory]
    [InlineData("{\"UserFile\":\"synthetic notes\"}")]
    [InlineData("{broken synthetic JSON")]
    [InlineData("{\"Owner\":\"ThreadLogViewer.UiSettings\",\"Schema\":999,\"Settings\":{}}")]
    [InlineData("{\"Owner\":\"other synthetic application\",\"Schema\":1,\"Settings\":{}}")]
    public void UnknownCorruptAndFutureSettingsRemainByteForByteUnchanged(string text)
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.FilePath)!);
        byte[] original = Encoding.UTF8.GetBytes(text);
        File.WriteAllBytes(store.FilePath, original);
        var load = store.Load();
        Assert.False(load.Success);
        Assert.False(load.CanSave);
        Assert.Equal(UiSettings.Default, load.Settings);
        Assert.False(store.Save(new UiSettings { Dark = false }).Success);
        Assert.Equal(original, File.ReadAllBytes(store.FilePath));
    }

    [Fact]
    public void OversizedSettingsAreBoundedAndPreserved()
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.FilePath)!);
        byte[] original = Encoding.UTF8.GetBytes(new string('x', 65537));
        File.WriteAllBytes(store.FilePath, original);
        Assert.False(store.Load().CanSave);
        Assert.False(store.Save(UiSettings.Default).Success);
        Assert.Equal(original, File.ReadAllBytes(store.FilePath));
    }

    [Fact]
    public void InvalidPreferenceRangesUseSafeDefaults()
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath,
            "{\"Owner\":\"ThreadLogViewer.UiSettings\",\"Schema\":1,\"Settings\":{\"Dark\":false,\"FontSize\":900,\"PanelWidth\":2}}");
        var load = store.Load();
        Assert.True(load.Success);
        Assert.False(load.Settings.Dark);
        Assert.Equal(14, load.Settings.FontSize);
        Assert.Equal(295, load.Settings.PanelWidth);
        Assert.Equal(180, load.Settings.ResultsHeight);
        Assert.False(load.Settings.ResultsCollapsed);
        Assert.Equal(14, new UiSettings { FontSize = double.NaN }.Normalize().FontSize);
        Assert.Equal(295, new UiSettings { PanelWidth = double.PositiveInfinity }.Normalize().PanelWidth);
        Assert.Equal(180, new UiSettings { ResultsHeight = double.NaN }.Normalize().ResultsHeight);
        Assert.Equal(180, new UiSettings { ResultsHeight = double.PositiveInfinity }.Normalize().ResultsHeight);
        Assert.Equal(180, new UiSettings { ResultsHeight = 79 }.Normalize().ResultsHeight);
        Assert.Equal(180, new UiSettings { ResultsHeight = 1201 }.Normalize().ResultsHeight);
        Assert.Equal(80, new UiSettings { ResultsHeight = 80 }.Normalize().ResultsHeight);
        Assert.Equal(1200, new UiSettings { ResultsHeight = 1200 }.Normalize().ResultsHeight);
    }

    [Fact]
    public void SettingsSourceCollisionProtectsExistingAndNotYetCreatedSourcePath()
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Assert.False(store.Save(UiSettings.Default, store.FilePath).Success);
        Assert.False(File.Exists(store.FilePath));
        Assert.True(store.Save(UiSettings.Default).Success);
        byte[] original = File.ReadAllBytes(store.FilePath);
        string equivalent = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(store.FilePath)!, ".", "ui-settings.json");
        Assert.False(store.Save(new UiSettings { Dark = false }, equivalent).Success);
        Assert.Equal(original, File.ReadAllBytes(store.FilePath));
    }

    [Fact]
    public void HardLinkedSettingsAndSourceAliasesArePreserved()
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Assert.True(store.Save(UiSettings.Default).Success);
        byte[] original = File.ReadAllBytes(store.FilePath);
        string alias = System.IO.Path.Combine(folder.Path, "synthetic-source-alias.log");
        Assert.True(CreateHardLink(alias, store.FilePath, IntPtr.Zero), $"CreateHardLink failed: {Marshal.GetLastWin32Error()}");
        Assert.False(store.Load().CanSave);
        Assert.False(store.Save(new UiSettings { Dark = false }, alias).Success);
        Assert.False(store.Save(new UiSettings { Dark = false }).Success);
        Assert.Equal(original, File.ReadAllBytes(store.FilePath));
        Assert.Equal(original, File.ReadAllBytes(alias));
    }

    [Fact]
    public void FailedAtomicSaveRetainsExistingFileAndRemovesOwnedTemporaryFile()
    {
        using var folder = new SyntheticFolder();
        var store = new SettingsStore(folder.Path);
        Assert.True(store.Save(UiSettings.Default).Success);
        byte[] original = File.ReadAllBytes(store.FilePath);
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(store.Load().Success);
            Assert.False(store.Save(new UiSettings { Dark = false }).Success);
        }
        Assert.Equal(original, File.ReadAllBytes(store.FilePath));
        Assert.Empty(Directory.EnumerateFiles(System.IO.Path.GetDirectoryName(store.FilePath)!, "*.tmp"));
    }

    [Fact]
    public void ExistingUserDataFileIsNeverRemovedToCreateSettingsDirectory()
    {
        using var folder = new SyntheticFolder();
        string obstacle = System.IO.Path.Combine(folder.Path, "user-data");
        File.WriteAllText(obstacle, "synthetic user file");
        var store = new SettingsStore(folder.Path);
        Assert.False(store.Load().CanSave);
        Assert.False(store.Save(UiSettings.Default).Success);
        Assert.Equal("synthetic user file", File.ReadAllText(obstacle));
    }

    [Fact]
    public void SelectionCountsScalarsNewlinesRealEmptyRowsAndExcludesVirtualFinalRow()
    {
        const string text = "한😀\t\r\n\r\nZ\n";
        Assert.Equal(new SelectionSummary(1, 5), SelectionMetrics.Compute(text, 3, [new(0, 6)]));
        Assert.Equal(new SelectionSummary(2, 7), SelectionMetrics.Compute(text, 3, [new(0, 8)]));
        Assert.Equal(new SelectionSummary(3, 9), SelectionMetrics.Compute(text, 3, [new(0, text.Length)]));
        Assert.Equal(new SelectionSummary(0, 0), SelectionMetrics.Compute(text, 3, [new(text.Length, 0)]));
        Assert.Equal(new SelectionSummary(1, 2), SelectionMetrics.Compute(text, 3, [new(6, 2)]));
    }

    [Fact]
    public void SelectionCountsTheUnionOfOverlappingAndDiscontinuousSegments()
    {
        const string text = "a😀b\nsecond\nthird";
        Assert.Equal(new SelectionSummary(2, 4), SelectionMetrics.Compute(text, 3, [new(0, 3), new(1, 3), new(5, 1)]));
        Assert.Equal(new SelectionSummary(1, 1), SelectionMetrics.Compute(text, [0, 5, 12], 3, [new(2, 1)]));
        Assert.Equal(new SelectionSummary(1, 1), SelectionMetrics.Compute(text, 3, [new(1, 1), new(2, 1)]));
        Assert.Equal(new SelectionSummary(2, 2), SelectionMetrics.Compute("a\r\nb\rc", 3, [new(0, 1), new(3, 1)]));
    }

    [Fact]
    public void LargeSelectionHonorsCancellationWithoutMaterializingSelectedText()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => SelectionMetrics.Compute("synthetic", 1, [new(0, 9)], cancelled.Token));
    }

    [Fact]
    public void SelectionSourceRangesSplitHiddenGapsAndPreserveEndExclusiveNewlineBoundaries()
    {
        const string original = "[000:00:01] [T1] first\r\nbody\r\n[000:00:02] [T2] hidden\n[000:00:03] [T1] last\n";
        var source = LogParser.Parse(original, null, "synthetic UTF-8");
        var projection = LogProjection.Create(source, [1]);
        int firstLength = source.GetEntryRange(0).Length;
        int thirdStart = source.GetEntryRange(2).Offset;
        var all = SelectionSourceRanges.Map(projection, [new(0, projection.Text.Length)]);
        Assert.Equal(new[] { new SourceTextRange(0, firstLength), source.GetEntryRange(2) }, all);
        Assert.Equal(all, SelectionSourceRanges.Map(projection, [new(0, projection.Text.Length), new(1, firstLength)]));
        Assert.Equal(new[] { new SourceTextRange(0, firstLength) }, SelectionSourceRanges.Map(projection, [new(0, firstLength)]));
        Assert.Equal(new[] { new SourceTextRange(thirdStart, 1) }, SelectionSourceRanges.Map(projection, [new(firstLength, 1)]));
        Assert.Equal(new[] { new SourceTextRange(firstLength - 1, 1), new SourceTextRange(thirdStart, 1) },
            SelectionSourceRanges.Map(projection, [new(firstLength - 1, 2)]));
        Assert.Empty(SelectionSourceRanges.Map(projection, [new(projection.Text.Length, 0)]));
    }

    [Fact]
    public void SelectionSourceRangesMergeOverlappingSelectionsWithoutSearchingTheGap()
    {
        var source = LogParser.Parse("[000:00:01] [T1] synthetic\nbody\n[000:00:02] [T2] other", null, "synthetic UTF-8");
        var projection = LogProjection.Create(source, [1]);
        Assert.Equal(new[] { new SourceTextRange(1, 5) }, SelectionSourceRanges.Map(projection, [new(1, 3), new(2, 4)]));
        Assert.Equal(new[] { new SourceTextRange(1, 2), new SourceTextRange(5, 2) },
            SelectionSourceRanges.Map(projection, [new(5, 2), new(1, 2)]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SelectionSourceRanges.Map(projection, [], cancellation.Token));
    }

    [Fact]
    public void HighlightQueryRetainsLongOverlapClipsViewportAndSkipsZeroLengthHits()
    {
        var index = new HighlightIndex([new(100, 2, 1), new(0, 1000, 0), new(150, 3, 2), new(200, 0, 3)]);
        Assert.Equal(3, index.Count);
        Assert.Equal(new[] { new HighlightSpan(500, 10, 0) }, index.Query(500, 510));
        Assert.Equal(new[] { new HighlightSpan(101, 2, 0), new HighlightSpan(101, 1, 1) }, index.Query(101, 103));
        Assert.Empty(index.Query(1000, 1100));
        Assert.Empty(index.Query(10, 10));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-app-features", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
