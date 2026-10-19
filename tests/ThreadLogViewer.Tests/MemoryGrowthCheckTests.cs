using System.IO;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class MemoryGrowthCheckTests
{
    [Fact]
    public void MarginsUseTheDocumentedFloorOrPercent()
    {
        Assert.Equal(MemoryGrowthCheck.ManagedHeapFloorBytes, MemoryGrowthCheck.ManagedHeapMargin(10L * 1024 * 1024));
        long heap = 5_000L * 1024 * 1024;
        Assert.Equal(heap / 50, MemoryGrowthCheck.ManagedHeapMargin(heap));
        Assert.True(MemoryGrowthCheck.ManagedHeapMargin(heap) > MemoryGrowthCheck.ManagedHeapFloorBytes);

        Assert.Equal(MemoryGrowthCheck.WorkingSetFloorBytes, MemoryGrowthCheck.WorkingSetMargin(100L * 1024 * 1024));
        long workingSet = 5_000L * 1024 * 1024;
        Assert.Equal(workingSet / 25, MemoryGrowthCheck.WorkingSetMargin(workingSet));
        Assert.True(MemoryGrowthCheck.WorkingSetMargin(workingSet) > MemoryGrowthCheck.WorkingSetFloorBytes);
    }

    [Fact]
    public void KeepsRisingRequiresThreeStrictStepsBeyondTheMargin()
    {
        const long margin = 10;
        Assert.False(MemoryGrowthCheck.KeepsRising(new long[] { 0, 100 }, margin));
        Assert.False(MemoryGrowthCheck.KeepsRising(new long[] { 0, 1, 10 }, margin));
        Assert.False(MemoryGrowthCheck.KeepsRising(new long[] { 0, 100, 90 }, margin));
        Assert.False(MemoryGrowthCheck.KeepsRising(new long[] { 0, 5, 5, 40 }, margin));
        Assert.True(MemoryGrowthCheck.KeepsRising(new long[] { 0, 1, 11 }, margin));
        Assert.True(MemoryGrowthCheck.KeepsRising(new long[] { 100, 110, 130, 160 }, margin));
    }

    [Fact]
    public void WarningUsesTheUnconfirmedPhraseAndDoesNotClaimALeak()
    {
        string? absent = MemoryGrowthCheck.WarningText(false, false, 10);
        Assert.True(absent is null);
        string? text = MemoryGrowthCheck.WarningText(true, true, 500);
        Assert.NotNull(text);
        Assert.Equal("500 MiB post-GC managed heap and working set: 누수 의심, 원인 미확인", text);
        Assert.Equal(MemoryGrowthCheck.SuspectedLeakUnconfirmed, "누수 의심, 원인 미확인");
        Assert.Contains(MemoryGrowthCheck.SuspectedLeakUnconfirmed, text, StringComparison.Ordinal);
        Assert.DoesNotContain("누수가 있", text, StringComparison.Ordinal);
        Assert.Equal(MemoryGrowthCheck.NoWarningVerdict, MemoryGrowthCheck.Verdict(false, false, 10));
        Assert.Contains("누수 없음으로 결론내지 않는다", MemoryGrowthCheck.NoWarningVerdict, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryBenchmarkDescribesTheOpenAndRefreshLayersAndRejectsEmptyRuns()
    {
        Assert.Contains("LogFileReader.ReadAsync", MemoryBenchmark.Layers, StringComparison.Ordinal);
        Assert.Contains("CancellableDocumentFactory.Create", MemoryBenchmark.Layers, StringComparison.Ordinal);
        Assert.Contains("SourceLineRemap.Create", MemoryBenchmark.Layers, StringComparison.Ordinal);
        Assert.Contains("FlaUI", MemoryBenchmark.Layers, StringComparison.Ordinal);
        Assert.Contains("Excluded", MemoryBenchmark.Layers, StringComparison.Ordinal);
        string dir = Path.Combine(Path.GetTempPath(), "tlv-memory-args-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(1, MemoryBenchmark.Run(dir, 0, 1));
        Assert.Equal(1, MemoryBenchmark.Run(dir, 1, 0));
        Assert.False(Directory.Exists(dir));
    }
}
