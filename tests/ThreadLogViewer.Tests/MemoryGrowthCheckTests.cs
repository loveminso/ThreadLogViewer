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
    public void ThreeRefreshSamplesAreNotEnoughToJudge()
    {
        string verdict = MemoryGrowthCheck.Verdict([0, 1, 100], margin: 1);
        Assert.Equal(MemoryGrowthCheck.InsufficientSamplesVerdict, verdict);
        Assert.Equal("판정 불가(표본 부족)", verdict);
        Assert.NotEqual(MemoryGrowthCheck.NoWarningVerdict, verdict);
        Assert.True(MemoryGrowthCheck.SlopeBytesPerRefresh([10L, 20L, 30L]) is null);
    }

    [Fact]
    public void SlopeIgnoresTheWarmUpAndWarnsOnlyAboveTheMargin()
    {
        long[] flatAfterWarmUp = [1_000, 5, 5, 5, 5];
        double warmSlope = MemoryGrowthCheck.SlopeBytesPerRefresh(flatAfterWarmUp) ?? throw new InvalidOperationException("slope");
        Assert.Equal(0d, warmSlope);
        Assert.Equal(MemoryGrowthCheck.NoWarningVerdict, MemoryGrowthCheck.Verdict(flatAfterWarmUp, margin: 0));

        long[] fittedRise = [999, 0, 10, 20, 30];
        double slope = MemoryGrowthCheck.SlopeBytesPerRefresh(fittedRise) ?? throw new InvalidOperationException("slope");
        double projected = MemoryGrowthCheck.ProjectedGrowth(fittedRise) ?? throw new InvalidOperationException("projected");
        Assert.Equal(10d, slope);
        Assert.Equal(40d, projected);
        Assert.Equal(MemoryGrowthCheck.NoWarningVerdict, MemoryGrowthCheck.Verdict(fittedRise, margin: 40));
        Assert.Equal(MemoryGrowthCheck.SuspectedLeakUnconfirmed, MemoryGrowthCheck.Verdict(fittedRise, margin: 39));
    }

    [Fact]
    public void RecordedHundredMiBManagedHeapAndPrivateBytesFollowTheSlopeRule()
    {
        // Managed-heap F5 samples and after-open heap 688547120 are the 100 MiB rows of run 39212578254.
        // That after-open heap selects the 32 MiB floor. Private-byte samples and after-open private
        // bytes 879276032 are the 100 MiB rows of run 39202580600 and select the 64 MiB floor.
        long[] managedHeap =
        [
            779266912, 779267224, 779399992, 779400312, 779400464,
            779400568, 779400720, 779401064, 779401256, 779401384
        ];
        long[] privateBytes =
        [
            1174745088, 1466609664, 1454526464, 1409257472, 1412227072,
            1418764288, 1407172608, 1392332800, 1413365760, 1426354176
        ];
        const long perRefresh = 30L * 1024 * 1024;
        long managedMargin = MemoryGrowthCheck.ManagedHeapMargin(688_547_120);
        long privateMargin = MemoryGrowthCheck.PrivateBytesMargin(879_276_032);
        Assert.Equal(MemoryGrowthCheck.ManagedHeapFloorBytes, managedMargin);
        Assert.Equal(MemoryGrowthCheck.WorkingSetFloorBytes, privateMargin);

        Assert.Equal(MemoryGrowthCheck.NoWarningVerdict, MemoryGrowthCheck.Verdict(managedHeap, managedMargin));
        Assert.Equal(MemoryGrowthCheck.SuspectedLeakUnconfirmed, MemoryGrowthCheck.Verdict(AddPerRefresh(managedHeap, perRefresh), managedMargin));
        Assert.Equal(MemoryGrowthCheck.NoWarningVerdict, MemoryGrowthCheck.Verdict(privateBytes, privateMargin));
        Assert.Equal(MemoryGrowthCheck.SuspectedLeakUnconfirmed, MemoryGrowthCheck.Verdict(AddPerRefresh(privateBytes, perRefresh), privateMargin));
    }

    [Fact]
    public void WarningUsesTheUnconfirmedPhraseAndDoesNotClaimALeak()
    {
        var clear = MemoryGrowthCheck.Judge(MemoryGrowthCheck.ManagedHeapMetric, [1, 1, 1, 1], margin: 32, MemoryGrowthCheck.ManagedHeapMarginRule);
        string? absent = MemoryGrowthCheck.WarningText([clear], 10);
        Assert.True(absent is null);
        var heap = new MemoryGrowthCheck.MetricResult(MemoryGrowthCheck.ManagedHeapMetric, 1, "", 1, 9, MemoryGrowthCheck.SuspectedLeakUnconfirmed);
        var privateBytes = new MemoryGrowthCheck.MetricResult(MemoryGrowthCheck.PrivateBytesMetric, 1, "", 1, 9, MemoryGrowthCheck.SuspectedLeakUnconfirmed);
        var committedLeak = new MemoryGrowthCheck.MetricResult(MemoryGrowthCheck.CommittedMetric, 1, "", 1, 9, MemoryGrowthCheck.SuspectedLeakUnconfirmed);
        string? text = MemoryGrowthCheck.WarningText([heap, privateBytes, committedLeak], 500);
        Assert.NotNull(text);
        Assert.Equal("500 MiB post-GC managed heap and private bytes: 누수 의심, 원인 미확인", text);
        Assert.DoesNotContain("GC committed", text, StringComparison.Ordinal);
        Assert.Equal(MemoryGrowthCheck.SuspectedLeakUnconfirmed, "누수 의심, 원인 미확인");
        Assert.Contains(MemoryGrowthCheck.SuspectedLeakUnconfirmed, text, StringComparison.Ordinal);
        Assert.DoesNotContain("누수가 있", text, StringComparison.Ordinal);
        Assert.Equal(MemoryGrowthCheck.NoWarningVerdict, clear.Verdict);
        Assert.Contains("누수 없음으로 결론내지 않는다", MemoryGrowthCheck.NoWarningVerdict, StringComparison.Ordinal);
        Assert.Equal(MemoryGrowthCheck.InsufficientSamplesVerdict, MemoryGrowthCheck.Summarize([MemoryGrowthCheck.InsufficientSamplesVerdict]));
        Assert.Null(MemoryGrowthCheck.WarningText([committedLeak], 500));
    }

    [Fact]
    public void CommittedAndPrivateMinusCommittedAreRecordedWithoutAVerdict()
    {
        long[] rising = [0, 10, 20, 30, 40];
        var committed = MemoryGrowthCheck.RecordOnly(
            MemoryGrowthCheck.CommittedMetric, rising, margin: 1, MemoryGrowthCheck.CommittedMarginRule);
        var residual = MemoryGrowthCheck.RecordOnly(
            MemoryGrowthCheck.PrivateMinusCommittedMetric, rising, margin: 1, MemoryGrowthCheck.PrivateMinusCommittedMarginRule);
        Assert.Equal("기록용(판정 안 함)", committed.Verdict);
        Assert.Equal(MemoryGrowthCheck.RecordedOnlyVerdict, committed.Verdict);
        Assert.Equal(MemoryGrowthCheck.RecordedOnlyVerdict, residual.Verdict);
        Assert.False(committed.Warning);
        Assert.False(residual.Warning);
        Assert.True(committed.ProjectedGrowthBytes > 1d);
        Assert.Null(MemoryGrowthCheck.WarningText([committed, residual], 100));
        Assert.Equal(
            "100 MiB post-GC managed heap bytes=779401384 slope*(N-1)=40 margin=33554432",
            MemoryGrowthCheck.FormatJudgedGrowthLine(100, MemoryGrowthCheck.ManagedHeapMetric, 779401384, 40d, 33554432));
        Assert.Equal(
            "10 MiB private bytes bytes=n/a slope*(N-1)=n/a margin=n/a",
            MemoryGrowthCheck.FormatJudgedGrowthLine(10, MemoryGrowthCheck.PrivateBytesMetric, null, null, null));
    }

    private static long[] AddPerRefresh(long[] samples, long bytesPerStep)
    {
        var boosted = new long[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            boosted[i] = samples[i] + (i + 1) * bytesPerStep;
        return boosted;
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
