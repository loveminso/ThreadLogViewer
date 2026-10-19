using System.Globalization;
using System.Linq;

namespace ThreadLogViewer.Tests;

public static class MemoryGrowthCheck
{
    public const long ManagedHeapFloorBytes = 32L * 1024 * 1024;
    public const long WorkingSetFloorBytes = 64L * 1024 * 1024;
    public const int MinimumRefreshSamples = 4;
    public const string SuspectedLeakUnconfirmed = "누수 의심, 원인 미확인";
    public const string NoWarningVerdict = "지속 증가 경고 없음. 누수 없음으로 결론내지 않는다.";
    public const string InsufficientSamplesVerdict = "판정 불가(표본 부족)";
    public const string RecordedOnlyVerdict = "기록용(판정 안 함)";
    public const string ManagedHeapMetric = "post-GC managed heap";
    public const string CommittedMetric = "GC committed";
    public const string PrivateBytesMetric = "private bytes";
    public const string PrivateMinusCommittedMetric = "private bytes minus GC committed";
    public const string ManagedHeapMarginRule = "max(32 MiB, after-open post-GC managed heap / 50)";
    public const string CommittedMarginRule = "max(32 MiB, after-open GC TotalCommittedBytes / 50)";
    public const string PrivateBytesMarginRule = "max(64 MiB, after-open private bytes / 25)";
    public const string PrivateMinusCommittedMarginRule = "max(64 MiB, absolute after-open (private bytes - GC committed) / 25)";
    public const string WorkingSetMarginRule = "max(64 MiB, after-open post-GC working set / 25)";

    public static long ManagedHeapMargin(long afterOpenManagedHeap) =>
        Math.Max(ManagedHeapFloorBytes, afterOpenManagedHeap / 50);

    public static long CommittedMargin(long afterOpenCommitted) =>
        Math.Max(ManagedHeapFloorBytes, afterOpenCommitted / 50);

    public static long PrivateBytesMargin(long afterOpenPrivate) =>
        Math.Max(WorkingSetFloorBytes, afterOpenPrivate / 25);

    public static long PrivateMinusCommittedMargin(long afterOpenPrivateMinusCommitted)
    {
        long magnitude = afterOpenPrivateMinusCommitted < 0 ? -afterOpenPrivateMinusCommitted : afterOpenPrivateMinusCommitted;
        return Math.Max(WorkingSetFloorBytes, magnitude / 25);
    }

    public static long WorkingSetMargin(long afterOpenWorkingSet) =>
        Math.Max(WorkingSetFloorBytes, afterOpenWorkingSet / 25);

    // F5 #1 is warm-up and is not part of the fit. The slope is bytes per refresh over F5 #2..#N.
    // x is the 1-based refresh number. Fewer than four F5 samples has no verdict other than 판정 불가.
    public static double? SlopeBytesPerRefresh(IReadOnlyList<long> refreshSamples)
    {
        if (refreshSamples.Count < MinimumRefreshSamples) return null;
        int count = refreshSamples.Count - 1;
        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
        for (int index = 1; index < refreshSamples.Count; index++)
        {
            double x = index + 1;
            double y = refreshSamples[index];
            sumX += x;
            sumY += y;
            sumXX += x * x;
            sumXY += x * y;
        }
        double denominator = count * sumXX - sumX * sumX;
        if (denominator == 0) return 0;
        return (count * sumXY - sumX * sumY) / denominator;
    }

    // Projected change across the whole F5 span, including the warm-up step: slope * (N - 1).
    public static double? ProjectedGrowth(IReadOnlyList<long> refreshSamples)
    {
        double? slope = SlopeBytesPerRefresh(refreshSamples);
        if (slope is null) return null;
        return slope.Value * (refreshSamples.Count - 1);
    }

    public static string Verdict(IReadOnlyList<long> refreshSamples, long margin)
    {
        double? projected = ProjectedGrowth(refreshSamples);
        if (projected is null) return InsufficientSamplesVerdict;
        if (projected.Value > margin) return SuspectedLeakUnconfirmed;
        return NoWarningVerdict;
    }

    public static MetricResult Judge(string name, IReadOnlyList<long> refreshSamples, long margin, string marginRule)
    {
        double? slope = SlopeBytesPerRefresh(refreshSamples);
        double? projected = slope is null ? null : slope.Value * (refreshSamples.Count - 1);
        string verdict;
        if (projected is not double growth)
            verdict = InsufficientSamplesVerdict;
        else if (growth > margin)
            verdict = SuspectedLeakUnconfirmed;
        else
            verdict = NoWarningVerdict;
        return new MetricResult(name, margin, marginRule, slope, projected, verdict);
    }

    public static MetricResult Insufficient(string name, string marginRule) =>
        new(name, null, marginRule, null, null, InsufficientSamplesVerdict);

    public static bool IsJudged(string name) =>
        name == ManagedHeapMetric || name == PrivateBytesMetric;

    // Bytes stay on the result. The verdict is not a growth judgment and does not warn.
    public static MetricResult RecordOnly(string name, IReadOnlyList<long>? refreshSamples, long? margin, string marginRule)
    {
        if (refreshSamples is null || margin is not long marginBytes)
            return new MetricResult(name, margin, marginRule, null, null, RecordedOnlyVerdict);
        MetricResult judged = Judge(name, refreshSamples, marginBytes, marginRule);
        return judged with { Verdict = RecordedOnlyVerdict };
    }

    public static string FormatJudgedGrowthLine(int mib, string metricName, long? bytes, double? slopeTimesCountMinusOne, long? marginBytes)
    {
        string byteText = bytes is long value ? value.ToString(CultureInfo.InvariantCulture) : "n/a";
        string growthText = slopeTimesCountMinusOne is double growth
            ? growth.ToString("0.################", CultureInfo.InvariantCulture)
            : "n/a";
        string marginText = marginBytes is long margin ? margin.ToString(CultureInfo.InvariantCulture) : "n/a";
        return $"{mib} MiB {metricName} bytes={byteText} slope*(N-1)={growthText} margin={marginText}";
    }

    public static string? WarningText(IEnumerable<MetricResult> results, int mib)
    {
        string[] names = results
            .Where(static result => result.Warning && IsJudged(result.Name))
            .Select(static result => result.Name)
            .ToArray();
        if (names.Length == 0) return null;
        return $"{mib} MiB {string.Join(" and ", names)}: {SuspectedLeakUnconfirmed}";
    }

    public static string Summarize(IReadOnlyList<string> verdicts)
    {
        if (verdicts.Count == 0) return InsufficientSamplesVerdict;
        if (verdicts.Contains(SuspectedLeakUnconfirmed)) return SuspectedLeakUnconfirmed;
        if (verdicts.All(static verdict => verdict == NoWarningVerdict)) return NoWarningVerdict;
        if (verdicts.All(static verdict => verdict == InsufficientSamplesVerdict)) return InsufficientSamplesVerdict;
        return string.Join("; ", verdicts);
    }

    public sealed record MetricResult(
        string Name,
        long? MarginBytes,
        string MarginRule,
        double? SlopeBytesPerRefresh,
        double? ProjectedGrowthBytes,
        string Verdict)
    {
        public bool Warning => Verdict == SuspectedLeakUnconfirmed;
    }
}
